using DeadmansTales.Networking;
using Unity.Netcode;
using UnityEngine;

namespace DeadmansTales.Ship
{
    /// <summary>
    /// A smaller, more volatile meter that sits in front of
    /// <see cref="NetworkShipHealth"/>. It behaves like Health itself --
    /// starts full, drops when the ship is hit, and is what the crew
    /// actually repairs mid-voyage. Health does not take damage directly;
    /// instead, for as long as SinkLevel is below full, Health continuously
    /// drains, faster the more SinkLevel is down. Health itself is only
    /// restored between voyages (see NetworkRunState.AdvanceStageServer), so
    /// keeping SinkLevel patched up is what actually protects the ship.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkShipSinkMeter : NetworkBehaviour
    {
        [SerializeField]
        [Min(1f)]
        private float maximumSinkLevel = 300f;

        [SerializeField]
        [Min(0f)]
        [Tooltip(
            "How much Health per second drains at maximum -- i.e. when " +
            "SinkLevel is fully drained to 0. Scales down to 0 drain as " +
            "SinkLevel approaches full."
        )]
        private float maximumHealthDrainPerSecond = 8f;

        [SerializeField]
        [Tooltip(
            "Leave empty to find the ship's NetworkShipHealth automatically."
        )]
        private NetworkShipHealth shipHealth;

        public readonly NetworkVariable<float> CurrentSinkLevel =
            new NetworkVariable<float>(
                0f,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server
            );

        /// <summary>
        /// Base capacity plus whatever the crew has bought at the ship
        /// shop this run (NetworkRunState.ShipSinkBonus persists across
        /// scenes, so this stays boosted even though the ship itself is
        /// rebuilt fresh each time Boat_Gameplay_2D/Kraken_Arena_2D loads).
        /// </summary>
        public float MaximumSinkLevel
        {
            get
            {
                float bonus = NetworkRunState.Instance != null
                    ? NetworkRunState.Instance.ShipSinkBonus.Value
                    : 0f;

                return Mathf.Max(1f, maximumSinkLevel + bonus);
            }
        }

        public float SinkFraction =>
            Mathf.Clamp01(CurrentSinkLevel.Value / MaximumSinkLevel);

        /// <summary>0 when pristine, 1 when SinkLevel is fully drained.</summary>
        public float DamagedFraction => 1f - SinkFraction;

        private bool isPlayerShip;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Enemy ships carry this same component -- only the player's
            // own ship should fully patch back up on reaching the next
            // island. Mirrors the same isPlayerShip gating NetworkShipHealth
            // uses for its own stage-advance restore.
            isPlayerShip = GetComponent<PlayerShipMarker>() != null;

            if (IsServer && CurrentSinkLevel.Value <= 0f)
            {
                CurrentSinkLevel.Value = MaximumSinkLevel;
            }

            if (IsServer && isPlayerShip && NetworkRunState.Instance != null)
            {
                NetworkRunState.Instance.CurrentStage.OnValueChanged +=
                    HandleStageAdvancedServer;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && isPlayerShip && NetworkRunState.Instance != null)
            {
                NetworkRunState.Instance.CurrentStage.OnValueChanged -=
                    HandleStageAdvancedServer;
            }

            base.OnNetworkDespawn();
        }

        /// <summary>
        /// Reaching the next stage (island) fully patches SinkLevel back up,
        /// same trigger NetworkShipHealth listens for on NetworkRunState
        /// directly.
        /// </summary>
        private void HandleStageAdvancedServer(
            int previousStage,
            int currentStage
        )
        {
            if (!IsServer || !IsSpawned || currentStage <= previousStage)
            {
                return;
            }

            CurrentSinkLevel.Value = MaximumSinkLevel;

            Debug.Log(
                $"[Ship Sink Meter] Reached stage {currentStage}; hull " +
                $"fully patched ({CurrentSinkLevel.Value:0}/" +
                $"{MaximumSinkLevel:0}).",
                this
            );
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned)
            {
                return;
            }

            TickHealthDrain();
        }

        /// <summary>
        /// Server-only: raw damage to SinkLevel, e.g. from a leak.
        /// </summary>
        public bool TakeDamageServer(float damage)
        {
            if (!IsSpawned || !IsServer || damage <= 0f)
            {
                return false;
            }

            CurrentSinkLevel.Value = Mathf.Clamp(
                CurrentSinkLevel.Value - damage,
                0f,
                MaximumSinkLevel
            );

            return true;
        }

        /// <summary>
        /// Server-only: a cannon hit's damage to SinkLevel, scaled by how
        /// direct the hit was. <paramref name="directness01"/> is 1.0 for a
        /// dead-center hit, tapering to 0 at the edge of the hull.
        ///
        /// Once SinkLevel is already at 0, there's nothing left here to
        /// absorb the blow -- any damage that doesn't fit into what SinkLevel
        /// still has left spills straight through to Health instead of being
        /// silently wasted on a clamp. A partial hit (SinkLevel has *some*
        /// room left, but not enough) splits the same way: it drains the
        /// rest of SinkLevel, then the remainder carries over to Health.
        /// </summary>
        public bool ApplyCannonHitServer(float damage, float directness01)
        {
            if (!IsSpawned || !IsServer)
            {
                return false;
            }

            float scaledDamage = damage * Mathf.Clamp01(directness01);

            if (scaledDamage <= 0f)
            {
                return false;
            }

            float sinkDamage = Mathf.Min(scaledDamage, CurrentSinkLevel.Value);
            float overflowDamage = scaledDamage - sinkDamage;

            if (sinkDamage > 0f)
            {
                TakeDamageServer(sinkDamage);
            }

            if (overflowDamage > 0f)
            {
                NetworkShipHealth resolvedShipHealth = ResolveShipHealth();

                if (resolvedShipHealth != null && !resolvedShipHealth.IsSunk)
                {
                    resolvedShipHealth.TakeDamageServer(overflowDamage);
                }
            }

            return true;
        }

        /// <summary>Server-only: patches SinkLevel back up. This is what a
        /// manned repair station calls -- Health itself cannot be repaired
        /// mid-voyage.</summary>
        public bool RepairServer(float amount)
        {
            if (!IsSpawned || !IsServer || amount <= 0f)
            {
                return false;
            }

            CurrentSinkLevel.Value = Mathf.Clamp(
                CurrentSinkLevel.Value + amount,
                0f,
                MaximumSinkLevel
            );

            return true;
        }

        private void TickHealthDrain()
        {
            float damagedFraction = DamagedFraction;

            if (damagedFraction <= 0f)
            {
                return;
            }

            NetworkShipHealth resolvedShipHealth = ResolveShipHealth();

            if (resolvedShipHealth == null || resolvedShipHealth.IsSunk)
            {
                return;
            }

            resolvedShipHealth.TakeDamageServer(
                maximumHealthDrainPerSecond * damagedFraction * Time.deltaTime
            );
        }

        private NetworkShipHealth ResolveShipHealth()
        {
            // Deliberately no scene-wide FindFirstObjectByType fallback --
            // this sink meter and its ship's health always live on the same
            // GameObject (player ship or enemy ship alike), and with more
            // than one ship in the scene, a blind global find could grab a
            // different ship's health entirely.
            if (shipHealth == null)
            {
                shipHealth = GetComponent<NetworkShipHealth>();
            }

            return shipHealth;
        }
    }
}
