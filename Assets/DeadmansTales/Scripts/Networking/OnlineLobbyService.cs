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
    /// Owns the Unity Multiplayer Services session and Relay lifecycle without
    /// depending on a specific menu layout.
    /// </summary>
    public sealed class OnlineLobbyService : MonoBehaviour
    {
        private const int SupportedMaxPlayers = 4;
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
        private bool usePlayerName;

        [SerializeField]
        private bool persistAcrossScenes = true;

        [Header("UI Output Events")]
        [SerializeField]
        private LobbyStringEvent onStatusChanged = new LobbyStringEvent();

        [SerializeField]
        private LobbyStringEvent onSessionCodeChanged =
            new LobbyStringEvent();

        [SerializeField]
        private LobbyIntEvent onPlayerCountChanged = new LobbyIntEvent();

        [SerializeField]
        private LobbyBoolEvent onHostChanged = new LobbyBoolEvent();

        [SerializeField]
        private LobbyBoolEvent onBusyChanged = new LobbyBoolEvent();

        [SerializeField]
        private LobbyBoolEvent onSessionPresenceChanged =
            new LobbyBoolEvent();

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

        public void SetJoinCode(string rawCode)
        {
            pendingJoinCode = NormalizeJoinCode(rawCode);
            onJoinCodeValidityChanged.Invoke(IsPendingJoinCodeValid);
        }

        public async void CreateLobby()
        {
            await CreateLobbyAsync();
        }

        public async void JoinLobby()
        {
            await JoinLobbyAsync(pendingJoinCode);
        }

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

        /// <summary>
        /// Clears any half-open NGO/Relay state left behind by a failed operation.
        /// Safe to call from menu cancel/back actions.
        /// </summary>
        public void ResetLocalSession()
        {
            ResetLocalNetworkState();
            SetState(
                servicesReady
                    ? LobbyConnectionState.Ready
                    : LobbyConnectionState.Offline,
                "Online lobby reset."
            );
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
                ResetLocalNetworkState();
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
                ResetLocalNetworkState();
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
                ResetLocalNetworkState();
                ReportJoinFailure(exception);
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

            SetBusy(true);
            SetState(
                LobbyConnectionState.Leaving,
                "Leaving lobby..."
            );

            bool remoteLeaveSucceeded = true;
            ISession sessionToLeave = currentSession;

            try
            {
                if (sessionToLeave != null)
                {
                    await sessionToLeave.LeaveAsync();
                }

                // Multiplayer Services owns the NGO network handler for Relay
                // sessions and shuts it down as part of LeaveAsync.
                UnbindCurrentSession();
            }
            catch (Exception exception)
            {
                remoteLeaveSucceeded = false;
                Debug.LogWarning(
                    "[Online Lobby] The backend leave request failed, but the " +
                    $"local session will still be reset: {exception.Message}",
                    this
                );

                // If the backend could not complete its normal cleanup, make
                // sure no unusable local NGO state survives the failed leave.
                ResetLocalNetworkState();
            }
            finally
            {
                SetBusy(false);
            }

            SetState(
                servicesReady
                    ? LobbyConnectionState.Ready
                    : LobbyConnectionState.Offline,
                remoteLeaveSucceeded
                    ? "Left the lobby."
                    : "Lobby reset locally."
            );

            return true;
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

            return ValidateAndRecoverIdleNetworkManager();
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
                    "[Online Lobby] The project-owned NetworkManager was not " +
                    "created before the lobby operation.",
                    this
                );

                return false;
            }

            return ValidateAndRecoverIdleNetworkManager();
        }

        private bool ValidateAndRecoverIdleNetworkManager()
        {
            NetworkManager networkManager = NetworkManager.Singleton;

            if (networkManager == null)
            {
                return true;
            }

            if (!networkManager.IsListening)
            {
                return true;
            }

            if (currentSession != null)
            {
                SetStatus("Networking is already running for the current lobby.");
                return false;
            }

            Debug.LogWarning(
                "[Online Lobby] Found stale NGO networking without an active " +
                "session. Shutting it down before retrying.",
                networkManager
            );

            networkManager.Shutdown();

            if (networkManager.IsListening)
            {
                SetState(
                    LobbyConnectionState.Error,
                    "Networking could not be reset. Try again."
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
            // The session network handler owns NGO shutdown. Calling
            // NetworkManager.Shutdown here races that handler and produces the
            // "shutdown outside of a session" warning.
            UnbindCurrentSession();

            SetState(
                servicesReady
                    ? LobbyConnectionState.Ready
                    : LobbyConnectionState.Offline,
                "The multiplayer session ended."
            );
        }

        private void ResetLocalNetworkState()
        {
            UnbindCurrentSession();

            NetworkManager networkManager = NetworkManager.Singleton;

            if (networkManager != null && networkManager.IsListening)
            {
                networkManager.Shutdown();
            }
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
            string message = $"{context}: {exception.Message}";
            Debug.LogException(exception, this);

            SetState(
                LobbyConnectionState.Error,
                message
            );
        }

        private void ReportJoinFailure(Exception exception)
        {
            Debug.LogWarning(
                "[Online Lobby] Join failed and local networking was reset: " +
                exception.Message,
                this
            );

            SetState(
                LobbyConnectionState.Error,
                "Lobby not found. Check the code and try again."
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
