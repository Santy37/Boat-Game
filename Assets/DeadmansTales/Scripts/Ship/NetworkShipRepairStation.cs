using DeadmansTales.Networking;
using Unity.Netcode;
using UnityEngine;

namespace DeadmansTales.Ship
{
    /// <summary>
    /// A repeatable ship-repair interaction for the boat survival loop.
    /// Any player presses E at the station to restore ship health; the
    /// server validates and applies the repair.
    /// </summary>
    public sealed class NetworkShipRepairStation : NetworkInteractable2D
    {
        [SerializeField]
        [Min(1f)]
        private float repairPerUse = 40f;

        [SerializeField]
        private NetworkShipHealth shipHealth;

        public override string InteractionPrompt
        {
            get
            {
                NetworkShipHealth ship = ResolveShipHealth();
                if (ship == null)
                {
                    return "No Ship To Repair";
                }

                if (ship.IsSunk)
                {
                    return "The Ship Has Sunk";
                }

                if (ship.HealthFraction >= 1f)
                {
                    return "Hull Is Fully Repaired";
                }

                return "Press E to Repair the Hull " +
                    $"({ship.CurrentHealth.Value:0}/{ship.MaximumHealth:0})";
            }
        }

        protected override bool CanInteractServer(
            NetworkInteractionController2D interactor
        )
        {
            NetworkShipHealth ship = ResolveShipHealth();
            return ship != null && !ship.IsSunk && ship.HealthFraction < 1f;
        }

        protected override void PerformInteractionServer(
            NetworkInteractionController2D interactor
        )
        {
            NetworkShipHealth ship = ResolveShipHealth();
            if (ship == null)
            {
                return;
            }

            ship.RepairServer(repairPerUse);

            Debug.Log(
                $"[Repair Station] Client {interactor.OwnerClientId} " +
                $"repaired the hull to {ship.CurrentHealth.Value:0}.",
                this
            );
        }

        private NetworkShipHealth ResolveShipHealth()
        {
            if (shipHealth == null)
            {
                shipHealth = FindFirstObjectByType<NetworkShipHealth>();
            }

            return shipHealth;
        }
    }
}
