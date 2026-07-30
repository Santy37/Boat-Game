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

    [Header("Networking")]
    [Tooltip(
        "Extra slack, in world units, added around this trigger when the " +
        "SERVER re-checks that a client asking to set sail really is " +
        "standing at the rowboat. The client's own trigger membership is " +
        "not evidence the server can take on faith."
    )]
    [SerializeField]
    [Min(0f)]
    private float serverRangeMargin = 1f;

    private TopDownNetworkPlayer2D
        localPlayerInRange;

    private Collider2D triggerCollider;

    // Server-side: the scene load is under way.
    private bool sceneLoadRequested;

    // Client-side: we have asked the server to set sail and are waiting for
    // the scene change. Kept separate because sceneLoadRequested is only
    // ever true on the server, and without this a client would keep showing
    // "Press E" (and keep re-sending) after it had already asked.
    private bool sailRequestSent;
    private float sailRequestRetryTime;

    // If the server refuses -- it did not place us at the boat, or an enemy
    // it can see is still alive -- no reply comes back, so the request has
    // to lapse on its own. Without this the client would sit on "Setting
    // Sail..." forever and could never press E again.
    private const float SailRequestRetrySeconds = 2f;

    private void Awake()
    {
        triggerCollider = GetComponent<Collider2D>();

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

        if (
            sailRequestSent &&
            Time.unscaledTime >= sailRequestRetryTime
        )
        {
            sailRequestSent = false;
        }

        if (sceneLoadRequested || sailRequestSent)
        {
            return;
        }

        if (!Input.GetKeyDown(interactionKey))
        {
            return;
        }

        int remainingEnemies = GetRemainingEnemyCount();

        if (remainingEnemies > 0)
        {
            Debug.Log(
                "[Rowboat] DEFEAT ALL ENEMIES " + remainingEnemies +" REMAINING.", this );

            return;
        }

        // Either player may set sail. The host loads the scene directly; a
        // client cannot start a networked scene load itself, so it asks the
        // server through its own player object.
        if (
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsServer
        )
        {
            TrySetSail();
            return;
        }

        sailRequestSent = true;
        sailRequestRetryTime =
            Time.unscaledTime + SailRequestRetrySeconds;

        localPlayerInRange.RequestSetSail();
    }

    /// <summary>
    /// Server-only entry point for a client's interact press, routed here by
    /// <see cref="TopDownNetworkPlayer2D.RequestSetSail"/>. Re-validates
    /// everything the client checked locally, because none of those checks
    /// ran anywhere the server can vouch for.
    /// </summary>
    public void RequestSetSailServer(Vector3 requesterPosition)
    {
        if (
            NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsServer
        )
        {
            return;
        }

        if (sceneLoadRequested)
        {
            return;
        }

        if (!IsWithinServerRange(requesterPosition))
        {
            Debug.Log(
                "[Rowboat] Ignoring a set-sail request from a player the " +
                "server does not place at the rowboat.",
                this
            );

            return;
        }

        int remainingEnemies = GetRemainingEnemyCount();

        if (remainingEnemies > 0)
        {
            Debug.Log(
                "[Rowboat] Ignoring a set-sail request: " +
                remainingEnemies + " enemies remain.",
                this
            );

            return;
        }

        TrySetSail();
    }

    /// <summary>
    /// Measured against the trigger's own bounds rather than an arbitrary
    /// radius, so the server's idea of "at the rowboat" follows whatever
    /// shape the designer gave the trigger.
    /// </summary>
    private bool IsWithinServerRange(Vector3 position)
    {
        if (triggerCollider == null)
        {
            return false;
        }

        Bounds area = triggerCollider.bounds;

        area.Expand(
            new Vector3(serverRangeMargin * 2f, serverRangeMargin * 2f, 0f)
        );

        // 2D comparison: the trigger's bounds are flat in z and players sit
        // at whatever z their sorting needs, so a 3D Contains would reject
        // everyone.
        return
            position.x >= area.min.x &&
            position.x <= area.max.x &&
            position.y >= area.min.y &&
            position.y <= area.max.y;
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
    private int GetRemainingEnemyCount()
    {
        Enemy[] enemies =
            FindObjectsByType<Enemy>(
                FindObjectsSortMode.None
            );

        int remainingEnemies = 0;

        foreach (Enemy enemy in enemies)
        {
            if (enemy.IsAlive)
            {
                remainingEnemies++;
            }
        }

        return remainingEnemies;
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

        DeadmansTales.Networking.NetworkRunState runState =
            DeadmansTales.Networking.NetworkRunState.Instance;

        if (runState != null && runState.IsSpawned)
        {
            runState.SetStatusServer(
                DeadmansTales.Networking.NetworkRunStatus.Loading
            );
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
            if (runState != null && runState.IsSpawned)
            {
                runState.SetStatusServer(
                    DeadmansTales.Networking.NetworkRunStatus.Playing
                );
            }

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

        // Identical on the host and on a client, because setting sail now is
        // too: the enemy count comes off Enemy.CurrentHealth, a
        // NetworkVariable every peer can read, so a client reaches the same
        // verdict the server will.
        if (sceneLoadRequested || sailRequestSent)
        {
            message = "Setting Sail...";
        }
        else
        {
            int remainingEnemies = GetRemainingEnemyCount();

            if (remainingEnemies > 0)
            {
                message =
                    "Defeat all enemies before setting sail (" + remainingEnemies +  " remaining)";
            }
            else
            {
                message = "Press E to Set Sail";
            }
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
