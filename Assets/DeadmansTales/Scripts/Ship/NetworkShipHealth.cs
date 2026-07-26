using DeadmansTales.Networking;
using Unity.Netcode;
using UnityEngine;

namespace DeadmansTales.Ship
{
    /// <summary>
    /// Server-authoritative shared ship health for the boat survival loop.
    /// This is the persistent, "way larger" pool -- nothing damages it
    /// directly. It only drains via NetworkShipSinkMeter's continuous leak
    /// while SinkLevel is below full, and it cannot be repaired mid-voyage;
    /// it only mends when the run advances to the next stage (reaching the
    /// next island), which this class listens for on NetworkRunState
    /// itself rather than NetworkRunState knowing anything about ships. The
    /// run is lost when the ship sinks (GDD lose state).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkShipHealth : NetworkBehaviour
    {
        [SerializeField]
        [Min(1f)]
        private float maximumHealth = 500f;

        [SerializeField]
        [Min(0f)]
        [Tooltip(
            "Flat amount of Health restored each time the run advances to " +
            "the next stage (the crew reaches the next island), clamped " +
            "to MaximumHealth. This is the only way Health is restored -- " +
            "it cannot be repaired mid-voyage."
        )]
        private float healthRestoredPerStageAdvance = 200f;

        public readonly NetworkVariable<float> CurrentHealth =
            new NetworkVariable<float>(
                0f,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server
            );

        public float MaximumHealth => Mathf.Max(1f, maximumHealth);

        public bool IsSunk => IsSpawned && CurrentHealth.Value <= 0f;

        public float HealthFraction =>
            Mathf.Clamp01(CurrentHealth.Value / MaximumHealth);

        /// <summary>Raised on every peer when the ship sinks.</summary>
        public event System.Action ShipSunk;

        private bool isPlayerShip;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Enemy ships carry this same component. Only the player's own
            // ship (marked with PlayerShipMarker) failing the run or mending
            // between stages makes sense -- an enemy sinking must not end
            // the player's run, and an enemy ship has no "next island" to
            // benefit from.
            isPlayerShip = GetComponent<PlayerShipMarker>() != null;

            CurrentHealth.OnValueChanged += HandleHealthChanged;

            if (IsServer && CurrentHealth.Value <= 0f)
            {
                CurrentHealth.Value = MaximumHealth;
            }

            if (IsServer && NetworkRunState.Instance != null)
            {
                NetworkRunState.Instance.CurrentStage.OnValueChanged +=
                    HandleStageAdvancedServer;
            }
        }

        public override void OnNetworkDespawn()
        {
            CurrentHealth.OnValueChanged -= HandleHealthChanged;

            if (IsServer && NetworkRunState.Instance != null)
            {
                NetworkRunState.Instance.CurrentStage.OnValueChanged -=
                    HandleStageAdvancedServer;
            }

            base.OnNetworkDespawn();
        }

        public bool TakeDamageServer(float damage)
        {
            if (!IsSpawned || !IsServer || IsSunk || damage <= 0f)
            {
                return false;
            }

            CurrentHealth.Value = Mathf.Clamp(
                CurrentHealth.Value - damage,
                0f,
                MaximumHealth
            );
            return true;
        }

        public bool RepairServer(float amount)
        {
            if (!IsSpawned || !IsServer || IsSunk || amount <= 0f)
            {
                return false;
            }

            CurrentHealth.Value = Mathf.Clamp(
                CurrentHealth.Value + amount,
                0f,
                MaximumHealth
            );
            return true;
        }

        /// <summary>
        /// Reaching the next stage (island) is the only time Health mends.
        /// Listens on NetworkRunState's own CurrentStage rather than having
        /// NetworkRunState call into ship-specific code.
        /// </summary>
        private void HandleStageAdvancedServer(
            int previousStage,
            int currentStage
        )
        {
            if (
                !isPlayerShip ||
                !IsServer ||
                !IsSpawned ||
                IsSunk ||
                currentStage <= previousStage
            )
            {
                return;
            }

            RepairServer(healthRestoredPerStageAdvance);

            Debug.Log(
                $"[Ship Health] Reached stage {currentStage}; restored " +
                $"{healthRestoredPerStageAdvance:0} hull integrity " +
                $"({CurrentHealth.Value:0}/{MaximumHealth:0}).",
                this
            );
        }

        private void HandleHealthChanged(float previousValue, float newValue)
        {
            if (previousValue > 0f && newValue <= 0f)
            {
                ShipSunk?.Invoke();

                if (!isPlayerShip)
                {
                    Debug.Log($"[Ship Health] {name} has sunk.", this);
                    return;
                }

                Debug.Log("[Ship Health] The ship has sunk. Run lost.", this);

                if (IsServer && NetworkRunState.Instance != null)
                {
                    NetworkRunState.Instance.SetStatusServer(
                        NetworkRunStatus.Failed
                    );
                }
            }
        }
    }
}
