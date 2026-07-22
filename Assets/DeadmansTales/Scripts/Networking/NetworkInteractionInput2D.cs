using Unity.Netcode;
using UnityEngine;

namespace DeadmansTales.Networking
{
    /// <summary>
    /// Finds the nearest network interactable for the locally owned player and
    /// sends an interaction request through NetworkInteractionController2D.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkInteractionController2D))]
    public sealed class NetworkInteractionInput2D : NetworkBehaviour
    {
        private const int MaximumOverlapResults = 32;

        [SerializeField]
        private KeyCode interactionKey = KeyCode.E;

        [SerializeField]
        [Min(0.25f)]
        private float searchRadius = 2f;

        private readonly Collider2D[] overlapResults =
            new Collider2D[MaximumOverlapResults];

        private NetworkInteractionController2D controller;
        private NetworkInteractable2D currentTarget;

        /// <summary>
        /// What the local player is currently standing close enough to use.
        /// Exposed so richer screens (the shop panel) can render the same
        /// target this component would act on, instead of repeating the
        /// overlap query and risking a different answer.
        /// </summary>
        public NetworkInteractable2D CurrentTarget => currentTarget;

        /// <summary>Sends the interaction the interact key would send.</summary>
        public bool RequestInteractionWithCurrentTarget()
        {
            return currentTarget != null &&
                controller.RequestInteraction(currentTarget);
        }

        private void Awake()
        {
            controller = GetComponent<NetworkInteractionController2D>();
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner || PauseMenu.InputBlocked)
            {
                currentTarget = null;
                return;
            }

            currentTarget = FindNearestTarget();

            if (
                currentTarget != null &&
                Input.GetKeyDown(interactionKey)
            )
            {
                controller.RequestInteraction(currentTarget);
            }
        }

        private NetworkInteractable2D FindNearestTarget()
        {
            int hitCount = Physics2D.OverlapCircle(
                transform.position,
                Mathf.Max(0.25f, searchRadius),
                ContactFilter2D.noFilter,
                overlapResults
            );

            NetworkInteractable2D bestTarget = null;
            float bestDistanceSquared = float.MaxValue;

            for (int index = 0; index < hitCount; index++)
            {
                Collider2D overlap = overlapResults[index];
                overlapResults[index] = null;

                if (overlap == null)
                {
                    continue;
                }

                NetworkInteractable2D candidate =
                    overlap.GetComponentInParent<NetworkInteractable2D>();

                if (
                    candidate == null ||
                    !candidate.IsInteractionAvailable ||
                    !controller.IsWithinLocalRange(candidate)
                )
                {
                    continue;
                }

                float distanceSquared = (
                    candidate.InteractionPoint -
                    (Vector2)transform.position
                ).sqrMagnitude;

                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    bestTarget = candidate;
                }
            }

            return bestTarget;
        }

        private void OnGUI()
        {
            if (
                !IsSpawned ||
                !IsOwner ||
                currentTarget == null ||
                currentTarget.DrawsOwnScreen
            )
            {
                return;
            }

            const float width = 360f;
            const float height = 46f;
            Rect promptRect = new Rect(
                (Screen.width - width) * 0.5f,
                Screen.height - 100f,
                width,
                height
            );

            GUI.Box(promptRect, currentTarget.InteractionPrompt);
        }
    }
}
