using System;
using Unity.Netcode;
using UnityEngine;

namespace DeadmansTales.Networking
{
    /// <summary>
    /// Base class for objects that can be used by a network player.
    ///
    /// This class owns only shared interaction rules and authority. Concrete
    /// gameplay objects such as chests, repair points, cannons, pickups, and
    /// exits inherit from it and implement their own server-side behavior.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public abstract class NetworkInteractable2D : NetworkBehaviour
    {
        [Header("Interaction Rules")]
        [SerializeField]
        private bool allowRepeatedInteraction = true;

        [SerializeField]
        [Min(0f)]
        [Tooltip(
            "Extra server-side range allowed for large objects whose visible " +
            "interaction point is wider than their transform position."
        )]
        private float additionalServerRange = 0.25f;

        public readonly NetworkVariable<bool> InteractionEnabled =
            new NetworkVariable<bool>(
                true,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server
            );

        public bool IsInteractionAvailable =>
            IsSpawned && InteractionEnabled.Value;

        public float AdditionalServerRange =>
            Mathf.Max(0f, additionalServerRange);

        /// <summary>
        /// Point used by the server for distance validation.
        /// Override this when the object's useful interaction point is not its
        /// transform position.
        /// </summary>
        public virtual Vector2 InteractionPoint =>
            transform.position;

        /// <summary>
        /// Server-only method for enabling or disabling this interaction.
        /// </summary>
        public void SetInteractionEnabledServer(bool enabled)
        {
            RequireServer(nameof(SetInteractionEnabledServer));
            InteractionEnabled.Value = enabled;
        }

        /// <summary>
        /// Called by NetworkInteractionController2D after ownership, existence,
        /// and distance checks have passed.
        /// </summary>
        internal bool TryInteractServer(
            NetworkInteractionController2D interactor
        )
        {
            RequireServer(nameof(TryInteractServer));

            if (interactor == null)
            {
                return false;
            }

            if (!InteractionEnabled.Value)
            {
                return false;
            }

            if (!CanInteractServer(interactor))
            {
                return false;
            }

            PerformInteractionServer(interactor);

            if (!allowRepeatedInteraction)
            {
                InteractionEnabled.Value = false;
            }

            return true;
        }

        /// <summary>
        /// Optional mechanic-specific server validation.
        /// Examples include checking inventory, objective state, cooldowns, or
        /// whether a repair point is already complete.
        /// </summary>
        protected virtual bool CanInteractServer(
            NetworkInteractionController2D interactor
        )
        {
            return true;
        }

        /// <summary>
        /// Concrete gameplay behavior runs here on the server.
        /// Never trust a client to directly mutate shared game state.
        /// </summary>
        protected abstract void PerformInteractionServer(
            NetworkInteractionController2D interactor
        );

        private void RequireServer(string methodName)
        {
            if (!IsSpawned)
            {
                throw new InvalidOperationException(
                    $"{GetType().Name}.{methodName} was called before its " +
                    "NetworkObject spawned."
                );
            }

            if (!IsServer)
            {
                throw new InvalidOperationException(
                    $"{GetType().Name}.{methodName} may only be called by " +
                    "the server or host."
                );
            }
        }
    }
}
