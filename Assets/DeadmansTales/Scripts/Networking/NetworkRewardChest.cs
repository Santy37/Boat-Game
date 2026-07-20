using Unity.Netcode;
using UnityEngine;

namespace DeadmansTales.Networking
{
    public enum NetworkRewardKind : byte
    {
        Healing,
        Weapon,
        Upgrade
    }

    /// <summary>
    /// A one-use, server-authoritative reward chest. Healing works immediately;
    /// weapon and upgrade rewards expose synchronized state for the inventory
    /// system to consume when that teammate-owned system is connected.
    /// </summary>
    public sealed class NetworkRewardChest : NetworkInteractable2D
    {
        [SerializeField]
        private NetworkRewardKind rewardKind = NetworkRewardKind.Healing;

        [SerializeField]
        [Min(0f)]
        private float healingAmount = 50f;

        [SerializeField]
        private GameObject closedVisual;

        [SerializeField]
        private GameObject openedVisual;

        public readonly NetworkVariable<bool> Opened =
            new NetworkVariable<bool>(
                false,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server
            );

        public readonly NetworkVariable<NetworkRewardKind> GrantedReward =
            new NetworkVariable<NetworkRewardKind>(
                NetworkRewardKind.Healing,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server
            );

        public override string InteractionPrompt =>
            Opened.Value
                ? "Chest Opened"
                : "Press E to Open Chest";

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Opened.OnValueChanged += HandleOpenedChanged;
            ApplyVisualState(Opened.Value);
        }

        public override void OnNetworkDespawn()
        {
            Opened.OnValueChanged -= HandleOpenedChanged;
            base.OnNetworkDespawn();
        }

        protected override bool CanInteractServer(
            NetworkInteractionController2D interactor
        )
        {
            return !Opened.Value;
        }

        protected override void PerformInteractionServer(
            NetworkInteractionController2D interactor
        )
        {
            GrantedReward.Value = rewardKind;

            switch (rewardKind)
            {
                case NetworkRewardKind.Healing:
                {
                    PlayerHealth playerHealth =
                        interactor.GetComponent<PlayerHealth>();

                    if (playerHealth != null)
                    {
                        playerHealth.Heal(Mathf.Max(0f, healingAmount));
                    }

                    break;
                }

                case NetworkRewardKind.Weapon:
                {
                    NetworkPlayerLoadout playerLoadout =
                        interactor.GetComponent<NetworkPlayerLoadout>();

                    if (playerLoadout != null)
                    {
                        playerLoadout.GrantWeaponServer();
                    }

                    break;
                }

                case NetworkRewardKind.Upgrade:
                {
                    NetworkPlayerLoadout playerLoadout =
                        interactor.GetComponent<NetworkPlayerLoadout>();

                    if (playerLoadout != null)
                    {
                        playerLoadout.GrantUpgradeServer();
                    }

                    break;
                }
            }

            Opened.Value = true;

            Debug.Log(
                $"[Reward Chest] Client {interactor.OwnerClientId} received " +
                $"{rewardKind} from {name}.",
                this
            );
        }

        private void HandleOpenedChanged(bool previousValue, bool currentValue)
        {
            ApplyVisualState(currentValue);
        }

        private void ApplyVisualState(bool opened)
        {
            if (closedVisual != null)
            {
                closedVisual.SetActive(!opened);
            }

            if (openedVisual != null)
            {
                openedVisual.SetActive(opened);
            }
        }
    }
}
