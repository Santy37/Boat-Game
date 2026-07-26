using System;
using DeadmansTales.Networking;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuManager : MonoBehaviour
{
    private const int LobbyMaxPlayers = 4;

    private static readonly string[] SelectableSceneNames =
    {
    "Lobby_Island_2D",
    "Island_After_Ocean_01_2D"
};

    private static readonly string[] SelectableLevelDisplayNames =
    {
    "LOBBY ISLAND",
    "OCEAN ISLAND"
};

    private static readonly int[] SelectableStartingStages =
    {
    1,
    2
};
    /// <summary>
    /// Stage index each level starts a fresh run at.
    ///
    /// This is not cosmetic: the post-ocean island's seeded content markers
    /// are gated to minimumStage 2 (they represent the island you reach
    /// AFTER the first voyage), so launching that scene directly at stage 1
    /// would deterministically spawn nothing — no enemies, no loot, no
    /// camp chests. Entering at stage 2 gives the same island the normal
    /// boat -> island transition produces for a given seed.
    /// </summary>


    [Header("Menu Panels")]
    public GameObject mainMenuPanel;
    public GameObject levelSelectPanel;
    public GameObject multiplayerPanel;
    public GameObject connectionOptions;
    public GameObject clientOptions;
    public GameObject hostOptions;
    public GameObject joinCodeOptions;

    [Header("Multiplayer UI")]
    public TMP_InputField lobbyCodeInput;
    public TMP_Text lobbyCodeText;
    public TMP_Text createOrJoinText;
    public TMP_Text playerListText;
    public TMP_Text enterCodeText;

    [SerializeField]
    private TMP_Text multiplayerStatusText;

    [Header("Run Seed")]
    [Tooltip(
        "Reuse one seed for every run instead of rolling a random one. " +
        "The same seed plus the same level always rebuilds the same " +
        "island layout, spawns, and loot, which is what makes a run " +
        "shareable and a bug reproducible."
    )]
    [SerializeField]
    private bool useFixedRunSeed;

    [SerializeField]
    private int fixedRunSeed = 12345;

    [Tooltip(
        "Optional. Wire a menu input field here to let players type a " +
        "seed. A valid number typed here overrides the settings above."
    )]
    [SerializeField]
    private TMP_InputField runSeedInput;

    /// <summary>
    /// Seed of the most recently started run, so the value that generated
    /// the current world can be read back (and re-entered) after the fact.
    /// </summary>
    public static int LastRunSeed
    {
        get;
        private set;
    }

    private OnlineLobbyService lobbyService;
    private bool lobbyOperationInProgress;
    private int selectedSceneIndex;
    private string statusMessage = string.Empty;
    private string defaultCreateOrJoinText = "CREATE OR JOIN";
    private string defaultEnterCodeText = "ENTER CODE";

    private Button selectLevelButton;
    private Button startGameButton;
    private Button readyButton;

    private RectTransform lobbyCodeRect;
    private Vector2 lobbyCodeDefaultAnchorMin;
    private Vector2 lobbyCodeDefaultAnchorMax;
    private Vector2 lobbyCodeDefaultPivot;
    private Vector2 lobbyCodeDefaultPosition;
    private Vector2 lobbyCodeDefaultSize;
    private TextAlignmentOptions lobbyCodeDefaultAlignment;
    private bool lobbyCodeLayoutCached;

    private string lastSessionCode = string.Empty;
    private int lastPlayerCount = -1;
    private bool lastIsHost;
    private bool lastInSession;
    private bool lastBusy;
    private LobbyConnectionState lastConnectionState =
        (LobbyConnectionState)(-1);

    private void Awake()
    {
        if (createOrJoinText != null)
        {
            defaultCreateOrJoinText = createOrJoinText.text;
        }

        if (enterCodeText != null)
        {
            defaultEnterCodeText = enterCodeText.text;
        }

        CacheLobbyCodeLayout();
        EnsureLobbyService();

        if (lobbyCodeInput != null)
        {
            lobbyCodeInput.onValueChanged.AddListener(
                HandleJoinCodeInputChanged
            );
        }

        WireKnownButtons();
        UpdateLevelButtonLabel();
        UpdateReadyButtonLabel();
    }

    private async void Start()
    {
        if (PlayerPrefs.GetInt("OpenLevelSelectAfterDeath", 0) == 1)
        {
            PlayerPrefs.DeleteKey("OpenLevelSelectAfterDeath");
            ShowLevelSelectMenu();
        }
        else
        {
            ShowMainMenu();
        }

        if (lobbyService == null)
        {
            SetStatus("ONLINE LOBBY SERVICE IS UNAVAILABLE");
            return;
        }

        SetStatus("CONNECTING TO ONLINE MULTIPLAYER...");
        bool ready = await lobbyService.EnsureServicesReadyAsync();

        SetStatus(
            ready
                ? "ONLINE MULTIPLAYER READY"
                : "COULD NOT CONNECT TO ONLINE MULTIPLAYER"
        );

        RefreshLobbyUi();
    }

    private void Update()
    {
        EnsureLobbyService();

        if (LobbySnapshotChanged())
        {
            RefreshLobbyUi();
        }
    }

    private void OnDestroy()
    {
        if (lobbyCodeInput != null)
        {
            lobbyCodeInput.onValueChanged.RemoveListener(
                HandleJoinCodeInputChanged
            );
        }

        if (lobbyService != null)
        {
            lobbyService.SessionChanged -= HandleSessionChanged;
        }
    }

    public void ShowMainMenu()
    {
        SetActive(mainMenuPanel, true);
        SetActive(levelSelectPanel, false);
        SetActive(multiplayerPanel, false);
        ApplyLobbyCodeLayout(false);
    }

    public void ShowLevelSelectMenu()
    {
        if (lobbyService != null && lobbyService.IsInSession)
        {
            CycleSelectedLevel();
            return;
        }

        SetActive(mainMenuPanel, false);
        SetActive(levelSelectPanel, true);
        SetActive(multiplayerPanel, false);
    }

    public void ShowMultiplayerMenu()
    {
        SetActive(mainMenuPanel, false);
        SetActive(levelSelectPanel, false);
        SetActive(multiplayerPanel, true);

        if (lobbyService != null && lobbyService.IsInSession)
        {
            ShowLobbyPanelForCurrentRole();
        }
        else
        {
            ShowConnectionOptions();
        }
    }

    public void ShowConnectionOptions()
    {
        SetActive(connectionOptions, true);
        SetActive(joinCodeOptions, false);
        SetActive(clientOptions, false);
        SetActive(hostOptions, false);

        SetActive(createOrJoinText, true);
        SetActive(playerListText, false);
        SetActive(enterCodeText, false);
        SetActive(lobbyCodeText, true);

        ApplyLobbyCodeLayout(false);
        UpdateReadyButtonLabel();

        if (lobbyCodeText != null)
        {
            lobbyCodeText.text = "";
        }

        RefreshLobbyUi();
    }

    public void ShowCreatedLobby()
    {
        BeginCreateOnlineLobby();
    }

    public void ShowJoinCodeOptions()
    {
        SetActive(connectionOptions, false);
        SetActive(joinCodeOptions, true);
        SetActive(clientOptions, false);
        SetActive(hostOptions, false);

        SetActive(createOrJoinText, false);
        SetActive(playerListText, false);
        SetActive(enterCodeText, true);
        SetActive(lobbyCodeText, true);

        ApplyLobbyCodeLayout(false);

        if (lobbyCodeText != null)
        {
            lobbyCodeText.text = "";
        }

        HandleJoinCodeInputChanged(
            lobbyCodeInput != null ? lobbyCodeInput.text : string.Empty
        );

        RefreshLobbyUi();
    }

    public void PreviewHostLobby()
    {
        BeginCreateOnlineLobby();
    }

    public void PreviewClientLobby()
    {
        BeginJoinOnlineLobby();
    }

    public void CreateOnlineLobby()
    {
        BeginCreateOnlineLobby();
    }

    public void JoinOnlineLobby()
    {
        BeginJoinOnlineLobby();
    }

    public async void LeaveLobby()
    {
        if (lobbyOperationInProgress)
        {
            return;
        }

        if (lobbyService == null || !lobbyService.IsInSession)
        {
            SetStatus("NOT CURRENTLY IN A LOBBY");
            ShowConnectionOptions();
            return;
        }

        lobbyOperationInProgress = true;
        SetStatus("LEAVING LOBBY...");

        try
        {
            bool left = await lobbyService.LeaveLobbyAsync();

            if (left)
            {
                SetStatus("LEFT THE LOBBY");
                ShowConnectionOptions();
            }
            else
            {
                SetStatus("COULD NOT LEAVE THE LOBBY");
            }
        }
        finally
        {
            lobbyOperationInProgress = false;
            RefreshLobbyUi();
        }
    }

    public void CopyLobbyCode()
    {
        CopySessionCode();
    }

    public void CopySessionCode()
    {
        if (lobbyService == null)
        {
            SetStatus("NO LOBBY CODE IS AVAILABLE");
            return;
        }

        lobbyService.CopySessionCode();

        SetStatus(
            string.IsNullOrWhiteSpace(lobbyService.SessionCode)
                ? "NO LOBBY CODE IS AVAILABLE"
                : "LOBBY CODE COPIED"
        );
    }

    public void ExitGame()
    {
        Debug.Log("Exit");
        Application.Quit();
    }

    public void OpenSettings()
    {
        Debug.Log("Settings");
    }

    public void SelectLevel()
    {
        CycleSelectedLevel();
    }

    public void CycleSelectedLevel()
    {
        if (lobbyService == null || !lobbyService.IsInSession)
        {
            ShowLevelSelectMenu();
            return;
        }

        if (!lobbyService.IsHost)
        {
            SetStatus("ONLY THE HOST CAN SELECT THE LEVEL");
            return;
        }

        selectedSceneIndex =
            (selectedSceneIndex + 1) % SelectableSceneNames.Length;

        UpdateLevelButtonLabel();
        SetStatus($"SELECTED LEVEL: {GetSelectedLevelDisplayName()}");
    }

    public void ToggleReady()
    {
        if (lobbyService == null || !lobbyService.IsInSession)
        {
            SetStatus("JOIN A LOBBY FIRST");
            return;
        }

        TopDownNetworkPlayer2D localPlayer = GetLocalNetworkPlayer();

        if (localPlayer == null)
        {
            SetStatus("WAITING FOR YOUR NETWORK PLAYER TO SPAWN");
            return;
        }

        bool nextReadyState = !localPlayer.IsLobbyReady;
        localPlayer.RequestLobbyReady(nextReadyState);
        UpdateReadyButtonLabel();
        SetStatus(
            nextReadyState
                ? "YOU ARE READY"
                : "YOU ARE NOT READY"
        );
    }

    public void Ready()
    {
        ToggleReady();
    }

    public void StartGame()
    {
        StartMultiplayerGame();
    }

    public void LoadLevelOne()
    {
        LoadSelectableLevel(0);
    }

    public void LoadLevelTwo()
    {
        LoadSelectableLevel(1);
    }

    public void LoadLevelThree()
    {
        ShowLevelComingSoon();
    }

    public void LoadLevelFour()
    {
        ShowLevelComingSoon();
    }

    /// <summary>
    /// Menu buttons for levels that have no scene yet. Says so instead of
    /// looking broken.
    /// </summary>
    public void ShowLevelComingSoon()
    {
        SetStatus("THAT LEVEL IS NOT BUILT YET");
    }

    /// <summary>
    /// Starts the chosen level, in the lobby session when there is one and
    /// as a solo host otherwise. Selecting the level first means the run is
    /// always initialized with that level's starting stage.
    /// </summary>
    private void LoadSelectableLevel(int levelIndex)
    {
        selectedSceneIndex = Mathf.Clamp(
            levelIndex,
            0,
            SelectableSceneNames.Length - 1
        );

        UpdateLevelButtonLabel();

        if (lobbyService != null && lobbyService.IsInSession)
        {
            StartMultiplayerGame();
            return;
        }

        StartSinglePlayerGame();
    }

    public void StartMultiplayerGame()
    {
        if (lobbyService == null || !lobbyService.IsInSession)
        {
            SetStatus("CREATE OR JOIN A LOBBY FIRST");
            return;
        }

        if (!lobbyService.IsHost)
        {
            SetStatus("ONLY THE HOST CAN START THE GAME");
            return;
        }

        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager == null)
        {
            SetStatus("NETWORK MANAGER WAS NOT FOUND");
            Debug.LogError("NetworkManager was not found.");
            return;
        }

        if (!networkManager.IsListening || !networkManager.IsServer)
        {
            SetStatus("ONLINE NETWORK SESSION IS NOT READY YET");
            return;
        }

        if (!CanHostStartGame(networkManager, out string readinessMessage))
        {
            SetStatus(readinessMessage);
            return;
        }

        string selectedScene = GetSelectedSceneName();

        if (!Application.CanStreamedLevelBeLoaded(selectedScene))
        {
            SetStatus($"SCENE IS NOT IN BUILD SETTINGS: {selectedScene}");
            Debug.LogError(
                $"[Main Menu] Scene '{selectedScene}' is not loadable."
            );
            return;
        }

        SetStatus($"STARTING {GetSelectedLevelDisplayName()}...");

        if (!TryInitializeNetworkRun(networkManager))
        {
            return;
        }

        try
        {
            networkManager.SceneManager.LoadScene(
                selectedScene,
                LoadSceneMode.Single
            );
        }
        catch (Exception exception)
        {
            SetStatus("COULD NOT START THE GAME");
            Debug.LogException(exception);
        }
    }

    private async void BeginCreateOnlineLobby()
    {
        EnsureLobbyService();

        if (lobbyOperationInProgress)
        {
            return;
        }

        if (lobbyService == null)
        {
            SetStatus("ONLINE LOBBY SERVICE IS UNAVAILABLE");
            return;
        }

        if (lobbyService.IsInSession)
        {
            ShowLobbyPanelForCurrentRole();
            RefreshLobbyUi();
            return;
        }

        lobbyOperationInProgress = true;
        SetStatus("CREATING 4-PLAYER ONLINE LOBBY...");

        try
        {
            bool created = await lobbyService.CreateLobbyAsync();

            if (!created)
            {
                SetStatus("COULD NOT CREATE THE ONLINE LOBBY");
                return;
            }

            selectedSceneIndex = 0;
            UpdateReadyButtonLabel();
            UpdateLevelButtonLabel();
            SetStatus("LOBBY CREATED - SHARE THE CODE");
            ShowLobbyPanelForCurrentRole();
        }
        finally
        {
            lobbyOperationInProgress = false;
            RefreshLobbyUi();
        }
    }

    private async void BeginJoinOnlineLobby()
    {
        EnsureLobbyService();

        if (lobbyOperationInProgress)
        {
            return;
        }

        if (lobbyService == null)
        {
            SetStatus("ONLINE LOBBY SERVICE IS UNAVAILABLE");
            return;
        }

        if (lobbyService.IsInSession)
        {
            ShowLobbyPanelForCurrentRole();
            RefreshLobbyUi();
            return;
        }

        string code = lobbyCodeInput != null
            ? lobbyCodeInput.text
            : string.Empty;

        lobbyService.SetJoinCode(code);

        if (!lobbyService.IsPendingJoinCodeValid)
        {
            SetStatus("ENTER A VALID 6-8 CHARACTER LOBBY CODE");
            return;
        }

        lobbyOperationInProgress = true;
        SetStatus("JOINING ONLINE LOBBY...");

        try
        {
            bool joined = await lobbyService.JoinLobbyAsync(code);

            if (!joined)
            {
                SetStatus("COULD NOT JOIN THAT ONLINE LOBBY");
                return;
            }

            selectedSceneIndex = 0;
            UpdateReadyButtonLabel();
            UpdateLevelButtonLabel();
            SetStatus("CONNECTED TO LOBBY");
            ShowLobbyPanelForCurrentRole();
        }
        finally
        {
            lobbyOperationInProgress = false;
            RefreshLobbyUi();
        }
    }

    private void StartSinglePlayerGame()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        Debug.Log("LEVEL BUTTON CLICKED");

        if (networkManager == null)
        {
            SetStatus("NETWORK MANAGER WAS NOT FOUND");
            Debug.LogError("NetworkManager was not found.");
            return;
        }

        if (!networkManager.IsClient && !networkManager.IsServer)
        {
            if (!networkManager.StartHost())
            {
                SetStatus("SINGLE-PLAYER HOST FAILED TO START");
                Debug.LogError("Single-player host failed to start.");
                return;
            }
        }

        string selectedScene = GetSelectedSceneName();

        if (!TryInitializeNetworkRun(networkManager))
        {
            return;
        }

        networkManager.SceneManager.LoadScene(
            selectedScene,
            LoadSceneMode.Single
        );
    }

    private bool TryInitializeNetworkRun(NetworkManager networkManager)
    {
        if (networkManager == null || !networkManager.IsServer)
        {
            SetStatus("ONLY THE SERVER CAN INITIALIZE THE RUN");
            return false;
        }

        NetworkRunState runState = NetworkRunState.Instance;

        if (runState == null || !runState.IsSpawned)
        {
            SetStatus("WAITING FOR THE NETWORK RUN STATE");
            Debug.LogError(
                "[Main Menu] NetworkRunState was not spawned before the " +
                "gameplay scene load."
            );
            return false;
        }

        int runSeed = ResolveRunSeed();
        int startingStage = GetSelectedStartingStage();

        runState.InitializeNewRunServer(
            runSeed,
            "boat_default",
            1,
            startingStage
        );

        LastRunSeed = runSeed;

        Debug.Log(
            $"[Main Menu] Run seed {runSeed} | stage {startingStage} | " +
            $"scene {GetSelectedSceneName()}. Re-enter this seed with the " +
            "same level to rebuild an identical world."
        );

        return true;
    }

    /// <summary>
    /// A typed seed wins, then the inspector's fixed seed, then a fresh
    /// random one. Seed 0 is never used: the run state treats it as unset.
    /// </summary>
    private int ResolveRunSeed()
    {
        if (
            runSeedInput != null &&
            int.TryParse(runSeedInput.text, out int typedSeed) &&
            typedSeed != 0
        )
        {
            return typedSeed;
        }

        if (useFixedRunSeed && fixedRunSeed != 0)
        {
            return fixedRunSeed;
        }

        return UnityEngine.Random.Range(1, int.MaxValue);
    }

    private int GetSelectedStartingStage()
    {
        int index = Mathf.Clamp(
            selectedSceneIndex,
            0,
            SelectableStartingStages.Length - 1
        );

        return SelectableStartingStages[index];
    }

    private void EnsureLobbyService()
    {
        OnlineLobbyService foundService = OnlineLobbyService.Instance;

        if (foundService == null)
        {
            foundService = FindFirstObjectByType<OnlineLobbyService>();
        }

        if (foundService == null)
        {
            GameObject serviceObject = new GameObject(
                "[DMT] OnlineLobbyService"
            );

            foundService = serviceObject.AddComponent<OnlineLobbyService>();
        }

        if (lobbyService == foundService)
        {
            return;
        }

        if (lobbyService != null)
        {
            lobbyService.SessionChanged -= HandleSessionChanged;
        }

        lobbyService = foundService;
        lobbyService.SessionChanged += HandleSessionChanged;
        CacheLobbySnapshot();
    }

    private void HandleSessionChanged()
    {
        CacheLobbySnapshot();

        if (
            multiplayerPanel != null &&
            multiplayerPanel.activeInHierarchy &&
            lobbyService != null &&
            lobbyService.IsInSession
        )
        {
            ShowLobbyPanelForCurrentRole();
        }

        RefreshLobbyUi();
    }

    private void HandleJoinCodeInputChanged(string rawCode)
    {
        if (lobbyService == null)
        {
            return;
        }

        lobbyService.SetJoinCode(rawCode);

        if (
            joinCodeOptions != null &&
            joinCodeOptions.activeInHierarchy &&
            !string.IsNullOrWhiteSpace(rawCode)
        )
        {
            SetStatus(
                lobbyService.IsPendingJoinCodeValid
                    ? "CODE FORMAT LOOKS GOOD"
                    : "ENTER A VALID 6-8 CHARACTER CODE"
            );
        }
    }

    private void ShowLobbyPanelForCurrentRole()
    {
        if (lobbyService == null || !lobbyService.IsInSession)
        {
            ShowConnectionOptions();
            return;
        }

        SetActive(connectionOptions, false);
        SetActive(joinCodeOptions, false);
        SetActive(hostOptions, lobbyService.IsHost);
        SetActive(clientOptions, !lobbyService.IsHost);

        SetActive(createOrJoinText, false);
        SetActive(playerListText, true);
        SetActive(enterCodeText, false);
        SetActive(lobbyCodeText, true);

        ApplyLobbyCodeLayout(true);
        UpdateLevelButtonLabel();
        UpdateReadyButtonLabel();
    }

    private void RefreshLobbyUi()
    {
        CacheLobbySnapshot();

        if (lobbyService != null && lobbyService.IsInSession)
        {
            string code = lobbyService.SessionCode;

            if (lobbyCodeText != null)
            {
                lobbyCodeText.text = string.IsNullOrWhiteSpace(code)
                    ? "LOBBY"
                    : $"LOBBY CODE: {code}";
            }

            if (playerListText != null)
            {
                string role = lobbyService.IsHost ? "HOST" : "CLIENT";
                TopDownNetworkPlayer2D localPlayer =
                    GetLocalNetworkPlayer();
                string readyState = lobbyService.IsHost
                    ? string.Empty
                    : localPlayer != null && localPlayer.IsLobbyReady
                        ? " - READY"
                        : " - NOT READY";

                playerListText.text =
                    $"{role}{readyState} - PLAYERS " +
                    $"{lobbyService.PlayerCount}/{LobbyMaxPlayers}\n" +
                    statusMessage;
            }

            ApplyLobbyCodeLayout(true);
        }
        else
        {
            if (lobbyCodeText != null)
            {
                lobbyCodeText.text = "";
            }

            if (playerListText != null && playerListText.gameObject.activeSelf)
            {
                playerListText.text = statusMessage;
            }

            ApplyLobbyCodeLayout(false);
        }

        if (multiplayerStatusText != null)
        {
            multiplayerStatusText.text = statusMessage;
        }

        if (
            connectionOptions != null &&
            connectionOptions.activeInHierarchy &&
            createOrJoinText != null
        )
        {
            createOrJoinText.text = string.IsNullOrWhiteSpace(statusMessage)
                ? defaultCreateOrJoinText
                : statusMessage;
        }

        if (
            joinCodeOptions != null &&
            joinCodeOptions.activeInHierarchy &&
            enterCodeText != null
        )
        {
            enterCodeText.text = string.IsNullOrWhiteSpace(statusMessage)
                ? defaultEnterCodeText
                : statusMessage;
        }
    }

    private void WireKnownButtons()
    {
        Button[] buttons = FindObjectsByType<Button>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (Button button in buttons)
        {
            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);

            if (label == null)
            {
                continue;
            }

            string buttonName = NormalizeButtonLabel(label.text);

            switch (buttonName)
            {
                case "CREATE LOBBY":
                    ReplaceButtonAction(button, CreateOnlineLobby);
                    break;

                case "JOIN LOBBY":
                    if (
                        connectionOptions != null &&
                        button.transform.IsChildOf(connectionOptions.transform)
                    )
                    {
                        ReplaceButtonAction(button, ShowJoinCodeOptions);
                    }
                    else
                    {
                        ReplaceButtonAction(button, JoinOnlineLobby);
                    }
                    break;

                case "SELECT LEVEL":
                case "LEVEL LOBBY ISLAND":
                case "LEVEL BOAT GAMEPLAY":
                case "LEVEL THE SHIP":
                case "LEVEL OCEAN ISLAND":
                case "LEVEL PORT MARKET":
                    selectLevelButton = button;
                    ReplaceButtonAction(button, CycleSelectedLevel);
                    break;

                case "START GAME":
                    startGameButton = button;
                    ReplaceButtonAction(button, StartMultiplayerGame);
                    break;

                case "READY":
                case "UNREADY":
                    readyButton = button;
                    ReplaceButtonAction(button, ToggleReady);
                    break;

                case "LEAVE LOBBY":
                    ReplaceButtonAction(button, LeaveLobby);
                    break;

                // The menu art spells these out ("Level One"), so both the
                // written and numeric forms are accepted.
                case "LEVEL 1":
                case "LEVEL ONE":
                    ReplaceButtonAction(button, LoadLevelOne);
                    break;

                case "LEVEL 2":
                case "LEVEL TWO":
                    ReplaceButtonAction(button, LoadLevelTwo);
                    break;

                case "LEVEL 3":
                case "LEVEL THREE":
                    ReplaceButtonAction(button, LoadLevelThree);
                    break;

                case "LEVEL 4":
                case "LEVEL FOUR":
                    ReplaceButtonAction(button, LoadLevelFour);
                    break;

                case "LEVEL 5":
                case "LEVEL FIVE":
                    ReplaceButtonAction(button, ShowLevelComingSoon);
                    break;
            }
        }
    }

    private void UpdateLevelButtonLabel()
    {
        if (selectLevelButton == null)
        {
            return;
        }

        TMP_Text label =
            selectLevelButton.GetComponentInChildren<TMP_Text>(true);

        if (label != null)
        {
            label.text = $"LEVEL: {GetSelectedLevelDisplayName()}";
        }
    }

    private void UpdateReadyButtonLabel()
    {
        if (readyButton == null)
        {
            return;
        }

        TopDownNetworkPlayer2D localPlayer = GetLocalNetworkPlayer();
        bool canReady =
            lobbyService != null &&
            lobbyService.IsInSession &&
            localPlayer != null;

        readyButton.interactable = canReady;

        TMP_Text label = readyButton.GetComponentInChildren<TMP_Text>(true);

        if (label != null)
        {
            label.text =
                localPlayer != null && localPlayer.IsLobbyReady
                    ? "UNREADY"
                    : "READY";
        }
    }

    private static TopDownNetworkPlayer2D GetLocalNetworkPlayer()
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

    private static bool CanHostStartGame(
        NetworkManager networkManager,
        out string status
    )
    {
        int unreadyClients = 0;

        foreach (NetworkClient client in networkManager.ConnectedClientsList)
        {
            if (client.PlayerObject == null)
            {
                status = "WAITING FOR ALL PLAYERS TO FINISH SPAWNING";
                return false;
            }

            if (client.ClientId == NetworkManager.ServerClientId)
            {
                continue;
            }

            TopDownNetworkPlayer2D player =
                client.PlayerObject.GetComponent<TopDownNetworkPlayer2D>();

            if (player == null)
            {
                status = "A NETWORK PLAYER IS MISCONFIGURED";
                return false;
            }

            if (!player.IsLobbyReady)
            {
                unreadyClients++;
            }
        }

        if (unreadyClients > 0)
        {
            string playerWord = unreadyClients == 1 ? "PLAYER" : "PLAYERS";
            status =
                $"WAITING FOR {unreadyClients} {playerWord} TO READY UP";
            return false;
        }

        status = string.Empty;
        return true;
    }

    private string GetSelectedSceneName()
    {
        selectedSceneIndex = Mathf.Clamp(
            selectedSceneIndex,
            0,
            SelectableSceneNames.Length - 1
        );

        return SelectableSceneNames[selectedSceneIndex];
    }

    private string GetSelectedLevelDisplayName()
    {
        int index = Mathf.Clamp(
            selectedSceneIndex,
            0,
            SelectableLevelDisplayNames.Length - 1
        );

        return SelectableLevelDisplayNames[index];
    }

    private void CacheLobbyCodeLayout()
    {
        if (lobbyCodeText == null)
        {
            return;
        }

        lobbyCodeRect = lobbyCodeText.rectTransform;
        lobbyCodeDefaultAnchorMin = lobbyCodeRect.anchorMin;
        lobbyCodeDefaultAnchorMax = lobbyCodeRect.anchorMax;
        lobbyCodeDefaultPivot = lobbyCodeRect.pivot;
        lobbyCodeDefaultPosition = lobbyCodeRect.anchoredPosition;
        lobbyCodeDefaultSize = lobbyCodeRect.sizeDelta;
        lobbyCodeDefaultAlignment = lobbyCodeText.alignment;
        lobbyCodeLayoutCached = true;
    }

    private void ApplyLobbyCodeLayout(bool inLobby)
    {
        if (!lobbyCodeLayoutCached || lobbyCodeRect == null)
        {
            return;
        }

        if (inLobby)
        {
            lobbyCodeRect.anchorMin = Vector2.one;
            lobbyCodeRect.anchorMax = Vector2.one;
            lobbyCodeRect.pivot = Vector2.one;
            lobbyCodeRect.anchoredPosition = new Vector2(-32f, -24f);
            lobbyCodeRect.sizeDelta = new Vector2(600f, 60f);
            lobbyCodeText.alignment = TextAlignmentOptions.TopRight;
            lobbyCodeRect.SetAsLastSibling();
        }
        else
        {
            lobbyCodeRect.anchorMin = lobbyCodeDefaultAnchorMin;
            lobbyCodeRect.anchorMax = lobbyCodeDefaultAnchorMax;
            lobbyCodeRect.pivot = lobbyCodeDefaultPivot;
            lobbyCodeRect.anchoredPosition = lobbyCodeDefaultPosition;
            lobbyCodeRect.sizeDelta = lobbyCodeDefaultSize;
            lobbyCodeText.alignment = lobbyCodeDefaultAlignment;
        }
    }

    private void SetStatus(string message)
    {
        statusMessage = message ?? string.Empty;
        RefreshLobbyUi();
    }

    private bool LobbySnapshotChanged()
    {
        if (lobbyService == null)
        {
            return false;
        }

        return
            lastSessionCode != lobbyService.SessionCode ||
            lastPlayerCount != lobbyService.PlayerCount ||
            lastIsHost != lobbyService.IsHost ||
            lastInSession != lobbyService.IsInSession ||
            lastBusy != lobbyService.IsBusy ||
            lastConnectionState != lobbyService.ConnectionState;
    }

    private void CacheLobbySnapshot()
    {
        if (lobbyService == null)
        {
            return;
        }

        lastSessionCode = lobbyService.SessionCode;
        lastPlayerCount = lobbyService.PlayerCount;
        lastIsHost = lobbyService.IsHost;
        lastInSession = lobbyService.IsInSession;
        lastBusy = lobbyService.IsBusy;
        lastConnectionState = lobbyService.ConnectionState;
    }

    private static string NormalizeButtonLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = value
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim()
            .ToUpperInvariant();

        while (normalized.Contains("  "))
        {
            normalized = normalized.Replace("  ", " ");
        }

        return normalized.Replace(":", string.Empty);
    }

    private static void ReplaceButtonAction(
        Button button,
        Action callback
    )
    {
        if (button == null || callback == null)
        {
            return;
        }

        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(() => callback());
    }

    private static void SetActive(Component component, bool active)
    {
        if (component != null)
        {
            component.gameObject.SetActive(active);
        }
    }

    private static void SetActive(GameObject gameObject, bool active)
    {
        if (gameObject != null)
        {
            gameObject.SetActive(active);
        }
    }
}
