using System;
using System.Collections;
using System.Reflection;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Connects the existing MainMenu Ready and Start Game buttons to the
/// synchronized ready state stored on each network player.
///
/// The host is always ready and may start alone. Once clients are connected,
/// every non-host client must be ready before the host can start the game.
/// </summary>
public sealed class OnlineLobbyReadyCoordinator : MonoBehaviour
{
    private static readonly MethodInfo SetStatusMethod =
        typeof(MainMenuManager).GetMethod(
            "SetStatus",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

    private MainMenuManager mainMenu;
    private Button readyButton;
    private Button startGameButton;

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.AfterSceneLoad
    )]
    private static void InstallForLoadedMenu()
    {
        MainMenuManager menu =
            Object.FindFirstObjectByType<MainMenuManager>(
                FindObjectsInactive.Include
            );

        if (
            menu == null ||
            menu.GetComponent<OnlineLobbyReadyCoordinator>() != null
        )
        {
            return;
        }

        menu.gameObject.AddComponent<OnlineLobbyReadyCoordinator>();
    }

    private IEnumerator Start()
    {
        mainMenu = GetComponent<MainMenuManager>();

        // MainMenuManager and OnlineLobbyMenuRecovery both wire buttons during
        // startup. Two frames makes this coordinator the final owner of Ready
        // and Start Game without changing the scene YAML.
        yield return null;
        yield return null;

        WireButtons();
        RefreshReadyButton();
    }

    private void Update()
    {
        if (readyButton == null || startGameButton == null)
        {
            WireButtons();
        }

        RefreshReadyButton();
    }

    private void WireButtons()
    {
        Button[] buttons = Object.FindObjectsByType<Button>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (Button button in buttons)
        {
            TMP_Text label =
                button.GetComponentInChildren<TMP_Text>(true);

            if (label == null)
            {
                continue;
            }

            string normalized = NormalizeLabel(label.text);

            if (normalized == "READY" || normalized == "UNREADY")
            {
                readyButton = button;
                ReplaceAction(button, HandleReadyClicked);
            }
            else if (normalized == "START GAME")
            {
                startGameButton = button;
                ReplaceAction(button, HandleStartClicked);
            }
        }
    }

    private void HandleReadyClicked()
    {
        TopDownNetworkPlayer2D localPlayer = GetLocalPlayer();

        if (localPlayer == null)
        {
            SetStatus("WAITING FOR YOUR NETWORK PLAYER TO SPAWN");
            return;
        }

        bool nextReadyState = !localPlayer.IsLobbyReady;
        localPlayer.RequestLobbyReady(nextReadyState);

        SetStatus(
            nextReadyState
                ? "READY STATUS UPDATING..."
                : "YOU ARE NOT READY"
        );
    }

    private void HandleStartClicked()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (
            networkManager == null ||
            !networkManager.IsListening ||
            !networkManager.IsServer
        )
        {
            SetStatus("ONLY THE HOST CAN START THE GAME");
            return;
        }

        int unreadyClients = 0;
        int playersStillSpawning = 0;

        foreach (
            NetworkClient client in networkManager.ConnectedClientsList
        )
        {
            NetworkObject playerObject = client.PlayerObject;

            if (playerObject == null)
            {
                playersStillSpawning++;
                continue;
            }

            // The host is always ready and may start a one-player lobby.
            if (
                playerObject.OwnerClientId ==
                NetworkManager.ServerClientId
            )
            {
                continue;
            }

            TopDownNetworkPlayer2D player =
                playerObject.GetComponent<TopDownNetworkPlayer2D>();

            if (player == null)
            {
                playersStillSpawning++;
                continue;
            }

            if (!player.IsLobbyReady)
            {
                unreadyClients++;
            }
        }

        if (playersStillSpawning > 0)
        {
            SetStatus("WAITING FOR ALL PLAYERS TO FINISH SPAWNING");
            return;
        }

        if (unreadyClients > 0)
        {
            string playerWord =
                unreadyClients == 1 ? "PLAYER" : "PLAYERS";

            SetStatus(
                $"WAITING FOR {unreadyClients} {playerWord} TO READY UP"
            );
            return;
        }

        mainMenu?.StartMultiplayerGame();
    }

    private void RefreshReadyButton()
    {
        if (readyButton == null)
        {
            return;
        }

        TopDownNetworkPlayer2D localPlayer = GetLocalPlayer();
        bool hasPlayer = localPlayer != null;
        bool isReady = hasPlayer && localPlayer.IsLobbyReady;

        readyButton.interactable = hasPlayer;

        TMP_Text label =
            readyButton.GetComponentInChildren<TMP_Text>(true);

        if (label != null)
        {
            label.text = isReady ? "UNREADY" : "READY";
        }
    }

    private static TopDownNetworkPlayer2D GetLocalPlayer()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (
            networkManager == null ||
            !networkManager.IsListening ||
            networkManager.LocalClient == null ||
            networkManager.LocalClient.PlayerObject == null
        )
        {
            return null;
        }

        return networkManager.LocalClient.PlayerObject
            .GetComponent<TopDownNetworkPlayer2D>();
    }

    private void SetStatus(string message)
    {
        if (mainMenu != null && SetStatusMethod != null)
        {
            SetStatusMethod.Invoke(
                mainMenu,
                new object[] { message }
            );
            return;
        }

        Debug.Log($"[Lobby Ready] {message}");
    }

    private static void ReplaceAction(
        Button button,
        UnityEngine.Events.UnityAction action
    )
    {
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(action);
    }

    private static string NormalizeLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim()
            .ToUpperInvariant();
    }
}
