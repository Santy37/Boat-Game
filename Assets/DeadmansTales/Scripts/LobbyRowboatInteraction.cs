using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class LobbyRowboatInteraction : MonoBehaviour
{
    [Header("Interaction")]
    [SerializeField] private KeyCode interactionKey = KeyCode.E;

    private TopDownNetworkPlayer2D localPlayerInRange;

    private void Awake()
    {
        Collider2D triggerCollider = GetComponent<Collider2D>();

        if (!triggerCollider.isTrigger)
        {
            Debug.LogError(
                "[Rowboat] The collider on InteractionTrigger must have Is Trigger enabled.",
                this
            );
        }
    }

    private void Update()
    {
        if (localPlayerInRange == null)
        {
            return;
        }

        if (!Input.GetKeyDown(interactionKey))
        {
            return;
        }

        TrySetSail();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TopDownNetworkPlayer2D player =
            other.GetComponentInParent<TopDownNetworkPlayer2D>();

        if (player == null)
        {
            return;
        }

        // Each game instance should only react to its own local player.
        if (!player.IsOwner)
        {
            return;
        }

        localPlayerInRange = player;

        Debug.Log(
            "[Rowboat] Local player entered the rowboat interaction area.",
            this
        );
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        TopDownNetworkPlayer2D player =
            other.GetComponentInParent<TopDownNetworkPlayer2D>();

        if (player == null)
        {
            return;
        }

        if (player != localPlayerInRange)
        {
            return;
        }

        localPlayerInRange = null;

        Debug.Log(
            "[Rowboat] Local player left the rowboat interaction area.",
            this
        );
    }

    private void TrySetSail()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError(
                "[Rowboat] No NetworkManager was found.",
                this
            );

            return;
        }

        if (!NetworkManager.Singleton.IsListening)
        {
            Debug.LogWarning(
                "[Rowboat] Networking has not started yet.",
                this
            );

            return;
        }

        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.Log(
                "[Rowboat] Only the host can start the voyage.",
                this
            );

            return;
        }

        Debug.Log(
            "[Rowboat] HOST PRESSED E — READY TO SET SAIL!",
            this
        );

        // The networked gameplay scene transition will go here
        // after Gameplay_Island_2D is created and configured.
    }

    private void OnGUI()
    {
        if (localPlayerInRange == null)
        {
            return;
        }

        string message;

        if (
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsServer
        )
        {
            message = "Press E to Set Sail";
        }
        else
        {
            message = "Waiting for the Host to Set Sail";
        }

        const float width = 320f;
        const float height = 50f;

        Rect promptRect = new Rect(
            (Screen.width - width) * 0.5f,
            Screen.height - 100f,
            width,
            height
        );

        GUI.Box(promptRect, message);
    }
}