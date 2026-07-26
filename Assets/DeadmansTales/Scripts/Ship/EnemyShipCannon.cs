using Unity.Netcode;
using UnityEngine;

namespace DeadmansTales.Ship
{
    /// <summary>
    /// A cannon mounted on an enemy ship. Unlike the player-manned
    /// ShipCannon, nobody sits at this one -- the server periodically
    /// checks whether the player's ship is within range and its forward
    /// arc, and fires at it automatically. Reuses NetworkCannonball for the
    /// actual projectile, so hits deal damage and resolve exactly like a
    /// player-fired shot (SinkLevel only, same directness scoring).
    ///
    /// Deliberately has NO RequireComponent(NetworkObject) -- this lives on
    /// a child GameObject under the ship's single root NetworkObject, and
    /// NGO does not support nested NetworkObjects. It's discovered and
    /// driven the same as any other NetworkBehaviour under that one root.
    /// </summary>
    public sealed class EnemyShipCannon : NetworkBehaviour
    {
        [Header("Mount")]
        [Tooltip("Where the ball spawns, and where the firing arc is measured from.")]
        [SerializeField]
        private Transform muzzle;

        [Tooltip("Direction this cannon points when idle -- its forward arc center.")]
        [SerializeField]
        private Vector2 facing = Vector2.up;

        [Header("Targeting")]
        [SerializeField]
        private NetworkCannonball cannonballPrefab;

        [SerializeField]
        [Min(0f)]
        private float firingRange = 14f;

        [Tooltip(
            "Degrees to either side of facing the player's ship must fall " +
            "within before this cannon will fire on it."
        )]
        [SerializeField]
        [Range(0f, 180f)]
        private float firingArcDegrees = 90f;

        [Tooltip("Random aim spread applied to each shot, in degrees.")]
        [SerializeField]
        [Min(0f)]
        private float aimSpreadDegrees = 6f;

        [Header("Firing")]
        [SerializeField]
        [Min(0.1f)]
        private float cooldown = 2.5f;

        [SerializeField]
        [Min(0.1f)]
        private float ballSpeed = 12f;

        [SerializeField]
        [Tooltip("Leave empty to find this ship's own EnemyShipApproach.")]
        private EnemyShipApproach shipApproach;

        private PlayerShipMarker targetShip;
        private float nextFireTime;

        private float nextDiagnosticLogTime;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            Debug.Log(
                $"[Enemy Ship Cannon] {name} OnNetworkSpawn -- " +
                $"IsServer={IsServer}, IsSpawned={IsSpawned}",
                this
            );

            // Wait out a full cooldown before the first shot so a freshly
            // spawned ship doesn't open fire the instant it exists.
            nextFireTime = Time.time + cooldown;
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned || Time.time < nextFireTime)
            {
                return;
            }

            // An unmanned cannon doesn't fire itself -- once every pirate
            // aboard this ship is dead, its cannons fall silent even if the
            // hull is still afloat.
            EnemyShipApproach approach = ResolveShipApproach();

            if (approach != null && !approach.HasLivingCrew)
            {
                if (Time.time >= nextDiagnosticLogTime)
                {
                    nextDiagnosticLogTime = Time.time + 2f;
                    Debug.LogWarning(
                        $"[Enemy Ship Cannon] {name} not firing -- " +
                        "ResolveShipApproach() reports no living crew.",
                        this
                    );
                }

                return;
            }

            PlayerShipMarker playerShip = ResolvePlayerShip();

            if (playerShip == null)
            {
                if (Time.time >= nextDiagnosticLogTime)
                {
                    nextDiagnosticLogTime = Time.time + 2f;
                    Debug.LogWarning(
                        $"[Enemy Ship Cannon] {name} not firing -- " +
                        "FindFirstObjectByType<PlayerShipMarker>() found " +
                        "nothing.",
                        this
                    );
                }

                return;
            }

            // Aim at the player's actual hull collider, not their ship's
            // raw Transform -- the root pivot can sit well away from where
            // the hull collider really is (same issue found on EnemyShip's
            // own deck collider), so aiming at transform.position alone can
            // miss the real hitbox and never register a hit.
            TryFireAt(playerShip.AimPoint);
        }

        private void TryFireAt(Vector2 targetPosition)
        {
            Vector2 from = muzzle != null
                ? (Vector2)muzzle.position
                : (Vector2)transform.position;

            Vector2 toTarget = targetPosition - from;
            float distance = toTarget.magnitude;

            if (distance > firingRange || distance < 0.01f)
            {
                if (Time.time >= nextDiagnosticLogTime)
                {
                    nextDiagnosticLogTime = Time.time + 2f;
                    Debug.LogWarning(
                        $"[Enemy Ship Cannon] {name} not firing -- target " +
                        $"distance {distance:0.0} is outside firingRange " +
                        $"{firingRange:0.0}.",
                        this
                    );
                }

                return;
            }

            // "facing" is authored in the cannon's own LOCAL space (its
            // default, Vector2.up, means "whatever this cannon's local up
            // is" -- e.g. a broadside cannon might use Vector2.right). It
            // must be rotated into world space by this transform's current
            // rotation before comparing against toTarget, which is already
            // world space. This cannon's transform is a child of the ship's
            // root and inherits the ship's rotation, so
            // transform.TransformDirection does exactly that.
            //
            // Previously this compared the raw local vector directly
            // against a world-space direction, which only ever happened to
            // work when the ship's rotation was near identity -- for any
            // ship approaching/engaging at an angle (the normal case), the
            // arc check was comparing against the wrong frame entirely, so
            // whichever cannons happened to line up by coincidence at spawn
            // were the only ones that would ever ever fire, permanently.
            Vector2 localFacing = facing.sqrMagnitude > 0.0001f
                ? facing.normalized
                : Vector2.up;

            Vector2 facingDirection = transform.TransformDirection(localFacing);

            float angleFromFacing = Vector2.Angle(facingDirection, toTarget);

            if (angleFromFacing > firingArcDegrees)
            {
                if (Time.time >= nextDiagnosticLogTime)
                {
                    nextDiagnosticLogTime = Time.time + 2f;
                    Debug.LogWarning(
                        $"[Enemy Ship Cannon] {name} not firing -- target " +
                        $"angle {angleFromFacing:0.0} deg from facing " +
                        $"{facingDirection} exceeds firingArcDegrees " +
                        $"{firingArcDegrees:0.0}.",
                        this
                    );
                }

                return;
            }

            nextFireTime = Time.time + cooldown;

            Debug.Log(
                $"[Enemy Ship Cannon] {name} FIRING at distance " +
                $"{distance:0.0}, angle {angleFromFacing:0.0} deg.",
                this
            );

            float spread = Random.Range(-aimSpreadDegrees, aimSpreadDegrees);
            Vector2 direction =
                (Quaternion.Euler(0f, 0f, spread) * toTarget).normalized;

            Fire(from, direction * ballSpeed);
        }

        private void Fire(Vector2 origin, Vector2 velocity)
        {
            if (cannonballPrefab == null)
            {
                Debug.LogWarning(
                    "[Enemy Ship Cannon] No cannonball prefab assigned.",
                    this
                );
                return;
            }

            NetworkCannonball ball = Instantiate(
                cannonballPrefab,
                origin,
                Quaternion.identity
            );

            ball.NetworkObject.Spawn(true);
            ball.SetIgnoreShipServer(GetComponentInParent<NetworkShipSinkMeter>());
            ball.LaunchServer(velocity);
        }

        private PlayerShipMarker ResolvePlayerShip()
        {
            if (targetShip != null)
            {
                return targetShip;
            }

            targetShip = FindFirstObjectByType<PlayerShipMarker>();

            return targetShip;
        }

        private EnemyShipApproach ResolveShipApproach()
        {
            if (shipApproach == null)
            {
                shipApproach = GetComponentInParent<EnemyShipApproach>();
            }

            return shipApproach;
        }
    }
}
