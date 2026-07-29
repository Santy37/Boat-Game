using System.Collections;
using DeadmansTales.Networking;
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

        yield return InitializeRunForLobby();

        networkManager.SceneManager.LoadScene(
            multiplayerLobbyScene, LoadSceneMode.Single);
    }

    /// <summary>
    /// Starts the shared run before the lobby loads.
    ///
    /// Without this the multiplayer route was a dead end: MainMenuManager is
    /// the only other caller of InitializeNewRunServer, so coming through the
    /// lobby left NetworkRunState uninitialized -- MasterSeed 0, stage 0. That
    /// makes StageSeedProvider bail (it requires StageIndex >= 1), so
    /// SeededIslandContentGenerator never completes, so the first island's
    /// portal -- which requires generation complete -- never opens. Nothing
    /// spawned and the crew could not leave.
    ///
    /// Stage 1 because the lobby is only an MP staging area; everyone plays
    /// level one, and the lobby sits before it rather than replacing it.
    /// </summary>
    private IEnumerator InitializeRunForLobby()
    {
        float deadline =
            Time.realtimeSinceStartup + Mathf.Max(0.1f, networkStartTimeout);

        // DeadmansNetworkBootstrap spawns NetworkRunState once the host is
        // listening, so it is not available the instant StartHost returns.
        while (
            (NetworkRunState.Instance == null ||
             !NetworkRunState.Instance.IsSpawned) &&
            Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        NetworkRunState runState = NetworkRunState.Instance;

        if (runState == null || !runState.IsSpawned)
        {
            Debug.LogError(
                "[Menu] NetworkRunState never spawned, so the run could not " +
                "be initialized. The islands will generate no content.",
                this);
            yield break;
        }

        if (!runState.IsServer)
        {
            // Only the host seeds the run; clients receive it replicated.
            yield break;
        }

        // Seed 0 means "unset" to NetworkRunState, so never hand it one.
        int seed = Random.Range(1, int.MaxValue);

        runState.InitializeNewRunServer(seed, "boat_default", 1, 1);

        Debug.Log(
            $"[Menu] Multiplayer run seeded {seed} at stage 1. " +
            "Lobby -> level one -> boat leg.",
            this);
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
