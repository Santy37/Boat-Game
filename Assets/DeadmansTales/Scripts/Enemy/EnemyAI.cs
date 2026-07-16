using UnityEngine;

/* EnemyAI
Wanders freely inside a small invisible "home" box until a living player
enters a larger line-of-sight box. It then chases that player
until the player leaves the LOS box or dies, attacking with a sword the same
way the player does. When the target is lost it walks back to its home box
and resumes wandering */
[RequireComponent(typeof(Rigidbody2D))]
public class EnemyAI : MonoBehaviour
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

    [Header("Home / Wander Box (small, invisible)")]
    [Tooltip("Center of the wander + return box. If empty, the enemy's start position is used.")]
    [SerializeField] private Transform homeCenter;
    [SerializeField] private Vector2 wanderAreaSize = new Vector2(4f, 4f);
    [SerializeField] private float wanderSpeed = 1.5f;
    [SerializeField] private float wanderPointTolerance = 0.15f;
    [Tooltip("Min/max seconds the enemy pauses after reaching a wander point.")]
    [SerializeField] private Vector2 wanderPauseRange = new Vector2(0.5f, 2f);

    [Header("Line-of-Sight / Aggro Box (big, ~triple size)")]
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
    [Tooltip("If the enemy can't make progress for this long (e.g. blocked by a water/obstacle collider), it gives up on the current point and picks a new one so it doesn't loiter against a wall.")]
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

    private void Start()
    {
        homePosition = homeCenter != null
            ? (Vector2)homeCenter.position
            : rb.position;

        lastPosition = rb.position;

        PickNewWanderTarget();
        RefreshPlayerCache();
    }

    private void FixedUpdate()
    {
        if (enemy != null && !enemy.IsAlive)
        {
            state = State.Dead;
        }

        if (state == State.Dead)
        {
            return;
        }

        // How far the body actually moved since the last physics step. If a
        // collider (water/obstacle) is blocking us, this stays near zero even
        // though we keep calling MovePosition — that's how we detect "stuck".
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
            // Blocked by something (usually the water/obstacle collider at the
            // edge of the box). Give up on this point and try another one.
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

        if (IsInsideWanderBox(rb.position))
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

    private bool IsInsideWanderBox(Vector2 pos)
    {
        Vector2 half = wanderAreaSize * 0.5f;
        return Mathf.Abs(pos.x - homePosition.x) <= half.x &&
               Mathf.Abs(pos.y - homePosition.y) <= half.y;
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

    private void PickNewWanderTarget()
    {
        Vector2 half = wanderAreaSize * 0.5f;
        wanderTarget = homePosition + new Vector2(
            Random.Range(-half.x, half.x),
            Random.Range(-half.y, half.y)
        );
    }

    // Editor visualization of the invisible boxes 

    private void OnDrawGizmosSelected()
    {
        Vector2 center = Application.isPlaying
            ? homePosition
            : (homeCenter != null
                ? (Vector2)homeCenter.position
                : (Vector2)transform.position);

        // Small wander box (green).
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(center, wanderAreaSize);

        // Large line-of-sight / aggro box (red).
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(center, aggroAreaSize);
    }
}
