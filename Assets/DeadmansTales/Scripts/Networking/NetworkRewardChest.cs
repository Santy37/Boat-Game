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

        [Header("Food spill")]
        [Tooltip("Food pickups scattered around the chest when it opens. Each "
            + "prefab needs a NetworkObject and must be registered in the "
            + "NetworkManager prefab list. Leave empty for no food.")]
        [SerializeField]
        private GameObject[] foodRewardPrefabs;

        [Tooltip("How many food pickups to spill. 0 disables the spill.")]
        [SerializeField]
        [Min(0)]
        private int foodRewardCount;

        [Tooltip("How far from the chest the food lands.")]
        [SerializeField]
        [Min(0f)]
        private float foodScatterRadius = 1.1f;

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
                : "PRESS E TO OPEN";

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
                    // Deliberately a no-op: weapon tier now only advances
                    // through the sword shop (NetworkShopVendor). Chests
                    // used to also call GrantWeaponServer() here, which let
                    // a beach loot chest silently upgrade the blade before
                    // the player ever visited a shop -- removed so the shop
                    // is the sole source of weapon upgrades.
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

            SpillFoodServer();

            Opened.Value = true;

            Debug.Log(
                $"[Reward Chest] Client {interactor.OwnerClientId} received " +
                $"{rewardKind} from {name}.",
                this
            );
        }

        /// <summary>
        /// Scatters food pickups around the chest so opening one leaves
        /// something on the ground to eat, rather than only applying an
        /// invisible effect. Server-only: each pickup is a NetworkObject the
        /// server spawns for everyone.
        /// </summary>
        private void SpillFoodServer()
        {
            if (
                foodRewardCount <= 0 ||
                foodRewardPrefabs == null ||
                foodRewardPrefabs.Length == 0
            )
            {
                return;
            }

            // Deterministic per chest, so every client's debug view agrees and
            // re-running a seeded stage lays the food out the same way.
            System.Random random = new System.Random(
                unchecked((int)NetworkObjectId * 397)
            );

            int spawned = 0;
            for (int index = 0; index < foodRewardCount; index++)
            {
                GameObject prefab = foodRewardPrefabs[
                    random.Next(foodRewardPrefabs.Length)
                ];

                if (prefab == null)
                {
                    continue;
                }

                // Ring the chest so multiple pickups never stack on one spot.
                double angle =
                    (index / (double)foodRewardCount) * System.Math.PI * 2d +
                    random.NextDouble() * 0.6d;
                float distance = foodScatterRadius *
                    (0.7f + 0.3f * (float)random.NextDouble());

                Vector3 offset = new Vector3(
                    (float)System.Math.Cos(angle) * distance,
                    (float)System.Math.Sin(angle) * distance,
                    0f
                );

                GameObject food = Instantiate(
                    prefab,
                    transform.position + offset,
                    Quaternion.identity
                );

                NetworkObject foodNetworkObject =
                    food.GetComponent<NetworkObject>();

                if (foodNetworkObject == null)
                {
                    Debug.LogError(
                        $"[Reward Chest] '{prefab.name}' has no NetworkObject; " +
                        "it cannot be spawned as food.",
                        this
                    );
                    Destroy(food);
                    continue;
                }

                // destroyWithScene: true. NGO defaults this to FALSE, which
                // parks dynamically spawned objects in DontDestroyOnLoad --
                // uneaten food then followed the crew out of the island,
                // floating in the sea in the shop and still lying around in
                // the boat scene. Every other spawner here passes true.
                foodNetworkObject.Spawn(true);
                spawned++;
            }

            if (spawned > 0)
            {
                Debug.Log(
                    $"[Reward Chest] {name} spilled {spawned} food pickup(s).",
                    this
                );
            }
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
