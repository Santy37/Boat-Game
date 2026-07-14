using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace DeadmansTales.Networking
{
    public enum NetworkRunStatus : byte
    {
        Lobby,
        Loading,
        Playing,
        Completed,
        Failed
    }

    [Serializable]
    public sealed class RunIntEvent : UnityEvent<int>
    {
    }

    [Serializable]
    public sealed class RunStatusEvent : UnityEvent<NetworkRunStatus>
    {
    }

    [Serializable]
    public sealed class RunStringEvent : UnityEvent<string>
    {
    }

    /// <summary>
    /// Server-authoritative state shared by the entire active game run.
    ///
    /// This class intentionally contains no combat, inventory, enemy, UI,
    /// island-layout, or upgrade behavior. Those systems read this state and
    /// request server-side changes through their own gameplay logic.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkRunState : NetworkBehaviour
    {
        public static NetworkRunState Instance
        {
            get;
            private set;
        }

        [Header("Lifecycle")]
        [SerializeField]
        private bool persistAcrossScenes = true;

        [Header("Defaults")]
        [SerializeField]
        private string defaultConfigId = "boat_default";

        [SerializeField]
        [Min(1)]
        private int defaultConfigVersion = 1;

        [Header("Local Output Events")]
        [SerializeField]
        private RunIntEvent onMasterSeedChanged = new RunIntEvent();

        [SerializeField]
        private RunIntEvent onStageChanged = new RunIntEvent();

        [SerializeField]
        private RunStatusEvent onStatusChanged = new RunStatusEvent();

        [SerializeField]
        private RunIntEvent onPlayerCountChanged = new RunIntEvent();

        [SerializeField]
        private RunStringEvent onConfigIdChanged = new RunStringEvent();

        [SerializeField]
        private RunIntEvent onConfigVersionChanged = new RunIntEvent();

        public readonly NetworkVariable<int> MasterSeed =
            new NetworkVariable<int>(
                0,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server
            );

        public readonly NetworkVariable<int> CurrentStage =
            new NetworkVariable<int>(
                0,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server
            );

        public readonly NetworkVariable<NetworkRunStatus> Status =
            new NetworkVariable<NetworkRunStatus>(
                NetworkRunStatus.Lobby,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server
            );

        public readonly NetworkVariable<int> ActivePlayerCount =
            new NetworkVariable<int>(
                1,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server
            );

        public readonly NetworkVariable<FixedString64Bytes> ConfigId =
            new NetworkVariable<FixedString64Bytes>(
                new FixedString64Bytes("boat_default"),
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server
            );

        public readonly NetworkVariable<int> ConfigVersion =
            new NetworkVariable<int>(
                1,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server
            );

        public bool IsInitialized =>
            IsSpawned &&
            MasterSeed.Value != 0;

        public int Seed => MasterSeed.Value;

        public int StageIndex => CurrentStage.Value;

        public NetworkRunStatus RunStatus => Status.Value;

        public string CurrentConfigId => ConfigId.Value.ToString();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning(
                    "[Run State] Duplicate NetworkRunState destroyed.",
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

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(defaultConfigId))
            {
                defaultConfigId = "boat_default";
            }
            else
            {
                defaultConfigId = defaultConfigId.Trim();
            }

            defaultConfigVersion = Mathf.Max(1, defaultConfigVersion);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            MasterSeed.OnValueChanged += HandleMasterSeedChanged;
            CurrentStage.OnValueChanged += HandleStageChanged;
            Status.OnValueChanged += HandleStatusChanged;
            ActivePlayerCount.OnValueChanged += HandlePlayerCountChanged;
            ConfigId.OnValueChanged += HandleConfigIdChanged;
            ConfigVersion.OnValueChanged += HandleConfigVersionChanged;

            if (IsServer && ConfigId.Value.IsEmpty)
            {
                ConfigId.Value = new FixedString64Bytes(defaultConfigId);
                ConfigVersion.Value = defaultConfigVersion;
            }

            PublishCurrentValues();
        }

        public override void OnNetworkDespawn()
        {
            MasterSeed.OnValueChanged -= HandleMasterSeedChanged;
            CurrentStage.OnValueChanged -= HandleStageChanged;
            Status.OnValueChanged -= HandleStatusChanged;
            ActivePlayerCount.OnValueChanged -= HandlePlayerCountChanged;
            ConfigId.OnValueChanged -= HandleConfigIdChanged;
            ConfigVersion.OnValueChanged -= HandleConfigVersionChanged;

            base.OnNetworkDespawn();
        }

        protected override void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            base.OnDestroy();
        }

        /// <summary>
        /// Starts a fresh run. This must only be called by server-side code.
        /// </summary>
        public void InitializeNewRunServer(
            int requestedSeed,
            string requestedConfigId,
            int requestedConfigVersion,
            int startingStage = 1
        )
        {
            RequireServer(nameof(InitializeNewRunServer));

            int safeSeed = requestedSeed == 0
                ? 1
                : requestedSeed;

            string safeConfigId = string.IsNullOrWhiteSpace(requestedConfigId)
                ? defaultConfigId
                : requestedConfigId.Trim();

            int safeConfigVersion = Mathf.Max(
                1,
                requestedConfigVersion
            );

            MasterSeed.Value = safeSeed;
            CurrentStage.Value = Mathf.Max(1, startingStage);
            Status.Value = NetworkRunStatus.Loading;
            ActivePlayerCount.Value = GetConnectedPlayerCount();
            ConfigId.Value = new FixedString64Bytes(safeConfigId);
            ConfigVersion.Value = safeConfigVersion;

            Debug.Log(
                "[Run State] New run initialized.\n" +
                $"Seed: {MasterSeed.Value}\n" +
                $"Stage: {CurrentStage.Value}\n" +
                $"Players: {ActivePlayerCount.Value}\n" +
                $"Config: {ConfigId.Value}\n" +
                $"Config Version: {ConfigVersion.Value}",
                this
            );
        }

        public void SetStageServer(int stageIndex)
        {
            RequireServer(nameof(SetStageServer));
            CurrentStage.Value = Mathf.Max(1, stageIndex);
        }

        public void AdvanceStageServer()
        {
            RequireServer(nameof(AdvanceStageServer));
            CurrentStage.Value = Mathf.Max(1, CurrentStage.Value + 1);
        }

        public void SetStatusServer(NetworkRunStatus newStatus)
        {
            RequireServer(nameof(SetStatusServer));
            Status.Value = newStatus;
        }

        public void RefreshPlayerCountServer()
        {
            RequireServer(nameof(RefreshPlayerCountServer));
            ActivePlayerCount.Value = GetConnectedPlayerCount();
        }

        public void ResetToLobbyServer()
        {
            RequireServer(nameof(ResetToLobbyServer));

            MasterSeed.Value = 0;
            CurrentStage.Value = 0;
            Status.Value = NetworkRunStatus.Lobby;
            ActivePlayerCount.Value = GetConnectedPlayerCount();
            ConfigId.Value = new FixedString64Bytes(defaultConfigId);
            ConfigVersion.Value = defaultConfigVersion;
        }

        private int GetConnectedPlayerCount()
        {
            if (NetworkManager == null)
            {
                return 1;
            }

            return Mathf.Max(
                1,
                NetworkManager.ConnectedClientsIds.Count
            );
        }

        private void RequireServer(string methodName)
        {
            if (!IsSpawned)
            {
                throw new InvalidOperationException(
                    $"NetworkRunState.{methodName} was called before " +
                    "the NetworkObject spawned."
                );
            }

            if (!IsServer)
            {
                throw new InvalidOperationException(
                    $"NetworkRunState.{methodName} may only be called " +
                    "by the server or host."
                );
            }
        }

        private void PublishCurrentValues()
        {
            onMasterSeedChanged.Invoke(MasterSeed.Value);
            onStageChanged.Invoke(CurrentStage.Value);
            onStatusChanged.Invoke(Status.Value);
            onPlayerCountChanged.Invoke(ActivePlayerCount.Value);
            onConfigIdChanged.Invoke(ConfigId.Value.ToString());
            onConfigVersionChanged.Invoke(ConfigVersion.Value);
        }

        private void HandleMasterSeedChanged(
            int previousValue,
            int currentValue
        )
        {
            onMasterSeedChanged.Invoke(currentValue);
        }

        private void HandleStageChanged(
            int previousValue,
            int currentValue
        )
        {
            onStageChanged.Invoke(currentValue);
        }

        private void HandleStatusChanged(
            NetworkRunStatus previousValue,
            NetworkRunStatus currentValue
        )
        {
            onStatusChanged.Invoke(currentValue);
        }

        private void HandlePlayerCountChanged(
            int previousValue,
            int currentValue
        )
        {
            onPlayerCountChanged.Invoke(currentValue);
        }

        private void HandleConfigIdChanged(
            FixedString64Bytes previousValue,
            FixedString64Bytes currentValue
        )
        {
            onConfigIdChanged.Invoke(currentValue.ToString());
        }

        private void HandleConfigVersionChanged(
            int previousValue,
            int currentValue
        )
        {
            onConfigVersionChanged.Invoke(currentValue);
        }
    }
}
