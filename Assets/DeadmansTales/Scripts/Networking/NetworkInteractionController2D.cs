using Unity.Netcode;
using UnityEngine;

namespace DeadmansTales.Networking
{
    /// <summary>
    /// Player-side gateway for requesting interactions with network objects.
    ///
    /// A locally owned player may request an interaction, but the server still
    /// validates ownership, object existence, availability, and distance before
    /// allowing the concrete interactable to change shared game state.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkInteractionController2D : NetworkBehaviour
    {
        [Header("Server Validation")]
        [SerializeField]
        [Min(0.1f)]
        private float maximumInteractionDistance = 2f;

        [SerializeField]
        [Tooltip(
            "Optional point used for distance checks. The player transform is " +
            "used when this is not assigned."
        )]
        private Transform distanceOrigin;

        [Header("Request Throttling")]
        [SerializeField]
        [Min(0f)]
        private float localRequestCooldown = 0.1f;

        private float nextAllowedRequestTime;

        public float MaximumInteractionDistance =>
            Mathf.Max(0.1f, maximumInteractionDistance);

        /// <summary>
        /// Called by locally owned player input or another local gameplay script.
        /// This method does not perform the interaction directly.
        /// </summary>
        public bool RequestInteraction(NetworkInteractable2D target)
        {
            if (!IsSpawned || !IsOwner)
            {
                return false;
            }

            if (target == null || !target.IsSpawned)
            {
                return false;
            }

            if (Time.unscaledTime < nextAllowedRequestTime)
            {
                return false;
            }

            nextAllowedRequestTime =
                Time.unscaledTime + Mathf.Max(0f, localRequestCooldown);

            NetworkObjectReference targetReference =
                new NetworkObjectReference(target.NetworkObject);

            RequestInteractionServerRpc(targetReference);
            return true;
        }

        /// <summary>
        /// Local helper for UI prompts and target selection. The server repeats
        /// the distance check before accepting the request.
        /// </summary>
        public bool IsWithinLocalRange(NetworkInteractable2D target)
        {
            if (target == null)
            {
                return false;
            }

            return IsWithinServerRange(target);
        }

        [ServerRpc(RequireOwnership = true)]
        private void RequestInteractionServerRpc(
            NetworkObjectReference targetReference,
            ServerRpcParams rpcParams = default
        )
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                Debug.LogWarning(
                    "[Interaction] Rejected request with mismatched ownership.",
                    this
                );
                return;
            }

            if (!targetReference.TryGet(out NetworkObject targetObject))
            {
                return;
            }

            NetworkInteractable2D target =
                targetObject.GetComponent<NetworkInteractable2D>();

            if (target == null || !target.IsInteractionAvailable)
            {
                return;
            }

            if (!IsWithinServerRange(target))
            {
                Debug.LogWarning(
                    $"[Interaction] Client {OwnerClientId} requested " +
                    $"'{target.name}' from outside the allowed range.",
                    this
                );
                return;
            }

            target.TryInteractServer(this);
        }

        private bool IsWithinServerRange(NetworkInteractable2D target)
        {
            Vector2 origin = distanceOrigin != null
                ? distanceOrigin.position
                : transform.position;

            Vector2 difference =
                target.InteractionPoint - origin;

            float allowedDistance =
                MaximumInteractionDistance + target.AdditionalServerRange;

            return difference.sqrMagnitude <=
                allowedDistance * allowedDistance;
        }
    }
}
