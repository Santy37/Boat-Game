using DeadmansTales.Networking;
using Unity.Netcode;
using UnityEngine;

namespace DeadmansTales.Ship
{
    /// <summary>
    /// Server-authoritative shared ship health for the boat survival loop.
    /// Enemies and hazards call <see cref="TakeDamageServer"/>; repair
    /// stations call <see cref="RepairServer"/>. The run is lost when the
    /// ship sinks (GDD lose state).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkShipHealth : NetworkBehaviour
    {
        [SerializeField]
        [Min(1f)]
        private float maximumHealth = 500f;

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

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            CurrentHealth.OnValueChanged += HandleHealthChanged;

            if (IsServer && CurrentHealth.Value <= 0f)
            {
                CurrentHealth.Value = MaximumHealth;
            }
        }

        public override void OnNetworkDespawn()
        {
            CurrentHealth.OnValueChanged -= HandleHealthChanged;
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

        private void HandleHealthChanged(float previousValue, float newValue)
        {
            if (previousValue > 0f && newValue <= 0f)
            {
                Debug.Log("[Ship Health] The ship has sunk. Run lost.", this);
                ShipSunk?.Invoke();

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
