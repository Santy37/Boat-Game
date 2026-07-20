using Unity.Netcode;
using UnityEngine;

namespace DeadmansTales.Networking
{
    /// <summary>
    /// A one-use island food pickup for the checkpoint heal loop. Eating the
    /// food heals the interacting player on the server, then the pickup
    /// despawns for everyone. Players at full health cannot waste it.
    /// </summary>
    public sealed class NetworkFoodPickup : NetworkInteractable2D
    {
        [SerializeField]
        private string foodName = "Food";

        [SerializeField]
        [Min(1f)]
        private float healAmount = 25f;

        public override string InteractionPrompt =>
            $"Press E to Eat {foodName} (+{healAmount:0} HP)";

        protected override bool CanInteractServer(
            NetworkInteractionController2D interactor
        )
        {
            PlayerHealth health = interactor.GetComponent<PlayerHealth>();

            return
                health != null &&
                health.IsAlive &&
                health.CurrentHealth.Value < health.MaximumHealth;
        }

        protected override void PerformInteractionServer(
            NetworkInteractionController2D interactor
        )
        {
            PlayerHealth health = interactor.GetComponent<PlayerHealth>();

            if (health != null)
            {
                health.Heal(Mathf.Max(1f, healAmount));

                Debug.Log(
                    $"[Food] Client {interactor.OwnerClientId} ate " +
                    $"{foodName} (+{healAmount:0} HP).",
                    this
                );
            }

            // One bite per pickup: remove it for every player.
            NetworkObject.Despawn(true);
        }
    }
}
