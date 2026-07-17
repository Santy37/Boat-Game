using System;
using System.Reflection;
using DeadmansTales.Networking;
using TMPro;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuManager : MonoBehaviour
{
    private const int LobbyMaxPlayers = 4;
    private const string SessionType = "dead-mans-tale";
    private const string SessionName = "Dead Man's Tale";

    private static readonly string[] SelectableSceneNames =
    {
        "Lobby_Island_2D",
        "Boat_Gameplay_2D"
    };

    private static readonly MethodInfo BindCurrentSessionMethod =
        typeof(OnlineLobbyService).GetMethod(
            "BindCurrentSession",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

    private static readonly MethodInfo SetLobbyStateMethod =
        typeof(OnlineLobbyService).GetMethod(
            "SetState",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

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

    private OnlineLobbyService lobbyService;
    private bool lobbyOperationInProgress;
    private bool localReady;
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
        localReady = false;
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
            lobbyCodeText.text = "LOBBY";
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
            lobbyCodeText.text = "LOBBY";
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
                localReady = false;
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

        localReady = !localReady;
        UpdateReadyButtonLabel();
        SetStatus(localReady ? "YOU ARE READY" : "YOU ARE NOT READY");
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
        if (lobbyService != null && lobbyService.IsInSession)
        {
            StartMultiplayerGame();
            return;
        }

        selectedSceneIndex = 0;
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
            bool created = await CreateFourPlayerLobbyAsync();

            if (!created)
            {
                SetStatus("COULD NOT CREATE THE ONLINE LOBBY");
                return;
            }

            localReady = true;
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

    private async System.Threading.Tasks.Task<bool>
        CreateFourPlayerLobbyAsync()
    {
        if (!await lobbyService.EnsureServicesReadyAsync())
        {
            return false;
        }

        if (NetworkManager.Singleton == null)
        {
            SetStatus("NETWORK MANAGER WAS NOT FOUND");
            return false;
        }

        if (BindCurrentSessionMethod == null)
        {
            Debug.LogError(
                "[Main Menu] Could not access OnlineLobbyService session binding."
            );
            return false;
        }

        try
        {
            SessionOptions options = new SessionOptions
            {
                MaxPlayers = LobbyMaxPlayers,
                Name = SessionName,
                Type = SessionType
            };

            options.WithRelayNetwork();

            IHostSession session =
                await MultiplayerService.Instance.CreateSessionAsync(options);

            BindCurrentSessionMethod.Invoke(
                lobbyService,
                new object[] { session }
            );

            SetLobbyStateMethod?.Invoke(
                lobbyService,
                new object[]
                {
                    LobbyConnectionState.InLobby,
                    "Lobby created. Share the join code."
                }
            );

            return lobbyService.IsInSession;
        }
        catch (TargetInvocationException exception)
        {
            Debug.LogException(exception.InnerException ?? exception);
            return false;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            return false;
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

            localReady = false;
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

        networkManager.SceneManager.LoadScene(
            selectedScene,
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

        if (lobbyService == null || !lobbyService.IsInSession)
        {
            localReady = false;
        }

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
                string readyState = lobbyService.IsHost
                    ? string.Empty
                    : localReady
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
                lobbyCodeText.text = "LOBBY";
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

                case "LEVEL 1":
                    ReplaceButtonAction(button, LoadLevelOne);
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

        TMP_Text label = readyButton.GetComponentInChildren<TMP_Text>(true);

        if (label != null)
        {
            label.text = localReady ? "UNREADY" : "READY";
        }
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
        return selectedSceneIndex == 0
            ? "LOBBY ISLAND"
            : "BOAT GAMEPLAY";
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