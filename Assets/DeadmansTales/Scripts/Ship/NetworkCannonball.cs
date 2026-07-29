using Unity.Netcode;
using UnityEngine;

namespace DeadmansTales.Ship
{
    /// <summary>
    /// Server-authoritative replacement for the old client-local Cannonball.
    /// The server moves it, resolves its hit, applies damage, and despawns
    /// it; every client just sees the replicated result through this
    /// object's NetworkTransform. Spawned by
    /// <see cref="TopDownNetworkPlayer2D"/>'s FireCannonServerRpc so every
    /// peer sees the same shot instead of each client instantiating (and
    /// only itself ever seeing) its own local ball.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkCannonball : NetworkBehaviour
    {
        [SerializeField]
        [Min(0.1f)]
        private float lifeSeconds = 3f;

        [SerializeField]
        [Min(0f)]
        private float damage = 25f;

        [SerializeField]
        [Min(1)]
        [Tooltip(
            "Damage dealt to a KrakenHealth boss per hit. Kept separate " +
            "from 'damage' above -- that value is tuned against a ship's " +
            "SinkLevel (default max 150) and would one/two-shot the " +
            "kraken's much smaller health pool (default max 20) if reused " +
            "directly. Mirrors the old local Cannonball's per-hit damage " +
            "of 1, so the fight paces the same either way."
        )]
        private int krakenDamage = 1;

        [SerializeField]
        [Min(0.1f)]
        [Tooltip(
            "Server side safety clamp on requested launch speed. Prevents " +
            "a modified client from requesting an unreasonably fast shot."
        )]
        private float maximumLaunchSpeed = 40f;

        [SerializeField]
        [Min(0f)]
        [Tooltip(
            "Trigger hits are ignored for this long right after launch. " +
            "The ball spawns at the cannon's muzzle, which usually still " +
            "overlaps the cannon's own barrel collider or the ship's hull " +
            "for a moment -- without this, it can register that as a hit " +
            "and despawn itself before it has moved at all."
        )]
        private float spawnGracePeriod = 0.08f;

        private Vector2 velocity;
        private float despawnTime;
        private float armedTime;
        private bool launched;
        private NetworkShipSinkMeter ignoreShip;

        /// <summary>
        /// Server only: tells this ball which ship fired it, so its own
        /// hull/rail/deck colliders never count as a hit -- no matter how
        /// close the ships are (e.g. Engaged, hulls touching) or how little
        /// travel distance the ball has had. This replaces relying on
        /// spawnGracePeriod alone: a timing window can't tell "the firing
        /// ship's hull, which happens to still be overlapping" apart from
        /// "the enemy's hull, which is also already overlapping because the
        /// ships are touching" -- an explicit reference can.
        /// </summary>
        public void SetIgnoreShipServer(NetworkShipSinkMeter shipToIgnore)
        {
            ignoreShip = shipToIgnore;
        }

        /// <summary>
        /// Server only: arms this ball with a velocity and lifetime. Called
        /// immediately after the server instantiates the prefab.
        /// </summary>
        public void LaunchServer(Vector2 requestedVelocity)
        {
            if (!IsServer)
            {
                Debug.LogWarning(
                    "[Cannonball] LaunchServer called on a non server " +
                    "instance; ignored.",
                    this
                );
                return;
            }

            velocity = Vector2.ClampMagnitude(
                requestedVelocity,
                maximumLaunchSpeed
            );

            despawnTime = Time.time + lifeSeconds;
            armedTime = Time.time + spawnGracePeriod;
            launched = true;
        }

        private void Update()
        {
            if (!IsServer || !launched)
            {
                return;
            }

            transform.position += (Vector3)(velocity * Time.deltaTime);

            if (Time.time >= despawnTime)
            {
                DespawnSelf();
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            HandlePossibleHit(other);
        }

        // A ball fired while the enemy ship is Engaged (hulls already
        // touching, see EnemyShipHullContact) spawns already overlapping the
        // enemy's hull trigger -- there's no travel gap to create a fresh
        // OnTriggerEnter2D once armed. That entry event fires once, gets
        // ignored during spawnGracePeriod, and since the overlap never ends
        // (ships stay touching), OnTriggerEnter2D never fires again -- the
        // ball just flies through untouched until its lifetime runs out.
        // OnTriggerStay2D catches that case: once armed, a continuing
        // overlap still lands the hit on the next physics tick.
        private void OnTriggerStay2D(Collider2D other)
        {
            HandlePossibleHit(other);
        }

        private void HandlePossibleHit(Collider2D other)
        {
            if (!IsServer || !launched || Time.time < armedTime)
            {
                return;
            }

            NetworkShipSinkMeter hitSinkMeter =
                other.GetComponentInParent<NetworkShipSinkMeter>();

            Debug.Log(
                $"[Cannonball] {name} HandlePossibleHit -- other=" +
                $"{other.name}, other.isTrigger={other.isTrigger}, " +
                $"hitSinkMeter={(hitSinkMeter != null ? hitSinkMeter.name : "null")}, " +
                $"ignoreShip={(ignoreShip != null ? ignoreShip.name : "null")}, " +
                $"isIgnored={(hitSinkMeter != null && hitSinkMeter == ignoreShip)}.",
                this
            );

            // The firing ship's own hull/rail/deck colliders never count as
            // a hit, no matter how close it is or how the grace period
            // timing lines up -- explicit ship identity, not overlap
            // timing, is what decides "is this myself." Return without
            // despawning so the ball keeps flying until it reaches
            // something else (the enemy, a rock, life expiring, etc.).
            if (ignoreShip != null && hitSinkMeter == ignoreShip)
            {
                return;
            }

            // Mirrors the original client local Cannonball: any (non-self)
            // trigger contact ends the shot. The only new behavior is
            // applying damage first, when the thing hit has health to take
            // it.
            ApplyDamageIfDamageable(other, hitSinkMeter);
            DespawnSelf();
        }

        private void ApplyDamageIfDamageable(
            Collider2D other,
            NetworkShipSinkMeter sinkMeter
        )
        {
            // A cannon hit on a ship only ever damages SinkLevel, never
            // Health directly -- Health drains on its own, continuously,
            // for as long as SinkLevel is below full (see
            // NetworkShipSinkMeter.TickHealthDrain). Health has no direct
            // damage entry point from combat by design.
            if (sinkMeter != null)
            {
                float directness = ComputeHitDirectness(other);

                Debug.Log(
                    $"[Cannonball] {name} applying {damage} dmg to " +
                    $"{sinkMeter.name} at directness {directness:0.00}. " +
                    $"SinkLevel before={sinkMeter.CurrentSinkLevel.Value:0.0}.",
                    this
                );

                sinkMeter.ApplyCannonHitServer(damage, directness);

                Debug.Log(
                    $"[Cannonball] {sinkMeter.name} SinkLevel after=" +
                    $"{sinkMeter.CurrentSinkLevel.Value:0.0}.",
                    this
                );

                return;
            }

            // The kraken boss isn't a ship and isn't an Enemy -- its own
            // server-authoritative KrakenHealth is what
            // KrakenArenaHud's boss bar reads via FindFirstObjectByType.
            // Uses krakenDamage, not damage: 'damage' is tuned for a
            // ship's much larger SinkLevel pool and would one/two-shot
            // the kraken's much smaller health pool if reused directly.
            KrakenHealth kraken = other.GetComponentInParent<KrakenHealth>();

            if (kraken != null)
            {
                if (!kraken.IsDead)
                {
                    kraken.TakeHitServer(krakenDamage);
                }

                return;
            }

            // Water obstacles: destructible rocks/hazards the progress bar
            // spawns. They carry their own per-hit damage, so we don't pass
            // this ball's damage -- just tell it it was hit.
            DestructibleObstacle obstacle =
                other.GetComponentInParent<DestructibleObstacle>();

            if (obstacle != null)
            {
                obstacle.ApplyCannonHitServer();
                return;
            }

            Enemy enemy = other.GetComponentInParent<Enemy>();

            if (enemy != null)
            {
                enemy.TakeDamage(damage);
                return;
            }

            Debug.Log(
                $"[Cannonball] {name} hit {other.name} but it has neither " +
                "a NetworkShipSinkMeter, KrakenHealth, nor an Enemy in its " +
                "parent chain -- shot consumed with no damage applied.",
                this
            );
        }

        /// <summary>
        /// 1.0 for a hit landing at the target collider's center (along the
        /// hull's length), tapering toward 0 near the bow/stern. Cheap
        /// stand-in for "how well aimed was this shot" without needing any
        /// new aiming mechanics.
        ///
        /// Deliberately measures only along the hull's LONG axis (bow to
        /// stern), not both axes -- see GetLocalHalfExtents for why.
        ///
        /// Works in the hit collider's own LOCAL space, not world space.
        /// An earlier version used hitCollider.bounds (a world-space,
        /// axis-aligned box) and picked whichever extent was bigger as "the
        /// long axis." That only works when the hull's rotation is near
        /// identity -- these ships approach and engage at all sorts of
        /// angles, and a rotated rectangle's world AABB mixes both the
        /// length and beam axes together, so "pick the bigger extent"
        /// stopped meaning "the bow-stern axis" and directness collapsed
        /// toward 0 for most Engaged hits again, same symptom as before.
        /// Using the collider's own local (unrotated) shape data and
        /// inverse-transforming the impact point into that same local space
        /// undoes the ship's rotation entirely, so this is correct at any
        /// facing.
        /// </summary>
        private float ComputeHitDirectness(Collider2D hitCollider)
        {
            Vector2 localImpact = hitCollider.transform.InverseTransformPoint(
                transform.position
            );

            GetLocalBounds(hitCollider, out Vector2 localCenter, out Vector2 localExtents);

            bool xIsLongAxis = localExtents.x >= localExtents.y;

            float longExtent = Mathf.Max(
                xIsLongAxis ? localExtents.x : localExtents.y,
                0.01f
            );

            // Offset from the shape's own local CENTER, not from the
            // collider's local origin (0,0). A collider's authored points
            // aren't necessarily centered on its own transform -- e.g. this
            // project's player ShipHitBox polygon spans roughly x:[-9.5,
            // 16.15], y:[6.5, 16.05], nowhere near local (0,0). Comparing
            // the impact point directly against local (0,0) as if it were
            // the center meant every hit measured as being enormously far
            // outside the hull, clamping directness to 0 on literally every
            // hit -- which is exactly why the player's own ship was
            // reported as never taking real cannon damage (SinkLevel stuck
            // at 150 the whole game) while the enemy ship, whose polygon
            // happened to be authored close to its own local origin,
            // worked fine by coincidence.
            float longOffset = xIsLongAxis
                ? localImpact.x - localCenter.x
                : localImpact.y - localCenter.y;

            return Mathf.Clamp01(1f - Mathf.Abs(longOffset) / longExtent);
        }

        /// <summary>
        /// Center and half-extents of the collider's shape in its own
        /// LOCAL space (i.e. as if the ship had identity rotation) -- unlike
        /// Collider2D.bounds, which is always a world-space axis-aligned
        /// box and gets fatter/mixes axes together as soon as the transform
        /// rotates. Ship hulls are long and thin (e.g. deck bounds extents
        /// observed as (6.13, 1.55) -- far wider than tall), so telling the
        /// long axis (bow-stern) apart from the short axis (beam) only
        /// works using shape data that isn't itself distorted by rotation.
        ///
        /// The center is NOT assumed to be local (0,0) -- a collider's
        /// authored points can sit anywhere relative to its own transform,
        /// so it's computed from the actual point data (or offset, for
        /// BoxCollider2D) every time.
        /// </summary>
        private static void GetLocalBounds(
            Collider2D hitCollider,
            out Vector2 center,
            out Vector2 extents
        )
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            bool foundPoints = false;

            if (hitCollider is PolygonCollider2D polygon)
            {
                for (int pathIndex = 0; pathIndex < polygon.pathCount; pathIndex++)
                {
                    foreach (Vector2 point in polygon.GetPath(pathIndex))
                    {
                        min = Vector2.Min(min, point);
                        max = Vector2.Max(max, point);
                        foundPoints = true;
                    }
                }
            }
            else if (hitCollider is EdgeCollider2D edge)
            {
                foreach (Vector2 point in edge.points)
                {
                    min = Vector2.Min(min, point);
                    max = Vector2.Max(max, point);
                    foundPoints = true;
                }
            }
            else if (hitCollider is BoxCollider2D box)
            {
                min = box.offset - box.size * 0.5f;
                max = box.offset + box.size * 0.5f;
                foundPoints = true;
            }

            if (!foundPoints)
            {
                // Fallback for collider types without exposed local point
                // data (e.g. CircleCollider2D/CapsuleCollider2D) -- not
                // rotation-safe, but these aren't used for ship hulls, so
                // this path shouldn't actually be hit in practice.
                Bounds worldBounds = hitCollider.bounds;
                center = hitCollider.transform.InverseTransformPoint(worldBounds.center);
                extents = worldBounds.extents;
                return;
            }

            center = (min + max) * 0.5f;
            extents = (max - min) * 0.5f;
        }

        private void DespawnSelf()
        {
            launched = false;

            if (IsSpawned)
            {
                NetworkObject.Despawn(true);
            }
        }
    }
}
