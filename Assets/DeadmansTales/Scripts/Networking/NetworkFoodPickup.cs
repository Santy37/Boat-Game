using Unity.Netcode;
using UnityEngine;

namespace DeadmansTales.Networking
{
    public sealed class NetworkFoodPickup : NetworkInteractable2D
    {
        [SerializeField]
        private string foodName = "Food";

        [SerializeField]
        [Min(1)]
        private int foodAmount = 1;

        public override string InteractionPrompt =>
            $"PRESS E TO PICK UP";

        protected override bool CanInteractServer(
            NetworkInteractionController2D interactor
        )
        {
            NetworkPlayerLoadout loadout =
                interactor.GetComponent<NetworkPlayerLoadout>();

            return
                loadout != null &&
                loadout.FoodCount.Value < NetworkPlayerLoadout.MaxFood;
        }

        protected override void PerformInteractionServer(
            NetworkInteractionController2D interactor
        )
        {
            NetworkPlayerLoadout loadout =
                interactor.GetComponent<NetworkPlayerLoadout>();

            if (loadout == null)
            {
                return;
            }

            bool added = loadout.AddFoodServer(foodAmount);

            if (!added)
            {
                return;
            }

            Debug.Log(
                $"[Food Pickup] Client {interactor.OwnerClientId} picked up " +
                $"{foodName}. Inventory: {loadout.FoodCount.Value}/" +
                $"{NetworkPlayerLoadout.MaxFood}.",
                this
            );

            NetworkObject.Despawn(true);
        }
    }
}