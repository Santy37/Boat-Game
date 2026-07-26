using DeadmansTales.Networking;
using UnityEngine;

namespace DeadmansTales.Ship
{
    /// <summary>
    /// A boarding point on the enemy ship. Only interactable once the ship
    /// has closed to engagement range (see EnemyShipApproach) and hasn't
    /// sunk. Moves the interacting player onto the enemy deck without
    /// freezing their movement -- unlike ShipCannon/ShipHelm's "seated
    /// station" teleport, a boarding player needs full control to fight.
    /// </summary>
    public sealed class EnemyShipBoardingPoint : NetworkInteractable2D
    {
        [SerializeField]
        [Tooltip("Where the player lands on the enemy deck after boarding.")]
        private Transform boardingDestination;

        [SerializeField]
        [Tooltip("Leave empty to find this ship's own EnemyShipApproach.")]
        private EnemyShipApproach shipApproach;

        [SerializeField]
        [Tooltip("Leave empty to find this ship's own NetworkShipHealth.")]
        private NetworkShipHealth shipHealth;

        public override string InteractionPrompt
        {
            get
            {
                EnemyShipApproach approach = ResolveShipApproach();

                if (
                    approach == null ||
                    approach.State != EnemyShipEngagementState.Engaged
                )
                {
                    return "Too Far to Board";
                }

                return "Press E to Board the Enemy Ship";
            }
        }

        protected override bool CanInteractServer(
            NetworkInteractionController2D interactor
        )
        {
            NetworkShipHealth ship = ResolveShipHealth();

            if (ship != null && ship.IsSunk)
            {
                return false;
            }

            EnemyShipApproach approach = ResolveShipApproach();
            return approach != null &&
                approach.State == EnemyShipEngagementState.Engaged;
        }

        protected override void PerformInteractionServer(
            NetworkInteractionController2D interactor
        )
        {
            if (boardingDestination == null)
            {
                Debug.LogWarning(
                    $"[Boarding Point] '{name}' has no boarding " +
                    "destination assigned.",
                    this
                );
                return;
            }

            TopDownNetworkPlayer2D player =
                interactor.GetComponent<TopDownNetworkPlayer2D>();

            if (player == null)
            {
                Debug.LogWarning(
                    "[Boarding Point] Interactor has no " +
                    "TopDownNetworkPlayer2D to move.",
                    this
                );
                return;
            }

            player.TeleportToSpawnServer(boardingDestination.position);

            Debug.Log(
                $"[Boarding Point] Client {interactor.OwnerClientId} " +
                $"boarded {name}.",
                this
            );
        }

        private EnemyShipApproach ResolveShipApproach()
        {
            if (shipApproach == null)
            {
                shipApproach = GetComponentInParent<EnemyShipApproach>();
            }

            return shipApproach;
        }

        private NetworkShipHealth ResolveShipHealth()
        {
            if (shipHealth == null)
            {
                shipHealth = GetComponentInParent<NetworkShipHealth>();
            }

            return shipHealth;
        }
    }
}
