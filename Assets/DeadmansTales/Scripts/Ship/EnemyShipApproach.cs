using Unity.Netcode;
using UnityEngine;

namespace DeadmansTales.Ship
{
    public enum EnemyShipEngagementState
    {
        Approaching,
        Engaged,
        Sunk
    }

    /// <summary>
    /// Closes distance on the player's ship while this enemy ship hasn't
    /// been dealt with -- its crew is still alive and it hasn't sunk. Stops
    /// once close enough to engage (cannon/boarding range) and permanently
    /// stops once sunk or its crew is wiped out.
    ///
    /// Server-only. Writes transform.position directly, the same way the
    /// player's ShipHelm does for the player's own ship; relies on this
    /// object's NetworkTransform to replicate the resulting position to
    /// every client, the same pattern TopDownNetworkPlayer2D uses for
    /// player movement.
    ///
    /// Crew (Enemy/ShipEnemyAI instances) each have their own NetworkObject
    /// (Enemy.cs requires one), so they cannot be real Transform children of
    /// this ship's NetworkObject -- NGO does not support nested spawned
    /// NetworkObjects. They must instead sit alongside this ship as sibling
    /// scene objects, assigned into the Crew list below, and get carried
    /// along by directly nudging their Rigidbody2D position by this ship's
    /// own per-frame movement delta (see LateUpdate), rather than through
    /// real parenting.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class EnemyShipApproach : NetworkBehaviour
    {
        [Header("Movement")]
        [SerializeField]
        [Min(0f)]
        private float approachSpeed = 2f;

        [Tooltip(
            "How close the ship gets, measured to the player ship. It stops " +
            "when it reaches this distance OR (if Hull Contact is wired) when " +
            "the hulls actually touch -- whichever happens first. Acts as a " +
            "stand-off offset: raise it to keep ships farther away, lower it " +
            "toward 0 to let them close right up to the hull."
        )]
        [SerializeField]
        [Min(0f)]
        private float engagementRange = 12f;

        [Tooltip(
            "The ship's hull trigger (e.g. ShipHitBox) with an " +
            "EnemyShipHullContact component on it. When assigned, the " +
            "ship engages the instant its hull actually touches the " +
            "player's ship instead of at a fixed distance. Leave empty to " +
            "fall back to Engagement Range."
        )]
        [SerializeField]
        private EnemyShipHullContact hullContact;

        [Header("Crew")]
        [Tooltip(
            "Assign each pirate standing on this ship's deck manually. " +
            "These must NOT be Transform children of this ship (see the " +
            "class remarks) -- place them as sibling scene objects "  +
            "positioned on the deck instead."
        )]
        [SerializeField]
        private Enemy[] crew;

        [Tooltip(
            "This ship's own deck-bounds collider (e.g. the deck-shaped " +
            "EdgeCollider2D/PolygonCollider2D), handed to spawned crew's " +
            "ShipEnemyAI.DeckBounds by EnemyShipSpawner. Wire this once on " +
            "the prefab itself, pointing to this same ship's own collider " +
            "-- it's an intra-prefab reference so every spawned instance " +
            "resolves it to its own copy automatically."
        )]
        [SerializeField]
        private Collider2D deckBoundsForCrew;

        public Collider2D DeckBoundsForCrew => deckBoundsForCrew;

        [SerializeField]
        [Tooltip("Leave empty to find this ship's own NetworkShipHealth.")]
        private NetworkShipHealth shipHealth;

        public EnemyShipEngagementState State { get; private set; } =
            EnemyShipEngagementState.Approaching;

        private Transform targetShip;
        private Vector2 lastTickPosition;
        private Vector2 frameMoveDelta;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            Debug.Log(
                $"[Enemy Ship Approach] {name} OnNetworkSpawn -- " +
                $"IsServer={IsServer}, IsSpawned={IsSpawned}",
                this
            );

            if (!IsServer)
            {
                return;
            }

            if (crew == null || crew.Length == 0)
            {
                crew = GetComponentsInChildren<Enemy>(true);
            }

            if (shipHealth == null)
            {
                shipHealth = GetComponent<NetworkShipHealth>();
            }

            if (hullContact == null)
            {
                hullContact = GetComponentInChildren<EnemyShipHullContact>(true);
            }

            if (shipHealth != null)
            {
                shipHealth.ShipSunk += HandleShipSunk;
            }

            lastTickPosition = transform.position;

            Debug.Log(
                $"[Enemy Ship Approach] {name} crew count={(crew == null ? 0 : crew.Length)}, " +
                $"shipHealth resolved={(shipHealth != null)}",
                this
            );
        }

        public override void OnNetworkDespawn()
        {
            if (shipHealth != null)
            {
                shipHealth.ShipSunk -= HandleShipSunk;
            }

            base.OnNetworkDespawn();
        }

        private float nextDiagnosticLogTime;

        private void Update()
        {
            frameMoveDelta = Vector2.zero;

            if (
                !IsServer ||
                !IsSpawned ||
                State == EnemyShipEngagementState.Sunk
            )
            {
                return;
            }

            if (IsDealtWith())
            {
                State = EnemyShipEngagementState.Engaged;
                return;
            }

            Transform target = ResolveTargetShip();

            if (target == null)
            {
                if (Time.time >= nextDiagnosticLogTime)
                {
                    nextDiagnosticLogTime = Time.time + 2f;
                    Debug.LogWarning(
                        $"[Enemy Ship Approach] {name} has no target -- " +
                        "FindFirstObjectByType<PlayerShipMarker>() found " +
                        "nothing.",
                        this
                    );
                }

                return;
            }

            float distance = Vector2.Distance(
                transform.position,
                target.position
            );

            if (Time.time >= nextDiagnosticLogTime)
            {
                nextDiagnosticLogTime = Time.time + 2f;
                Debug.Log(
                    $"[Enemy Ship Approach] {name} distance to player " +
                    $"ship={distance:0.0}, engagementRange={engagementRange:0.0}, " +
                    $"hullTouching={(hullContact != null ? hullContact.IsTouchingPlayerShip.ToString() : "n/a")}, " +
                    $"state={State}.",
                    this
                );
            }

            // Stop when the hull actually touches OR the ship has closed to
            // within Engagement Range -- whichever happens first. This makes
            // Engagement Range a stand-off offset that always applies: 0 lets
            // the ship come right up to the hull, larger values keep it farther.
            bool hullTouching =
                hullContact != null && hullContact.IsTouchingPlayerShip;
            bool withinRange = distance <= engagementRange;
            bool shouldEngage = hullTouching || withinRange;

            if (shouldEngage)
            {
                State = EnemyShipEngagementState.Engaged;
                return;
            }

            State = EnemyShipEngagementState.Approaching;

            Vector2 beforePos = transform.position;

            Vector2 direction = (
                (Vector2)target.position - (Vector2)transform.position
            ).normalized;

            transform.position +=
                (Vector3)(direction * approachSpeed * Time.deltaTime);

            Vector2 afterPos = transform.position;

            if (Time.time >= nextMoveDiagnosticLogTime)
            {
                nextMoveDiagnosticLogTime = Time.time + 2f;
                Debug.Log(
                    $"[Enemy Ship Approach] {name} MOVE -- before={beforePos}, " +
                    $"after={afterPos}, targetPos={(Vector2)target.position}, " +
                    $"direction={direction}, approachSpeed={approachSpeed:0.0}, " +
                    $"deltaTime={Time.deltaTime:0.000}.",
                    this
                );
            }

            frameMoveDelta = (Vector2)transform.position - lastTickPosition;
            lastTickPosition = transform.position;
        }

        private float nextMoveDiagnosticLogTime;

        // Runs after Update (and after crew's own FixedUpdate-driven
        // MovePosition calls have already been applied for this physics
        // step), so nudging crew here doesn't get overwritten by their own
        // wander/chase movement -- it just carries them along on top of it.
        // Crew can't be real Transform children (see class remarks), so this
        // is the stand-in for "ride along with the ship."
        private void LateUpdate()
        {
            if (
                !IsServer ||
                !IsSpawned ||
                frameMoveDelta == Vector2.zero ||
                crew == null
            )
            {
                return;
            }

            foreach (Enemy pirate in crew)
            {
                if (pirate == null || !pirate.IsAlive)
                {
                    continue;
                }

                Rigidbody2D pirateBody = pirate.GetComponent<Rigidbody2D>();

                if (pirateBody != null)
                {
                    pirateBody.position += frameMoveDelta;
                }
                else
                {
                    pirate.transform.position += (Vector3)frameMoveDelta;
                }

                ShipEnemyAI pirateAi = pirate.GetComponent<ShipEnemyAI>();
                pirateAi?.ApplyExternalDelta(frameMoveDelta);
            }
        }

        /// <summary>
        /// Server-only: called by EnemyShipSpawner right after it spawns
        /// this ship's crew at runtime, since they can't be wired in the
        /// Inspector ahead of time the way manually-placed crew are.
        /// </summary>
        public void SetCrewServer(Enemy[] spawnedCrew)
        {
            crew = spawnedCrew;
        }

        /// <summary>
        /// Server-only: overrides the prefab's Approach Speed, so the spawner
        /// that created this ship can control how fast it closes in.
        /// </summary>
        public void SetApproachSpeedServer(float speed)
        {
            if (!IsServer)
            {
                return;
            }

            if (speed >= 0f)
            {
                approachSpeed = speed;
            }
        }

        /// <summary>
        /// Server-only: despawns whatever crew is still alive when this ship
        /// is defeated. They're sibling scene objects, not real Transform
        /// children, so despawning/destroying this ship's own NetworkObject
        /// does not take them with it automatically -- this has to be done
        /// explicitly. Called by EnemyShipCleanup right before the ship
        /// itself despawns.
        /// </summary>
        public void DespawnRemainingCrewServer()
        {
            Debug.Log(
                $"[Enemy Ship Approach] {name} DespawnRemainingCrewServer " +
                $"called -- IsServer={IsServer}, crew count=" +
                $"{(crew == null ? 0 : crew.Length)}.",
                this
            );

            if (!IsServer || crew == null)
            {
                return;
            }

            foreach (Enemy pirate in crew)
            {
                if (
                    pirate == null ||
                    pirate.NetworkObject == null ||
                    !pirate.NetworkObject.IsSpawned
                )
                {
                    Debug.Log(
                        $"[Enemy Ship Approach] {name} skipping a crew " +
                        $"entry -- pirate null={pirate == null}, " +
                        $"NetworkObject null=" +
                        $"{(pirate != null && pirate.NetworkObject == null)}, " +
                        $"IsSpawned=" +
                        $"{(pirate != null && pirate.NetworkObject != null && pirate.NetworkObject.IsSpawned)}.",
                        this
                    );
                    continue;
                }

                Debug.Log(
                    $"[Enemy Ship Approach] Despawning crew member " +
                    $"{pirate.name}.",
                    this
                );

                bool destroyRuntimeObject =
                    pirate.NetworkObject.IsSceneObject != true;

                pirate.NetworkObject.Despawn(destroyRuntimeObject);
            }
        }

        /// <summary>
        /// True while at least one pirate assigned to this ship is still
        /// alive. An empty/unassigned crew list counts as "alive" -- a ship
        /// with no crew configured yet (e.g. mid-setup) shouldn't silently
        /// read as already defeated. Public so EnemyShipCannon can require a
        /// living crew before firing -- an unmanned cannon shouldn't keep
        /// shooting on its own.
        /// </summary>
        public bool HasLivingCrew
        {
            get
            {
                if (crew == null || crew.Length == 0)
                {
                    return true;
                }

                foreach (Enemy pirate in crew)
                {
                    if (pirate != null && pirate.IsAlive)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// "Dealt with" means either the ship itself has sunk, or every
        /// pirate aboard it is dead -- either should stop it from
        /// continuing to close, per the design plan.
        /// </summary>
        private bool IsDealtWith()
        {
            if (shipHealth != null && shipHealth.IsSunk)
            {
                return true;
            }

            return !HasLivingCrew;
        }

        private void HandleShipSunk()
        {
            State = EnemyShipEngagementState.Sunk;
        }

        private Transform ResolveTargetShip()
        {
            if (targetShip != null)
            {
                return targetShip;
            }

            PlayerShipMarker playerShip =
                FindFirstObjectByType<PlayerShipMarker>();

            if (playerShip != null)
            {
                targetShip = playerShip.transform;
            }

            return targetShip;
        }
    }
}
