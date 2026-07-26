using Unity.Netcode;
using UnityEngine;

/* ShipEnemyAI
A copy of EnemyAI for pirates standing on a ship deck. EnemyAI's wander/home
box is a plain axis-aligned rectangle, which doesn't fit an oval-ish deck --
sizing a rectangle to fit inside the hull either leaves most of the deck
unused or lets pirates clip through the rail into the water. This variant
instead confines wandering to an assigned Collider2D (the ship's deck-shaped
trigger, e.g. "ShipHitBox"), so pirates roam the actual deck shape.

Deliberately a separate script, not a subclass/edit of EnemyAI -- EnemyAI's
rectangle boxes are still exactly right for land-based enemies elsewhere in
the game, and its fields are private, so this duplicates the whole
wander/chase/return state machine rather than touching the shared script. */
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(NetworkObject))]
public sealed class ShipEnemyAI : NetworkBehaviour
{
    private enum State
    {
        Wander,
        Chase,
        Return,
        Dead
    }

    [Header("References")]
    [Tooltip("Existing health script. Auto-found on this object if left empty.")]
    [SerializeField] private Enemy enemy;
    [Tooltip("Sword script on a child object. Auto-found in children if empty.")]
    [SerializeField] private EnemyAttack enemyAttack;

    [Header("Deck Bounds")]
    [Tooltip(
        "The ship's deck-shaped Collider2D (e.g. the ShipHitBox trigger " +
        "polygon). Wandering/returning is confined inside this shape " +
        "instead of a rectangle, so pirates stay on deck. Required -- " +
        "without one this behaves like a rectangle-less EnemyAI and the " +
        "pirate won't wander at all."
    )]
    [SerializeField] private Collider2D deckBounds;

    [Tooltip(
        "Safety cap on how many random points we try per wander pick " +
        "before giving up and using the last sampled point anyway."
    )]
    [SerializeField] private int maxWanderSampleAttempts = 12;

    [Header("Home / Wander (small, invisible)")]
    [Tooltip("Center of the wander/return search. If empty, the enemy's start position is used.")]
    [SerializeField] private Transform homeCenter;
    [SerializeField] private float wanderSpeed = 1.5f;
    [SerializeField] private float wanderPointTolerance = 0.15f;
    [Tooltip("Min/max seconds the enemy pauses after reaching a wander point.")]
    [SerializeField] private Vector2 wanderPauseRange = new Vector2(0.5f, 2f);

    [Header("Line-of-Sight / Aggro Box (big, rectangle)")]
    [Tooltip(
        "Detection range around home for spotting players -- kept as a " +
        "simple rectangle since it's just a detection radius, not a " +
        "physical bound. Chasing can still carry a pirate toward a " +
        "player standing off this ship; boarding/leash-back handling for " +
        "that case comes later."
    )]
    [SerializeField] private Vector2 aggroAreaSize = new Vector2(12f, 12f);
    [Tooltip("Extra margin added to the aggro box while chasing so the enemy does not flip-flop at the edge.")]
    [SerializeField] private float chaseExitBuffer = 0.75f;
    [Tooltip("Optional: require an unobstructed straight line to the player.")]
    [SerializeField] private bool useLineOfSight = false;
    [SerializeField] private LayerMask lineOfSightBlockers;

    [Header("Chase / Attack")]
    [SerializeField] private float chaseSpeed = 3f;
    [Tooltip("Distance at which the enemy stops and swings.")]
    [SerializeField] private float attackRange = 1.1f;

    [Header("Target Scan")]
    [Tooltip("How often (seconds) the enemy re-scans the scene for players.")]
    [SerializeField] private float scanInterval = 0.25f;

    [Header("Stuck Handling")]
    [Tooltip("If the enemy can't make progress for this long (e.g. blocked by an obstacle collider), it gives up on the current point and picks a new one so it doesn't loiter against a wall.")]
    [SerializeField] private float stuckRerollTime = 0.4f;

    private Rigidbody2D rb;
    private State state = State.Wander;

    private Vector2 homePosition;
    private Vector2 wanderTarget;
    private float wanderPauseTimer;

    private Transform target;
    private PlayerHealth[] cachedPlayers;
    private float scanTimer;

    private Vector2 lastPosition;
    private float movedLastTick;
    private float stuckTimer;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;

        if (enemy == null)
        {
            enemy = GetComponent<Enemy>();
        }

        if (enemyAttack == null)
        {
            enemyAttack = GetComponentInChildren<EnemyAttack>(true);
        }
    }

    /// <summary>
    /// Called by EnemyShipApproach when it carries this pirate's body along
    /// with the ship's own movement (see EnemyShipApproach.LateUpdate).
    /// homePosition/wanderTarget are cached absolute world positions -- if
    /// only the Rigidbody2D gets nudged and these stay put, the pirate
    /// keeps trying to walk to a point that's drifted out from under the
    /// moving deck, which reads as "stuck"/frozen relative to the ship it's
    /// standing on (it may even wander itself off the moving deck chasing a
    /// stale target). Shifting these by the same delta keeps its internal
    /// wander state consistent with where its body actually got moved to.
    /// </summary>
    public void ApplyExternalDelta(Vector2 delta)
    {
        homePosition += delta;
        wanderTarget += delta;
        lastPosition += delta;
    }

    /// <summary>
    /// Called by EnemyShipSpawner right after it spawns this pirate onto a
    /// runtime-spawned ship, since Deck Bounds can't be wired in the
    /// Inspector ahead of time for a ship that doesn't exist yet.
    /// </summary>
    public void SetDeckBoundsServer(Collider2D bounds)
    {
        deckBounds = bounds;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer)
        {
            return;
        }

        // Force the Rigidbody2D's cached position back in sync with the
        // Transform before reading it. A freshly-instantiated pirate that
        // gets moved into this scene at runtime (see
        // NetworkEnemyShipSpawner2D, which uses
        // SceneManager.MoveGameObjectToScene) can have rb.position lag a
        // physics step behind transform.position -- reading rb.position
        // here would then silently ignore wherever we actually placed it.
        rb.position = transform.position;

        homePosition = homeCenter != null
            ? (Vector2)homeCenter.position
            : rb.position;

        lastPosition = rb.position;

        PickNewWanderTarget();
        RefreshPlayerCache();

        Debug.Log(
            $"[Ship Enemy AI] {name} OnNetworkSpawn -- " +
            $"transform.position={(Vector2)transform.position}, " +
            $"rb.position={rb.position}, homePosition={homePosition}, " +
            $"deckBounds assigned={(deckBounds != null)}, " +
            $"deckBounds type={(deckBounds != null ? deckBounds.GetType().Name : "n/a")}, " +
            $"deckBounds.bounds={(deckBounds != null ? deckBounds.bounds.ToString() : "n/a")}, " +
            $"homePosition inside deckBounds={IsInsideDeckBounds(homePosition)}, " +
            $"wanderTarget={wanderTarget}.",
            this
        );
    }

    private void FixedUpdate()
    {
        if (!IsSpawned || !IsServer)
        {
            return;
        }

        if (enemy != null && !enemy.IsAlive)
        {
            state = State.Dead;
        }

        if (state == State.Dead)
        {
            return;
        }

        // How far the body actually moved since the last physics step. If a
        // collider is blocking us, this stays near zero even though we keep
        // calling MovePosition -- that's how we detect "stuck".
        movedLastTick = ((Vector2)rb.position - lastPosition).magnitude;
        lastPosition = rb.position;

        scanTimer -= Time.fixedDeltaTime;
        if (scanTimer <= 0f)
        {
            RefreshPlayerCache();
            scanTimer = scanInterval;
        }

        switch (state)
        {
            case State.Wander:
                TickWander();
                break;

            case State.Chase:
                TickChase();
                break;

            case State.Return:
                TickReturn();
                break;
        }
    }

    // States

    private void TickWander()
    {
        Transform spotted = FindTargetInAggro(0f);
        if (spotted != null)
        {
            target = spotted;
            state = State.Chase;
            return;
        }

        if (wanderPauseTimer > 0f)
        {
            wanderPauseTimer -= Time.fixedDeltaTime;
            return;
        }

        MoveTowards(wanderTarget, wanderSpeed);

        if (Reached(wanderTarget, wanderPointTolerance))
        {
            wanderPauseTimer = Random.Range(
                wanderPauseRange.x,
                wanderPauseRange.y
            );
            PickNewWanderTarget();
            stuckTimer = 0f;
        }
        else if (IsStuck(wanderSpeed))
        {
            // Blocked by something (usually the deck's own rail collider).
            // Give up on this point and try another one.
            PickNewWanderTarget();
            stuckTimer = 0f;
        }
    }

    private void TickChase()
    {
        // Lost the target? (left the LOS box or died)
        if (target == null ||
            IsTargetDead(target) ||
            !IsInAggroBox(target.position, chaseExitBuffer))
        {
            target = null;
            state = State.Return;
            return;
        }

        float distance = Vector2.Distance(
            rb.position,
            target.position
        );

        if (distance <= attackRange)
        {
            // In range: stop and swing, exactly like a player click-attack.
            if (enemyAttack != null)
            {
                enemyAttack.TryAttack();
            }
        }
        else
        {
            MoveTowards(target.position, chaseSpeed);
        }
    }

    private void TickReturn()
    {
        // A player can re-aggro us on the way back.
        Transform spotted = FindTargetInAggro(0f);
        if (spotted != null)
        {
            target = spotted;
            state = State.Chase;
            return;
        }

        MoveTowards(homePosition, wanderSpeed);

        if (IsInsideDeckBounds(rb.position))
        {
            wanderPauseTimer = 0f;
            PickNewWanderTarget();
            stuckTimer = 0f;
            state = State.Wander;
        }
        else if (IsStuck(wanderSpeed))
        {
            // Can't reach home (blocked). Stop fighting the wall and just
            // wander from wherever we are.
            wanderPauseTimer = 0f;
            PickNewWanderTarget();
            stuckTimer = 0f;
            state = State.Wander;
        }
    }

    // Targeting

    private void RefreshPlayerCache()
    {
        cachedPlayers = FindObjectsByType<PlayerHealth>(
            FindObjectsSortMode.None
        );
    }

    private Transform FindTargetInAggro(float buffer)
    {
        if (cachedPlayers == null)
        {
            return null;
        }

        Transform best = null;
        float bestSqr = float.MaxValue;

        foreach (PlayerHealth player in cachedPlayers)
        {
            if (player == null || !player.IsAlive)
            {
                continue;
            }

            Vector2 pos = player.transform.position;

            if (!IsInAggroBox(pos, buffer))
            {
                continue;
            }

            if (useLineOfSight && !HasLineOfSight(pos))
            {
                continue;
            }

            float sqr = ((Vector2)rb.position - pos).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = player.transform;
            }
        }

        return best;
    }

    private bool IsInAggroBox(Vector2 pos, float buffer)
    {
        Vector2 half = aggroAreaSize * 0.5f + Vector2.one * buffer;
        return Mathf.Abs(pos.x - homePosition.x) <= half.x &&
               Mathf.Abs(pos.y - homePosition.y) <= half.y;
    }

    // True if pos is inside the assigned deck collider. Falls back to "true"
    // if no collider was assigned so a mis-set-up pirate doesn't get stuck
    // forever oscillating between Wander/Return.
    //
    // EdgeCollider2D is a special case: it's an open line with no interior,
    // so Collider2D.OverlapPoint never reports "inside" for one. Decks are
    // often traced as an EdgeCollider2D outline rather than a solid
    // PolygonCollider2D, so when that's what's assigned we do our own
    // point-in-polygon test against its raw points instead of relying on
    // the built-in overlap check.
    private bool IsInsideDeckBounds(Vector2 pos)
    {
        if (deckBounds == null)
        {
            return true;
        }

        if (deckBounds is EdgeCollider2D edge)
        {
            return IsInsideEdgePolygon(edge, pos);
        }

        return deckBounds.OverlapPoint(pos);
    }

    private static bool IsInsideEdgePolygon(EdgeCollider2D edge, Vector2 worldPoint)
    {
        Vector2 local = edge.transform.InverseTransformPoint(worldPoint);
        Vector2[] points = edge.points;

        if (points == null || points.Length < 3)
        {
            return false;
        }

        // Standard ray-casting point-in-polygon test, treating the point
        // list as an implicitly closed loop (last point connects back to
        // the first) regardless of whether the traced outline's ends
        // actually meet exactly.
        bool inside = false;

        for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
        {
            Vector2 pi = points[i];
            Vector2 pj = points[j];

            bool straddles = (pi.y > local.y) != (pj.y > local.y);

            if (straddles)
            {
                float edgeX = (pj.x - pi.x) *
                    (local.y - pi.y) / (pj.y - pi.y) + pi.x;

                if (local.x < edgeX)
                {
                    inside = !inside;
                }
            }
        }

        return inside;
    }

    private bool HasLineOfSight(Vector2 targetPos)
    {
        Vector2 origin = rb.position;
        Vector2 dir = targetPos - origin;
        float dist = dir.magnitude;

        if (dist <= 0.01f)
        {
            return true;
        }

        RaycastHit2D hit = Physics2D.Raycast(
            origin,
            dir / dist,
            dist,
            lineOfSightBlockers
        );

        return hit.collider == null;
    }

    private bool IsTargetDead(Transform t)
    {
        PlayerHealth ph = t.GetComponentInParent<PlayerHealth>();
        return ph != null && !ph.IsAlive;
    }

    // Movement helpers

    private void MoveTowards(Vector2 destination, float speed)
    {
        Vector2 next = Vector2.MoveTowards(
            rb.position,
            destination,
            speed * Time.fixedDeltaTime
        );

        rb.MovePosition(next);
    }

    private bool Reached(Vector2 point, float tolerance)
    {
        return ((Vector2)rb.position - point).sqrMagnitude
            <= tolerance * tolerance;
    }

    // True once the enemy has been unable to move (blocked) for stuckRerollTime.
    private bool IsStuck(float speed)
    {
        float minProgress = speed * Time.fixedDeltaTime * 0.25f;

        if (movedLastTick < minProgress)
        {
            stuckTimer += Time.fixedDeltaTime;
        }
        else
        {
            stuckTimer = 0f;
        }

        return stuckTimer >= stuckRerollTime;
    }

    // Picks a wander point somewhere inside the deck collider. Samples
    // random points within the collider's bounding box and keeps the first
    // one that actually lands inside the (possibly non-rectangular) shape,
    // giving up after a few tries and using the last sample anyway so a
    // weirdly-shaped deck can't stall this out.
    private void PickNewWanderTarget()
    {
        if (deckBounds == null)
        {
            wanderTarget = homePosition;
            return;
        }

        Bounds bounds = deckBounds.bounds;
        Vector2 candidate = homePosition;

        for (int attempt = 0; attempt < maxWanderSampleAttempts; attempt++)
        {
            candidate = new Vector2(
                Random.Range(bounds.min.x, bounds.max.x),
                Random.Range(bounds.min.y, bounds.max.y)
            );

            if (IsInsideDeckBounds(candidate))
            {
                break;
            }
        }

        wanderTarget = candidate;
    }

    // Editor visualization

    private void OnDrawGizmosSelected()
    {
        Vector2 center = Application.isPlaying
            ? homePosition
            : (homeCenter != null
                ? (Vector2)homeCenter.position
                : (Vector2)transform.position);

        // Deck bounds this pirate is confined to wander/return within.
        if (deckBounds != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(deckBounds.bounds.center, deckBounds.bounds.size);
        }

        // Large line-of-sight / aggro box (red) -- detection only.
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(center, aggroAreaSize);
    }
}
