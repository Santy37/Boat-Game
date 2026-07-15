using System;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.Events;

namespace DeadmansTales.Networking
{
    public enum LobbyConnectionState
    {
        Offline,
        Initializing,
        Ready,
        Creating,
        Joining,
        InLobby,
        Leaving,
        Error
    }

    [Serializable]
    public sealed class LobbyStringEvent : UnityEvent<string>
    {
    }

    [Serializable]
    public sealed class LobbyIntEvent : UnityEvent<int>
    {
    }

    [Serializable]
    public sealed class LobbyBoolEvent : UnityEvent<bool>
    {
    }

    [Serializable]
    public sealed class LobbyStateEvent : UnityEvent<LobbyConnectionState>
    {
    }

    /// <summary>
    /// Owns the online session lifecycle without depending on any specific UI.
    ///
    /// UI scripts should call the public button/input methods and subscribe to
    /// the serialized UnityEvents instead of implementing networking directly.
    /// This keeps the multiplayer backend separate from the MainMenu UI branch.
    /// </summary>
    public sealed class OnlineLobbyService : MonoBehaviour
    {
        private const int SupportedMaxPlayers = 2;
        private const string ValidCodeCharacters =
            "6789BCDFGHJKLMNPQRTW";

        public static OnlineLobbyService Instance
        {
            get;
            private set;
        }

        [Header("Session Configuration")]
        [SerializeField]
        private string sessionType = "dead-mans-tale";

        [SerializeField]
        private string sessionName = "Dead Man's Tale";

        [SerializeField]
        private bool usePlayerName = true;

        [SerializeField]
        private bool persistAcrossScenes = true;

        [Header("UI Output Events")]
        [Tooltip("Human-readable connection status for a label.")]
        [SerializeField]
        private LobbyStringEvent onStatusChanged = new LobbyStringEvent();

        [Tooltip("Current host join code. Empty when not in a session.")]
        [SerializeField]
        private LobbyStringEvent onSessionCodeChanged =
            new LobbyStringEvent();

        [Tooltip("Current number of players in the session.")]
        [SerializeField]
        private LobbyIntEvent onPlayerCountChanged = new LobbyIntEvent();

        [Tooltip("True when this machine is the session host.")]
        [SerializeField]
        private LobbyBoolEvent onHostChanged = new LobbyBoolEvent();

        [Tooltip("True while a create, join, leave, or initialization operation is running.")]
        [SerializeField]
        private LobbyBoolEvent onBusyChanged = new LobbyBoolEvent();

        [Tooltip("True while this machine belongs to a multiplayer session.")]
        [SerializeField]
        private LobbyBoolEvent onSessionPresenceChanged =
            new LobbyBoolEvent();

        [Tooltip("True when the currently entered join code has a valid format.")]
        [SerializeField]
        private LobbyBoolEvent onJoinCodeValidityChanged =
            new LobbyBoolEvent();

        [SerializeField]
        private LobbyStateEvent onConnectionStateChanged =
            new LobbyStateEvent();

        private ISession currentSession;
        private Task initializationTask;
        private string pendingJoinCode = string.Empty;
        private bool servicesReady;
        private bool isBusy;
        private LobbyConnectionState connectionState =
            LobbyConnectionState.Offline;

        /// <summary>
        /// Fired whenever session metadata changes, including player count.
        /// Gameplay code can subscribe without depending on Unity UI.
        /// </summary>
        public event Action SessionChanged;

        public ISession CurrentSession => currentSession;

        public LobbyConnectionState ConnectionState => connectionState;

        public bool IsBusy => isBusy;

        public bool IsInSession => currentSession != null;

        public bool IsHost => currentSession?.IsHost ?? false;

        public int PlayerCount => currentSession?.Players?.Count ?? 0;

        public int MaxPlayers => SupportedMaxPlayers;

        public string SessionCode => currentSession?.Code ?? string.Empty;

        public string PendingJoinCode => pendingJoinCode;

        public bool IsPendingJoinCodeValid =>
            IsJoinCodeFormatValid(pendingJoinCode);

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning(
                    "[Online Lobby] Duplicate OnlineLobbyService destroyed.",
                    this
                );

                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (persistAcrossScenes)
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        private async void Start()
        {
            await EnsureServicesReadyAsync();
        }

        private void OnDestroy()
        {
            UnbindCurrentSession();

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnValidate()
        {
            sessionType = string.IsNullOrWhiteSpace(sessionType)
                ? "dead-mans-tale"
                : sessionType.Trim();

            sessionName = string.IsNullOrWhiteSpace(sessionName)
                ? "Dead Man's Tale"
                : sessionName.Trim();
        }

        /// <summary>
        /// Connect a TMP_InputField OnValueChanged event to this method.
        /// Whitespace is removed and letters are normalized to uppercase.
        /// </summary>
        public void SetJoinCode(string rawCode)
        {
            pendingJoinCode = NormalizeJoinCode(rawCode);

            onJoinCodeValidityChanged.Invoke(
                IsPendingJoinCodeValid
            );
        }

        /// <summary>
        /// Safe Unity Button entry point for creating a two-player Relay session.
        /// </summary>
        public async void CreateLobby()
        {
            await CreateLobbyAsync();
        }

        /// <summary>
        /// Safe Unity Button entry point for joining with the code supplied to
        /// SetJoinCode.
        /// </summary>
        public async void JoinLobby()
        {
            await JoinLobbyAsync(pendingJoinCode);
        }

        /// <summary>
        /// Safe Unity Button entry point for leaving the active session.
        /// </summary>
        public async void LeaveLobby()
        {
            await LeaveLobbyAsync();
        }

        public void CopySessionCode()
        {
            if (string.IsNullOrWhiteSpace(SessionCode))
            {
                SetStatus("No lobby code is available to copy.");
                return;
            }

            GUIUtility.systemCopyBuffer = SessionCode;
            SetStatus("Lobby code copied.");
        }

        public async Task<bool> CreateLobbyAsync()
        {
            if (!CanBeginSessionOperation("create a lobby"))
            {
                return false;
            }

            if (!await EnsureServicesReadyAsync())
            {
                return false;
            }

            if (!ValidateNetworkManager())
            {
                return false;
            }

            SetBusy(true);
            SetState(
                LobbyConnectionState.Creating,
                "Creating online lobby..."
            );

            try
            {
                SessionOptions options = BuildSessionOptions();

                IHostSession session =
                    await MultiplayerService.Instance.CreateSessionAsync(
                        options
                    );

                BindCurrentSession(session);

                SetState(
                    LobbyConnectionState.InLobby,
                    "Lobby created. Share the join code."
                );

                return true;
            }
            catch (Exception exception)
            {
                ReportFailure("Could not create the lobby", exception);
                return false;
            }
            finally
            {
                SetBusy(false);
            }
        }

        public async Task<bool> JoinLobbyAsync(string rawCode)
        {
            if (!CanBeginSessionOperation("join a lobby"))
            {
                return false;
            }

            string normalizedCode = NormalizeJoinCode(rawCode);
            SetJoinCode(normalizedCode);

            if (!IsJoinCodeFormatValid(normalizedCode))
            {
                SetState(
                    LobbyConnectionState.Error,
                    "Enter a valid 6-8 character lobby code."
                );

                return false;
            }

            if (!await EnsureServicesReadyAsync())
            {
                return false;
            }

            if (!ValidateNetworkManager())
            {
                return false;
            }

            SetBusy(true);
            SetState(
                LobbyConnectionState.Joining,
                "Joining online lobby..."
            );

            try
            {
                JoinSessionOptions options = BuildJoinOptions();

                ISession session =
                    await MultiplayerService.Instance.JoinSessionByCodeAsync(
                        normalizedCode,
                        options
                    );

                BindCurrentSession(session);

                SetState(
                    LobbyConnectionState.InLobby,
                    "Connected to lobby."
                );

                return true;
            }
            catch (Exception exception)
            {
                ReportFailure("Could not join the lobby", exception);
                return false;
            }
            finally
            {
                SetBusy(false);
            }
        }

        public async Task<bool> LeaveLobbyAsync()
        {
            if (isBusy)
            {
                SetStatus("Another multiplayer operation is still running.");
                return false;
            }

            if (currentSession == null)
            {
                SetState(
                    servicesReady
                        ? LobbyConnectionState.Ready
                        : LobbyConnectionState.Offline,
                    "Not currently in a lobby."
                );

                return true;
            }

            SetBusy(true);
            SetState(
                LobbyConnectionState.Leaving,
                "Leaving lobby..."
            );

            try
            {
                ISession sessionToLeave = currentSession;
                await sessionToLeave.LeaveAsync();

                UnbindCurrentSession();

                SetState(
                    LobbyConnectionState.Ready,
                    "Left the lobby."
                );

                return true;
            }
            catch (Exception exception)
            {
                ReportFailure("Could not leave the lobby", exception);
                return false;
            }
            finally
            {
                SetBusy(false);
            }
        }

        public async Task<bool> EnsureServicesReadyAsync()
        {
            if (servicesReady)
            {
                return true;
            }

            initializationTask ??= InitializeServicesInternalAsync();

            try
            {
                await initializationTask;
                servicesReady = true;
                return true;
            }
            catch (Exception exception)
            {
                initializationTask = null;
                servicesReady = false;

                ReportFailure(
                    "Unity multiplayer services failed to initialize",
                    exception
                );

                return false;
            }
        }

        private async Task InitializeServicesInternalAsync()
        {
            SetBusy(true);
            SetState(
                LobbyConnectionState.Initializing,
                "Connecting to Unity multiplayer services..."
            );

            try
            {
                while (
                    UnityServices.State ==
                    ServicesInitializationState.Initializing
                )
                {
                    await Task.Yield();
                }

                if (
                    UnityServices.State !=
                    ServicesInitializationState.Initialized
                )
                {
                    await UnityServices.InitializeAsync();
                }

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance
                        .SignInAnonymouslyAsync();
                }

                if (MultiplayerService.Instance == null)
                {
                    throw new InvalidOperationException(
                        "MultiplayerService.Instance is unavailable after " +
                        "Unity Services initialization."
                    );
                }

                SetState(
                    LobbyConnectionState.Ready,
                    "Online multiplayer ready."
                );
            }
            finally
            {
                SetBusy(false);
            }
        }

        private SessionOptions BuildSessionOptions()
        {
            SessionOptions options = new SessionOptions
            {
                MaxPlayers = SupportedMaxPlayers,
                Name = sessionName,
                Type = sessionType
            };

            if (usePlayerName)
            {
                options.WithPlayerName();
            }

            options.WithRelayNetwork();
            return options;
        }

        private JoinSessionOptions BuildJoinOptions()
        {
            JoinSessionOptions options = new JoinSessionOptions
            {
                Type = sessionType
            };

            if (usePlayerName)
            {
                options.WithPlayerName();
            }

            return options;
        }

        private bool CanBeginSessionOperation(string operationDescription)
        {
            if (isBusy)
            {
                SetStatus(
                    $"Cannot {operationDescription}; another operation is running."
                );

                return false;
            }

            if (currentSession != null)
            {
                SetStatus(
                    $"Leave the current lobby before trying to {operationDescription}."
                );

                return false;
            }

            return true;
        }

        private bool ValidateNetworkManager()
        {
            if (NetworkManager.Singleton == null)
            {
                SetState(
                    LobbyConnectionState.Error,
                    "No NetworkManager exists in the MainMenu scene."
                );

                Debug.LogError(
                    "[Online Lobby] Add the existing '[BB] NetworkManager' " +
                    "prefab to MainMenu before creating or joining a lobby.",
                    this
                );

                return false;
            }

            if (NetworkManager.Singleton.IsListening)
            {
                SetState(
                    LobbyConnectionState.Error,
                    "Networking is already running without an active session."
                );

                return false;
            }

            return true;
        }

        private void BindCurrentSession(ISession session)
        {
            UnbindCurrentSession();

            currentSession = session;

            if (currentSession == null)
            {
                throw new InvalidOperationException(
                    "The multiplayer service returned a null session."
                );
            }

            currentSession.Changed += HandleSessionChanged;
            currentSession.RemovedFromSession += HandleSessionEnded;
            currentSession.Deleted += HandleSessionEnded;

            PublishSessionState();
        }

        private void UnbindCurrentSession()
        {
            if (currentSession != null)
            {
                currentSession.Changed -= HandleSessionChanged;
                currentSession.RemovedFromSession -= HandleSessionEnded;
                currentSession.Deleted -= HandleSessionEnded;
                currentSession = null;
            }

            PublishSessionState();
        }

        private void HandleSessionChanged()
        {
            PublishSessionState();
        }

        private void HandleSessionEnded()
        {
            UnbindCurrentSession();

            SetState(
                servicesReady
                    ? LobbyConnectionState.Ready
                    : LobbyConnectionState.Offline,
                "The multiplayer session ended."
            );
        }

        private void PublishSessionState()
        {
            onSessionCodeChanged.Invoke(SessionCode);
            onPlayerCountChanged.Invoke(PlayerCount);
            onHostChanged.Invoke(IsHost);
            onSessionPresenceChanged.Invoke(IsInSession);

            SessionChanged?.Invoke();
        }

        private void SetBusy(bool value)
        {
            if (isBusy == value)
            {
                return;
            }

            isBusy = value;
            onBusyChanged.Invoke(isBusy);
        }

        private void SetState(
            LobbyConnectionState newState,
            string statusMessage
        )
        {
            connectionState = newState;
            onConnectionStateChanged.Invoke(connectionState);
            SetStatus(statusMessage);
        }

        private void SetStatus(string message)
        {
            string safeMessage = message ?? string.Empty;

            Debug.Log(
                $"[Online Lobby] {safeMessage}",
                this
            );

            onStatusChanged.Invoke(safeMessage);
        }

        private void ReportFailure(
            string context,
            Exception exception
        )
        {
            string message =
                $"{context}: {exception.Message}";

            Debug.LogException(exception, this);

            SetState(
                LobbyConnectionState.Error,
                message
            );
        }

        private static string NormalizeJoinCode(string rawCode)
        {
            if (string.IsNullOrWhiteSpace(rawCode))
            {
                return string.Empty;
            }

            StringBuilder normalized = new StringBuilder(8);

            foreach (char character in rawCode)
            {
                if (char.IsWhiteSpace(character) || character == '-')
                {
                    continue;
                }

                normalized.Append(
                    char.ToUpperInvariant(character)
                );
            }

            return normalized.ToString();
        }

        private static bool IsJoinCodeFormatValid(string code)
        {
            if (
                string.IsNullOrEmpty(code) ||
                code.Length < 6 ||
                code.Length > 8
            )
            {
                return false;
            }

            foreach (char character in code)
            {
                if (!ValidCodeCharacters.Contains(character))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
