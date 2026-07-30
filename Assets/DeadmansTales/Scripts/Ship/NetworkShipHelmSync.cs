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
    ///
    /// THE WHEEL IS A CLAIMED SEAT. Every peer has its own copy of the
    /// non-networked ShipHelm, and "am I manning it" used to be purely local
    /// state on each of them. Nothing asked the server whether the wheel was
    /// already taken, so with three or four players two of them could each
    /// decide they were the steersman and both stream input here at once:
    /// the operator id flip-flopped between them every packet, their inputs
    /// summed into one offset (double speed when they agreed, a stutter when
    /// they fought), and PinOperatorServer yanked whichever of them had sent
    /// the most recent packet onto the wheel while leaving the other frozen
    /// in place but not pinned. <see cref="ClaimHelmServerRpc"/> is what
    /// makes the seat exclusive, and <see cref="SubmitSteerInputServerRpc"/>
    /// now drops input from anyone who does not hold it.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkShipHelmSync : NetworkBehaviour
    {
        /// <summary>
        /// Sentinel for "nobody is steering". A real client id is never this
        /// value, and a sentinel keeps the operator in a NetworkVariable
        /// (which cannot hold a nullable) so that every peer can see whether
        /// the wheel is free BEFORE trying to take it.
        /// </summary>
        public const ulong NoOperator = ulong.MaxValue;

        /// <summary>How long a client waits for a verdict on its claim.</summary>
        private const float ClaimReplyTimeoutSeconds = 3f;

        /// <summary>
        /// This machine's view of its own attempt to take the wheel.
        /// Deliberately NOT a NetworkVariable: it is local UI state about a
        /// request in flight, and only the requesting client ever reads it.
        /// </summary>
        public enum HelmClaim
        {
            None,
            Pending,
            Granted,
            Denied,
        }

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

        [Tooltip(
            "Extra margin, in world units, added around the deck bounds when " +
            "deciding who is 'aboard' and should be carried along with the " +
            "ship. A little slack matters: a player standing right against " +
            "the rail sits fractionally outside the deck collider, and " +
            "without margin they would be dropped from the carry list for a " +
            "frame and left behind by the moving deck."
        )]
        [SerializeField]
        [Min(0f)]
        private float aboardMargin = 0.75f;

        /// <summary>
        /// Who holds the wheel, replicated so that every peer's ShipHelm can
        /// see the seat is occupied without asking, and so a client can tell
        /// when the server has taken the wheel back off it (stale input,
        /// disconnect) and stand its own player up to match.
        /// </summary>
        private readonly NetworkVariable<ulong> operatorClientId =
            new NetworkVariable<ulong>(
                NoOperator,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server
            );

        private Vector3 shipHome;
        private Vector2 steerOffset;

        // The operator's most recent input, held until the next server frame
        // integrates it. NOT integrated on arrival -- see
        // IntegrateSteeringServer for why that was the whole ballgame.
        private Vector2 pendingSteerInput;

        private float inputExpiryTime;
        private Collider2D steeredHull;
        private PlayerShipMarker marker;

        private HelmClaim localClaim;
        private float localClaimTimeoutTime;

        // World position the ship sat at after the previous authoritative
        // move. The difference against the current one is how far the deck
        // travelled, which is exactly how far everyone standing on it has to
        // be carried -- see CarryPassengersServer.
        private Vector3 lastAppliedShipPosition;
        private bool hasLastAppliedShipPosition;

        /// <summary>True while somebody holds the wheel.</summary>
        public bool HasOperator => operatorClientId.Value != NoOperator;

        /// <summary>
        /// True when this machine may try to take the wheel: nobody has it,
        /// or this machine already does. Checked by ShipHelm before it seats
        /// a player, so the common case (walking up to a wheel another
        /// player is visibly steering) is refused without a round trip.
        /// </summary>
        public bool IsHelmFree
        {
            get
            {
                if (!IsSpawned)
                {
                    return false;
                }

                return !HasOperator ||
                    operatorClientId.Value == NetworkManager.LocalClientId;
            }
        }

        /// <summary>
        /// True once the server has confirmed this machine holds the wheel.
        /// Steering input is only worth sending in this state; while the
        /// claim is still Pending the server would reject it anyway.
        /// </summary>
        public bool IsLocalClaimGranted =>
            IsSpawned &&
            localClaim == HelmClaim.Granted &&
            operatorClientId.Value == NetworkManager.LocalClientId;

        /// <summary>
        /// True while this machine's bid for the wheel is still alive --
        /// either waiting on a verdict, or granted and still held. Goes
        /// false the moment the server denies the claim, hands the wheel to
        /// somebody else, or drops us for stale input, which is ShipHelm's
        /// cue to stand its player back up.
        /// </summary>
        public bool IsLocalClaimActive
        {
            get
            {
                if (!IsSpawned)
                {
                    return false;
                }

                if (localClaim == HelmClaim.Pending)
                {
                    return Time.unscaledTime < localClaimTimeoutTime;
                }

                return IsLocalClaimGranted;
            }
        }

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
                lastAppliedShipPosition = transform.position;
                hasLastAppliedShipPosition = true;

                NetworkManager.OnClientDisconnectCallback +=
                    HandleClientDisconnectServer;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback -=
                    HandleClientDisconnectServer;
            }

            localClaim = HelmClaim.None;

            base.OnNetworkDespawn();
        }

        /// <summary>
        /// Called on this machine by ShipHelm when its local player takes the
        /// wheel. Marks the claim in flight and asks the server to award it.
        /// </summary>
        public void BeginLocalClaim()
        {
            if (!IsSpawned)
            {
                return;
            }

            localClaim = HelmClaim.Pending;
            localClaimTimeoutTime = Time.unscaledTime + ClaimReplyTimeoutSeconds;

            ClaimHelmServerRpc();
        }

        /// <summary>
        /// Called on this machine by ShipHelm when its local player steps
        /// away, so a later "is the wheel still mine" check cannot re-answer
        /// yes off stale local state.
        /// </summary>
        public void ClearLocalClaim()
        {
            localClaim = HelmClaim.None;
        }

        /// <summary>
        /// Awards the wheel to the caller, but only if it is actually free.
        /// Any connected client may ask -- there is nothing to own ahead of
        /// time -- but exactly one of them can hold it, which is the point.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void ClaimHelmServerRpc(ServerRpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;

            bool granted =
                !HasOperator || operatorClientId.Value == sender;

            if (granted)
            {
                operatorClientId.Value = sender;

                // Start them still, and give them a full stale window before
                // the first input packet has to arrive.
                pendingSteerInput = Vector2.zero;
                inputExpiryTime = Time.unscaledTime + inputStaleSeconds;
            }

            ReplyClaimClientRpc(
                granted,
                new ClientRpcParams
                {
                    Send = new ClientRpcSendParams
                    {
                        TargetClientIds = new[] { sender },
                    },
                }
            );
        }

        [ClientRpc]
        private void ReplyClaimClientRpc(
            bool granted,
            ClientRpcParams rpcParams = default
        )
        {
            // A verdict on a claim we already abandoned (walked away before
            // the reply landed) must not put us back at the wheel.
            if (localClaim != HelmClaim.Pending)
            {
                return;
            }

            localClaim = granted ? HelmClaim.Granted : HelmClaim.Denied;
        }

        /// <summary>
        /// Called by the operator's client while it mans the helm. This is
        /// the LATEST INPUT, not an increment: the server integrates it once
        /// per frame in <see cref="IntegrateSteeringServer"/>, so it does not
        /// matter how many of these arrive in a given frame.
        ///
        /// Unreliable, and sent by the client on change plus a heartbeat
        /// rather than every frame, exactly like TopDownNetworkPlayer2D's own
        /// movement stream. A held direction is one packet every 50 ms, not
        /// one per rendered frame per player.
        /// </summary>
        [ServerRpc(RequireOwnership = false, Delivery = RpcDelivery.Unreliable)]
        public void SubmitSteerInputServerRpc(
            Vector2 rawInput,
            ServerRpcParams rpcParams = default
        )
        {
            // Only the player holding the wheel steers the ship. Without
            // this, whoever sent the most recent packet became the operator
            // by the mere act of sending one.
            if (operatorClientId.Value != rpcParams.Receive.SenderClientId)
            {
                return;
            }

            inputExpiryTime = Time.unscaledTime + inputStaleSeconds;
            pendingSteerInput = Vector2.ClampMagnitude(rawInput, 1f);
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
            if (operatorClientId.Value == rpcParams.Receive.SenderClientId)
            {
                ReleaseHelmServer();
            }
        }

        private void HandleClientDisconnectServer(ulong clientId)
        {
            if (operatorClientId.Value == clientId)
            {
                ReleaseHelmServer();
            }
        }

        private void ReleaseHelmServer()
        {
            operatorClientId.Value = NoOperator;
            pendingSteerInput = Vector2.zero;
        }

        private void LateUpdate()
        {
            if (!IsServer)
            {
                return;
            }

            ExpireStaleOperatorServer();

            // The one authoritative move for this frame, integrated on the
            // server's own clock. Everything below reacts to where that put
            // the ship, so it has to happen first.
            IntegrateSteeringServer();

            // Before anyone is repositioned below -- otherwise a passenger
            // gets moved to match a deck that is about to be shoved
            // somewhere else this same frame. Mirrors the ordering the old
            // ShipHelm.LateUpdate used.
            PushOutOfEnemyHullsServer();

            // Everyone standing on the deck rides along with it. This is what
            // makes the ship feel like a floor rather than a picture sliding
            // around underneath the crew, and it is why the enemy ship
            // shoving the hull no longer leaves players behind (which, with
            // the hull moving out from under them, read as being clipped off
            // the ship).
            CarryPassengersServer();

            // Last, and separately from the general carry: the steersman is
            // pinned to the exact wheel position rather than merely
            // translated. They are not standing on the deck freely, they are
            // fixed to a specific spot on it.
            PinOperatorServer();
        }

        private void ExpireStaleOperatorServer()
        {
            if (HasOperator && Time.unscaledTime >= inputExpiryTime)
            {
                ReleaseHelmServer();
            }
        }

        /// <summary>
        /// Advances the ship by the operator's held input, ONCE per server
        /// frame, using the server's own delta time.
        ///
        /// This used to live in SubmitSteerInputServerRpc, which multiplied
        /// moveSpeed by Time.deltaTime for every packet that arrived. Two
        /// separate things were wrong with that, and together they are the
        /// "ship spasms out" report:
        ///
        /// The delta was the SERVER's frame time, but the number of packets
        /// was set by the CLIENT's frame rate. A client running at 144 fps
        /// against a 60 fps host landed about 2.4 packets per server frame
        /// and got 2.4x the intended speed; a client running at 30 fps got
        /// half. The ship's speed was a function of whose machine was faster.
        ///
        /// And packet arrival is not smooth. Under jitter -- which gets
        /// markedly worse as the third and fourth players add traffic --
        /// several frames' worth of packets bunch up and land together, then
        /// none land at all, so the ship lurched forward and stalled in turn
        /// even though the operator was holding one steady direction.
        ///
        /// Integrating here instead makes the ship's speed depend on nothing
        /// but the server's clock. Dropped or bunched packets change when the
        /// server learns the input, never how fast the ship travels.
        /// </summary>
        private void IntegrateSteeringServer()
        {
            if (!HasOperator || pendingSteerInput == Vector2.zero)
            {
                return;
            }

            steerOffset += pendingSteerInput * (moveSpeed * Time.deltaTime);
            ClampSteerOffset();
            ApplyPositionServer();
        }

        private void ClampSteerOffset()
        {
            steerOffset.x = Mathf.Clamp(steerOffset.x, -moveBounds.x, moveBounds.x);
            steerOffset.y = Mathf.Clamp(steerOffset.y, -moveBounds.y, moveBounds.y);
        }

        private void ApplyPositionServer()
        {
            transform.localPosition = shipHome + (Vector3)steerOffset;
        }

        /// <summary>
        /// Translates every player currently standing on this ship's deck by
        /// however far the deck itself just moved.
        ///
        /// Deliberately a translation and not a re-parent. Re-parenting a
        /// networked player under the ship would mean NGO has to replicate
        /// the parent change and every peer has to agree on when it happened,
        /// and the player's own NetworkTransform syncs in world space -- a
        /// mid-run parent swap is exactly the kind of thing that makes a
        /// player snap to a wrong position on one machine only. Applying the
        /// delta keeps every player a plain, unparented, server-positioned
        /// object, which is what the rest of this project already assumes.
        /// </summary>
        private void CarryPassengersServer()
        {
            Vector3 currentPosition = transform.position;

            if (!hasLastAppliedShipPosition)
            {
                lastAppliedShipPosition = currentPosition;
                hasLastAppliedShipPosition = true;
                return;
            }

            Vector3 delta = currentPosition - lastAppliedShipPosition;
            lastAppliedShipPosition = currentPosition;

            if (delta.sqrMagnitude < 0.0000001f)
            {
                return;
            }

            Collider2D deck = ResolveDeckServer();

            if (deck == null || !deck.isActiveAndEnabled)
            {
                return;
            }

            // The deck collider was moved by the write above, but Physics2D
            // keeps its own copy of collider positions and only refreshes it
            // at the next physics step -- so the aboard test would otherwise
            // be measured against the deck's location from last frame.
            Physics2D.SyncTransforms();

            Bounds aboard = deck.bounds;
            aboard.Expand(new Vector3(aboardMargin * 2f, aboardMargin * 2f, 0f));

            foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
            {
                if (client?.PlayerObject == null)
                {
                    continue;
                }

                TopDownNetworkPlayer2D player =
                    client.PlayerObject.GetComponent<TopDownNetworkPlayer2D>();

                if (player == null)
                {
                    continue;
                }

                // The steersman is skipped: PinOperatorServer puts them on an
                // exact spot immediately after this, so carrying them first
                // would just be a wasted write.
                if (HasOperator && client.ClientId == operatorClientId.Value)
                {
                    continue;
                }

                Vector3 playerPosition = player.transform.position;

                // Compared in 2D: the deck's bounds are flat, and players sit
                // at whatever z their sorting needs, so a 3D containment test
                // would reject everyone.
                if (
                    playerPosition.x < aboard.min.x ||
                    playerPosition.x > aboard.max.x ||
                    playerPosition.y < aboard.min.y ||
                    playerPosition.y > aboard.max.y
                )
                {
                    continue;
                }

                player.PinToStationServer(
                    new Vector2(
                        playerPosition.x + delta.x,
                        playerPosition.y + delta.y
                    )
                );
            }
        }

        /// <summary>
        /// The walkable deck bounds for the aboard test, resolved through the
        /// ship's own PlayerShipMarker (which already falls back to the first
        /// child EdgeCollider2D when the field was never wired).
        /// </summary>
        private Collider2D ResolveDeckServer()
        {
            if (marker == null)
            {
                marker = GetComponent<PlayerShipMarker>();
            }

            return marker == null ? null : marker.DeckBounds;
        }

        private void PinOperatorServer()
        {
            if (
                standPoint == null ||
                !HasOperator ||
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
            PlayerShipMarker shipMarker = GetComponent<PlayerShipMarker>();

            if (shipMarker == null)
            {
                return;
            }

            steeredHull = shipMarker.Hitbox;

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
            ClampSteerOffset();

            ApplyPositionServer();
        }
    }
}
