using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Main-menu entry point. Hook the two buttons here:
///
///   Start Game   -> StartLocalRun()        (3-player couch co-op, no network)
///   Multiplayer  -> StartMultiplayerRun()  (starts a host, networked lobby)
///
/// This is the only place the mode is decided. Everything else just reads
/// <see cref="GameMode"/>.
/// </summary>
public class GameModeMenu : MonoBehaviour
{
    [Header("Local")]
    [Tooltip("Leave empty to find the LocalRunManager automatically.")]
    [SerializeField] private LocalRunManager localRunManager;

    [Header("Multiplayer")]
    [Tooltip("The NETWORKED lobby. The local lobby is set on LocalRunManager.")]
    [SerializeField] private string multiplayerLobbyScene = "Lobby_Island_2D";
    [SerializeField] private float networkStartTimeout = 5f;

    /// <summary>Hook this to the Start Game button.</summary>
    public void StartLocalRun()
    {
        GameMode.SetLocal();

        // Make sure no host is left running from a previous multiplayer try.
        ShutDownNetworkIfRunning();

        if (localRunManager == null)
        {
            localRunManager = FindFirstObjectByType<LocalRunManager>();
        }

        if (localRunManager == null)
        {
            Debug.LogError(
                "[Menu] No LocalRunManager in the scene. Add one to the " +
                "RunManager object in StartScene.",
                this);
            return;
        }

        localRunManager.StartRun();
    }

    /// <summary>
    /// Hook this to a "Random Run" button: same as Start Game, but rolls a
    /// fresh seed so the map, islands and layout differ every time.
    /// </summary>
    public void StartLocalRunRandom()
    {
        GameMode.SetLocal();
        ShutDownNetworkIfRunning();

        if (localRunManager == null)
        {
            localRunManager = FindFirstObjectByType<LocalRunManager>();
        }

        if (localRunManager == null)
        {
            Debug.LogError(
                "[Menu] No LocalRunManager in the scene. Add one to the " +
                "RunManager object in StartScene.",
                this);
            return;
        }

        localRunManager.StartRandomRun();
    }

    /// <summary>Hook this to the Multiplayer button.</summary>
    public void StartMultiplayerRun()
    {
        GameMode.SetMultiplayer();
        StartCoroutine(StartHostThenLobby());
    }

    private IEnumerator StartHostThenLobby()
    {
        // The NetworkManager is built by DeadmansNetworkBootstrap on load.
        float deadline = Time.realtimeSinceStartup + Mathf.Max(0.1f, networkStartTimeout);

        while (NetworkManager.Singleton == null &&
               Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager == null)
        {
            Debug.LogError("[Menu] No NetworkManager appeared.", this);
            yield break;
        }

        if (!networkManager.IsListening && !networkManager.StartHost())
        {
            Debug.LogError(
                "[Menu] StartHost failed. If the Console mentions port 7777 " +
                "being in use, a previous host is still holding it.",
                this);
            yield break;
        }

        networkManager.SceneManager.LoadScene(
            multiplayerLobbyScene, LoadSceneMode.Single);
    }

    private void ShutDownNetworkIfRunning()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager != null && networkManager.IsListening)
        {
            Debug.Log("[Menu] Shutting down the running host for local play.");
            networkManager.Shutdown();
        }
    }
}
