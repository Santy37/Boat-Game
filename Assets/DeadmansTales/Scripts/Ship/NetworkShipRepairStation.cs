using DeadmansTales.Networking;
using Unity.Netcode;
using UnityEngine;

namespace DeadmansTales.Ship
{
    /// <summary>
    /// A repeatable ship-repair interaction for the boat survival loop.
    /// Any player presses E at the station to restore SinkLevel; the server
    /// validates and applies the repair. Health itself is NOT repairable
    /// here by design -- it's the persistent, "way larger pool" that only
    /// mends between voyages (see NetworkRunState.AdvanceStageServer).
    /// Keeping SinkLevel patched is what protects it mid-voyage.
    /// </summary>
    public sealed class NetworkShipRepairStation : NetworkInteractable2D
    {
        [SerializeField]
        [Min(1f)]
        private float repairPerUse = 40f;

        [SerializeField]
        private NetworkShipSinkMeter sinkMeter;

        [SerializeField]
        [Tooltip(
            "Leave empty to find the ship's NetworkShipHealth automatically. " +
            "Only used to stop offering repairs once the ship has actually " +
            "sunk."
        )]
        private NetworkShipHealth shipHealth;

        public override string InteractionPrompt
        {
            get
            {
                NetworkShipHealth ship = ResolveShipHealth();

                if (ship != null && ship.IsSunk)
                {
                    return "The Ship Has Sunk";
                }

                NetworkShipSinkMeter meter = ResolveSinkMeter();

                if (meter == null)
                {
                    return "No Ship To Repair";
                }

                if (meter.SinkFraction >= 1f)
                {
                    return "Hull Is Fully Repaired";
                }

                return "Press E to Repair the Hull " +
                    $"({meter.CurrentSinkLevel.Value:0}/{meter.MaximumSinkLevel:0})";
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

            NetworkShipSinkMeter meter = ResolveSinkMeter();
            return meter != null && meter.SinkFraction < 1f;
        }

        protected override void PerformInteractionServer(
            NetworkInteractionController2D interactor
        )
        {
            NetworkShipSinkMeter meter = ResolveSinkMeter();

            if (meter == null)
            {
                return;
            }

            meter.RepairServer(repairPerUse);

            Debug.Log(
                $"[Repair Station] Client {interactor.OwnerClientId} " +
                $"repaired the hull to {meter.CurrentSinkLevel.Value:0}.",
                this
            );
        }

        private NetworkShipSinkMeter ResolveSinkMeter()
        {
            // Repair stations only ever exist on the player's own ship --
            // resolve through PlayerShipMarker rather than a scene-wide
            // find, since enemy ships now carry this same component type
            // and a blind find could grab the wrong ship's meter.
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

        private NetworkShipHealth ResolveShipHealth()
        {
            if (shipHealth == null)
            {
                PlayerShipMarker playerShip =
                    FindFirstObjectByType<PlayerShipMarker>();

                shipHealth = playerShip != null
                    ? playerShip.GetComponent<NetworkShipHealth>()
                    : null;
            }

            return shipHealth;
        }
    }
}
