using DeadmansTales.Networking;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuManager : MonoBehaviour
{
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

    [SerializeField]
    private string gameplaySceneName = "Lobby_Island_2D";

    private OnlineLobbyService lobbyService;
    private bool lobbyOperationInProgress;
    private string statusMessage = string.Empty;
    private string defaultCreateOrJoinText = "CREATE OR JOIN";
    private string defaultEnterCodeText = "ENTER CODE";

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

        EnsureLobbyService();

        if (lobbyCodeInput != null)
        {
            lobbyCodeInput.onValueChanged.AddListener(
                HandleJoinCodeInputChanged
            );
        }
    }

    private async void Start()
    {
        ShowMainMenu();

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
    }

    public void ShowLevelSelectMenu()
    {
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

        if (lobbyCodeText != null)
        {
            lobbyCodeText.text = "LOBBY";
        }

        RefreshLobbyUi();
    }

    /// <summary>
    /// Kept for Shay's existing Host button hookup. This now creates a real
    /// Relay-backed online lobby instead of previewing the host panel.
    /// </summary>
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

        if (lobbyCodeText != null)
        {
            lobbyCodeText.text = "LOBBY";
        }

        HandleJoinCodeInputChanged(
            lobbyCodeInput != null ? lobbyCodeInput.text : string.Empty
        );

        RefreshLobbyUi();
    }

    /// <summary>
    /// Kept for the existing Host button hookup.
    /// </summary>
    public void PreviewHostLobby()
    {
        BeginCreateOnlineLobby();
    }

    /// <summary>
    /// Kept for the existing Join button hookup.
    /// </summary>
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

            SetStatus(
                left
                    ? "LEFT THE LOBBY"
                    : "COULD NOT LEAVE THE LOBBY"
            );

            if (left)
            {
                ShowConnectionOptions();
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

    /// <summary>
    /// Existing Level 1 button entry point. Outside an online session it starts
    /// a local single-player host. Inside an online session it is host-only and
    /// synchronizes the scene load for every connected player.
    /// </summary>
    public void LoadLevelOne()
    {
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

        SetStatus("STARTING GAME...");

        networkManager.SceneManager.LoadScene(
            gameplaySceneName,
            LoadSceneMode.Single
        );
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
        SetStatus("CREATING ONLINE LOBBY...");

        try
        {
            bool created = await lobbyService.CreateLobbyAsync();

            if (!created)
            {
                SetStatus("COULD NOT CREATE THE ONLINE LOBBY");
                return;
            }

            SetStatus("LOBBY CREATED • SHARE THE CODE");
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
            SetStatus("ENTER A VALID 6–8 CHARACTER LOBBY CODE");
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
        Debug.Log("LEVEL 1 BUTTON CLICKED");

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

        networkManager.SceneManager.LoadScene(
            gameplaySceneName,
            LoadSceneMode.Single
        );
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
                    : "ENTER A VALID 6–8 CHARACTER CODE"
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
                playerListText.text =
                    $"{role} • PLAYERS {lobbyService.PlayerCount}/" +
                    $"{lobbyService.MaxPlayers}\n{statusMessage}";
            }
        }
        else
        {
            if (lobbyCodeText != null)
            {
                lobbyCodeText.text = "LOBBY";
            }

            if (playerListText != null && playerListText.gameObject.activeSelf)
            {
                playerListText.text = statusMessage;
            }
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
