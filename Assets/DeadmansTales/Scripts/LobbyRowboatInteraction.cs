using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Collider2D))]
public class LobbyRowboatInteraction : MonoBehaviour
{
    [Header("Interaction")]
    [SerializeField]
    private KeyCode interactionKey =
        KeyCode.E;

    [Header("Destination")]
    [SerializeField]
    private string gameplaySceneName =
        "Boat_Gameplay_2D";

    private TopDownNetworkPlayer2D
        localPlayerInRange;

    private bool sceneLoadRequested;

    private void Awake()
    {
        Collider2D triggerCollider =
            GetComponent<Collider2D>();

        if (!triggerCollider.isTrigger)
        {
            Debug.LogError(
                "[Rowboat] InteractionTrigger collider " +
                "must have Is Trigger enabled.",
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

        if (sceneLoadRequested)
        {
            return;
        }

        if (!Input.GetKeyDown(interactionKey))
        {
            return;
        }

        TrySetSail();
    }

    private void OnTriggerEnter2D(
        Collider2D other
    )
    {
        TopDownNetworkPlayer2D player =
            other.GetComponentInParent<
                TopDownNetworkPlayer2D
            >();

        if (player == null)
        {
            return;
        }

        if (!player.IsOwner)
        {
            return;
        }

        localPlayerInRange = player;

        Debug.Log(
            "[Rowboat] Local player entered " +
            "the rowboat interaction area.",
            this
        );
    }

    private void OnTriggerExit2D(
        Collider2D other
    )
    {
        TopDownNetworkPlayer2D player =
            other.GetComponentInParent<
                TopDownNetworkPlayer2D
            >();

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
            "[Rowboat] Local player left " +
            "the rowboat interaction area.",
            this
        );
    }

    private void TrySetSail()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError(
                "[Rowboat] No NetworkManager exists.",
                this
            );

            return;
        }

        if (!NetworkManager.Singleton.IsListening)
        {
            Debug.LogWarning(
                "[Rowboat] Networking has not started.",
                this
            );

            return;
        }

        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.Log(
                "[Rowboat] Only the host can " +
                "start the voyage.",
                this
            );

            return;
        }

        if (sceneLoadRequested)
        {
            return;
        }

        SceneEventProgressStatus status =
            NetworkManager
                .Singleton
                .SceneManager
                .LoadScene(
                    gameplaySceneName,
                    LoadSceneMode.Single
                );

        if (
            status ==
            SceneEventProgressStatus.Started
        )
        {
            sceneLoadRequested = true;

            Debug.Log(
                $"[Rowboat] Voyage started. " +
                $"Loading {gameplaySceneName}.",
                this
            );
        }
        else
        {
            Debug.LogError(
                $"[Rowboat] Failed to load " +
                $"{gameplaySceneName}. " +
                $"Status: {status}",
                this
            );
        }
    }

    private void OnGUI()
    {
        if (localPlayerInRange == null)
        {
            return;
        }

        string message;

        if (sceneLoadRequested)
        {
            message = "Setting Sail...";
        }
        else if (
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsServer
        )
        {
            message = "Press E to Set Sail";
        }
        else
        {
            message =
                "Waiting for the Host to Set Sail";
        }

        const float width = 320f;
        const float height = 50f;

        Rect promptRect =
            new Rect(
                (Screen.width - width) * 0.5f,
                Screen.height - 100f,
                width,
                height
            );

        GUI.Box(
            promptRect,
            message
        );
    }
}