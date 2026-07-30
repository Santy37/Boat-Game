using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace DeadmansTales.Ship
{
    /// <summary>
    /// Server-authoritative ship steering. <see cref="ShipHelm"/> (a plain,
    /// non-networked MonoBehaviour) still owns all of the LOCAL
    /// interaction/UI: who is in range, camera zoom, and reading the
    /// operator's own input every frame. For a networked player it hands
    /// that input to this component instead of writing the ship's Transform
    /// itself -- only the server may move a replicated NetworkObject's
    /// Transform and expect NetworkTransform (also on this GameObject) to
    /// broadcast it. Previously every peer just moved its own local copy of
    /// the ship and never actually told anyone else about it, which is why
    /// the ship never moved for anyone except whoever was steering.
    ///
    /// This also takes over two things ShipHelm used to do unconditionally
    /// in LateUpdate on every single peer: pushing the ship back out of an
    /// overlapping enemy hull, and keeping the seated operator glued to the
    /// wheel as it drifts. Both mutate authoritative state (the ship's
    /// position, a networked player's position), so both now run
    /// server-only, same as the steering itself.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkShipHelmSync : NetworkBehaviour
    {
        [Header("Wiring")]
        [Tooltip(
            "The same stand point ShipHelm seats the operator at. Read " +
            "directly from the server's own local scene copy -- it never " +
            "needs to cross the network, since every peer (including the " +
            "server) already has this same in-scene Transform, and the " +
            "server is the only one that acts on it."
        )]
        [SerializeField]
        private Transform standPoint;

        [Header("Movement")]
        [Tooltip("Units per second the ship moves while steering.")]
        [SerializeField]
        private float moveSpeed = 3f;

        [Tooltip("How far (x, y) the ship may drift from its start before it stops.")]
        [SerializeField]
        private Vector2 moveBounds = new Vector2(5f, 3f);

        [Header("Hull Separation")]
        [SerializeField]
        private bool pushOutOfEnemyHulls = true;

        [SerializeField]
        private float hullSeparationSkin = 0f;

        [Header("Safety")]
        [Tooltip(
            "If the manning client stops sending steering input for this " +
            "long -- disconnect, freeze, or a scene change without a clean " +
            "'leave the wheel' -- the server stops treating them as the " +
            "operator, so a stale client can never leave another player " +
            "glued to a wheel nobody is steering."
        )]
        [SerializeField]
        [Min(0f)]
        private float inputStaleSeconds = 0.5f;

        private Vector3 shipHome;
        private Vector2 steerOffset;
        private ulong? operatorClientId;
        private float inputExpiryTime;
        private Collider2D steeredHull;

        private void Awake()
        {
            shipHome = transform.localPosition;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsServer)
            {
                ResolveSteeredHullServer();
            }
        }

        /// <summary>
        /// Called every frame a networked player mans the helm (mirrors the
        /// old ShipHelm.LateUpdate write, just routed through the server
        /// instead of straight onto the Transform). Any connected client may
        /// call this -- there is nothing to own ahead of time, since who is
        /// currently steering is exactly what this call itself establishes.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void SubmitSteerInputServerRpc(
            Vector2 rawInput,
            ServerRpcParams rpcParams = default
        )
        {
            operatorClientId = rpcParams.Receive.SenderClientId;
            inputExpiryTime = Time.unscaledTime + inputStaleSeconds;

            steerOffset += rawInput * (moveSpeed * Time.deltaTime);
            steerOffset.x = Mathf.Clamp(steerOffset.x, -moveBounds.x, moveBounds.x);
            steerOffset.y = Mathf.Clamp(steerOffset.y, -moveBounds.y, moveBounds.y);

            ApplyPositionServer();
        }

        /// <summary>
        /// Called once when a networked player steps away from the helm.
        /// Only clears the operator if the caller is the one currently
        /// steering -- an out-of-order or stale packet from a previous
        /// operator must never boot whoever is steering now.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void StopSteerServerRpc(ServerRpcParams rpcParams = default)
        {
            if (operatorClientId == rpcParams.Receive.SenderClientId)
            {
                operatorClientId = null;
            }
        }

        private void Update()
        {
            if (!IsServer)
            {
                return;
            }

            if (
                operatorClientId.HasValue &&
                Time.unscaledTime >= inputExpiryTime
            )
            {
                operatorClientId = null;
            }
        }

        private void LateUpdate()
        {
            if (!IsServer)
            {
                return;
            }

            // Before the operator is re-pinned below -- otherwise the
            // steersman gets glued to a wheel that is about to be shoved
            // somewhere else this same frame. Mirrors the ordering the old
            // ShipHelm.LateUpdate used.
            PushOutOfEnemyHullsServer();
            PinOperatorServer();
        }

        private void ApplyPositionServer()
        {
            transform.localPosition = shipHome + (Vector3)steerOffset;
        }

        private void PinOperatorServer()
        {
            if (
                standPoint == null ||
                !operatorClientId.HasValue ||
                NetworkManager == null ||
                !NetworkManager.ConnectedClients.TryGetValue(
                    operatorClientId.Value,
                    out NetworkClient client
                ) ||
                client.PlayerObject == null
            )
            {
                return;
            }

            TopDownNetworkPlayer2D player =
                client.PlayerObject.GetComponent<TopDownNetworkPlayer2D>();

            if (player != null)
            {
                player.PinToStationServer(standPoint.position);
            }
        }

        // Only the PLAYER's ship gets pushed out of things -- resolved
        // through PlayerShipMarker, which lives on this same GameObject (see
        // Boat_Gameplay_2D's Ship root). An EnemyShip's own helm/steering
        // carries no PlayerShipMarker, so separation stays off for it.
        private void ResolveSteeredHullServer()
        {
            PlayerShipMarker marker = GetComponent<PlayerShipMarker>();

            if (marker == null)
            {
                return;
            }

            steeredHull = marker.Hitbox;

            if (steeredHull == null)
            {
                Debug.LogWarning(
                    $"[Network Ship Helm Sync] '{name}' steers the " +
                    "player's ship but its PlayerShipMarker has no Hitbox " +
                    "wired, so it cannot be kept out of enemy hulls.",
                    this
                );
            }
        }

        /// <summary>
        /// Post-move overlap resolution: shoves the player's ship straight
        /// back out of any enemy hull it is currently inside. Ported
        /// unchanged from the old ShipHelm.PushOutOfEnemyHulls, just run
        /// server-only against the server's own authoritative steerOffset.
        /// </summary>
        private void PushOutOfEnemyHullsServer()
        {
            if (!pushOutOfEnemyHulls || steeredHull == null)
            {
                return;
            }

            if (!steeredHull.enabled || !steeredHull.gameObject.activeInHierarchy)
            {
                return;
            }

            IReadOnlyList<EnemyShipHullContact> hulls =
                EnemyShipHullContact.Active;

            if (hulls.Count == 0)
            {
                return;
            }

            Physics2D.SyncTransforms();

            Vector2 correction = Vector2.zero;

            foreach (EnemyShipHullContact hull in hulls)
            {
                if (hull == null || hull.Hull == null)
                {
                    continue;
                }

                Collider2D enemyHull = hull.Hull;

                if (
                    enemyHull == steeredHull ||
                    !enemyHull.enabled ||
                    !enemyHull.gameObject.activeInHierarchy
                )
                {
                    continue;
                }

                ColliderDistance2D separation =
                    Physics2D.Distance(enemyHull, steeredHull);

                if (!separation.isValid || !separation.isOverlapped)
                {
                    continue;
                }

                Vector2 push = separation.pointA - separation.pointB;

                if (hullSeparationSkin > 0f && push != Vector2.zero)
                {
                    push += push.normalized * hullSeparationSkin;
                }

                if (push.sqrMagnitude > correction.sqrMagnitude)
                {
                    correction = push;
                }
            }

            if (correction == Vector2.zero)
            {
                return;
            }

            if (transform.parent != null)
            {
                correction = transform.parent.InverseTransformVector(correction);
            }

            steerOffset += correction;
            steerOffset.x = Mathf.Clamp(steerOffset.x, -moveBounds.x, moveBounds.x);
            steerOffset.y = Mathf.Clamp(steerOffset.y, -moveBounds.y, moveBounds.y);

            ApplyPositionServer();
        }
    }
}
