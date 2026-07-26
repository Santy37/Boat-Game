using DeadmansTales.Networking;
using Unity.Netcode;
using UnityEngine;

namespace DeadmansTales.Ship
{
    /// <summary>
    /// A hull leak on the boat. While active it steadily damages the ship's
    /// SinkLevel until a player patches it with E -- the same meter cannon
    /// hits damage, and the same one repair stations restore. Leaks are
    /// scene-placed and dormant by default; <see cref="ShipLeakDirector"/>
    /// opens them on an escalating timer during the run.
    /// </summary>
    public sealed class NetworkShipLeak : NetworkInteractable2D
    {
        [SerializeField]
        [Min(0.1f)]
        private float damagePerSecond = 4f;

        [SerializeField]
        private NetworkShipSinkMeter sinkMeter;

        [SerializeField]
        [Tooltip("Visual shown only while the leak is open.")]
        private GameObject leakVisual;

        public readonly NetworkVariable<bool> LeakActive =
            new NetworkVariable<bool>(
                false,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server
            );

        public override string InteractionPrompt =>
            LeakActive.Value
                ? "Press E to Patch the Leak!"
                : "Hull Is Sound Here";

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            LeakActive.OnValueChanged += HandleLeakChanged;
            ApplyVisualState(LeakActive.Value);
        }

        public override void OnNetworkDespawn()
        {
            LeakActive.OnValueChanged -= HandleLeakChanged;
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer || !LeakActive.Value)
            {
                return;
            }

            NetworkShipSinkMeter meter = ResolveSinkMeter();

            if (meter == null)
            {
                return;
            }

            meter.TakeDamageServer(damagePerSecond * Time.deltaTime);
        }

        /// <summary>Server-only: opens this leak.</summary>
        public bool ActivateServer()
        {
            if (!IsSpawned || !IsServer || LeakActive.Value)
            {
                return false;
            }

            LeakActive.Value = true;

            Debug.Log($"[Ship Leak] {name} burst open!", this);
            return true;
        }

        protected override bool CanInteractServer(
            NetworkInteractionController2D interactor
        )
        {
            return LeakActive.Value;
        }

        protected override void PerformInteractionServer(
            NetworkInteractionController2D interactor
        )
        {
            LeakActive.Value = false;

            Debug.Log(
                $"[Ship Leak] Client {interactor.OwnerClientId} patched " +
                $"{name}.",
                this
            );
        }

        private void HandleLeakChanged(bool previousValue, bool currentValue)
        {
            ApplyVisualState(currentValue);
        }

        private void ApplyVisualState(bool active)
        {
            if (leakVisual != null)
            {
                leakVisual.SetActive(active);
            }
        }

        private NetworkShipSinkMeter ResolveSinkMeter()
        {
            // Leaks only ever exist on the player's own ship -- resolve
            // through PlayerShipMarker rather than a scene-wide find, since
            // enemy ships now carry this same component type and a blind
            // find could grab the wrong ship's meter.
            if (sinkMeter == null)
            {
                PlayerShipMarker playerShip =
                    FindFirstObjectByType<PlayerShipMarker>();

                sinkMeter = playerShip != null
                    ? playerShip.GetComponent<NetworkShipSinkMeter>()
                    : null;
            }

            return sinkMeter;
        }
    }
}
