using System;
using DeadmansTales.Ship;
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
        /// <summary>How much the ship shop adds to sink-meter capacity per purchase.</summary>
        public const float ShipSinkBonusPerUpgrade = 100f;

        /// <summary>How much the ship shop adds to hull health capacity per purchase.</summary>
        public const float ShipHealthBonusPerUpgrade = 50f;

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

        /// <summary>
        /// Total sink-meter capacity the crew has bought at the ship shop
        /// this run, added on top of NetworkShipSinkMeter's own base
        /// maximum. Lives here rather than on the ship itself because the
        /// ship is a scene-placed object that only exists in the boat and
        /// kraken-arena scenes and is rebuilt fresh each time -- this
        /// persists across every scene so the upgrade survives visiting a
        /// shop island and sailing on.
        /// </summary>
        public readonly NetworkVariable<float> ShipSinkBonus =
            new NetworkVariable<float>(
                0f,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server
            );

        /// <summary>Total hull health capacity the crew has bought this run.</summary>
        public readonly NetworkVariable<float> ShipHealthBonus =
            new NetworkVariable<float>(
                0f,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server
            );

        /// <summary>
        /// How far along the current boat leg the voyage is, 0 to 1.
        ///
        /// Lives here for the same reason ShipSinkBonus does: the progress bar
        /// is a scene-placed object rebuilt from scratch every time the boat
        /// scene loads, and it cannot own authoritative state that clients
        /// must agree on. It used to advance independently on every peer,
        /// which meant a client's bar ran ahead through fights the host was
        /// still resolving and reported the leg finished while the host was
        /// mid-battle.
        /// </summary>
        public readonly NetworkVariable<float> LegProgress =
            new NetworkVariable<float>(
                0f,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server
            );

        /// <summary>
        /// How many of this leg's events (rock waves / pirate ships) the
        /// SERVER has fully resolved. Clients complete their own matching
        /// event only when this number rises, which is what stops a client
        /// waiting out a fixed three-second pause and sailing on while the
        /// host is still fighting.
        /// </summary>
        public readonly NetworkVariable<int> LegEventsCompleted =
            new NetworkVariable<int>(
                0,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server
            );

        /// <summary>
        /// Which boat level (1-based) the current leg is running, and so how
        /// many events it contains. 0 means the server has not started a leg
        /// yet, which clients treat as "wait".
        ///
        /// This has to be replicated because the level is chosen by a menu
        /// button click that writes a plain local static
        /// (<c>BoatLevelSelection.PendingLevel</c>). Only the host ever clicks
        /// it -- a joining client's static stays 0 and falls back to the
        /// prefab's default level, so the two peers built legs with different
        /// numbers of fights even when they agreed on the random seed.
        /// </summary>
        public readonly NetworkVariable<int> LegLevel =
            new NetworkVariable<int>(
                0,
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
                if (Instance.IsSpawned)
                {
                    NetworkObject duplicate = GetComponent<NetworkObject>();
                    NetworkManager manager = NetworkManager.Singleton;
                    bool spawnedOnClient =
                        duplicate != null &&
                        duplicate.IsSpawned &&
                        manager != null &&
                        manager.IsListening &&
                        !manager.IsServer;

                    if (spawnedOnClient)
                    {
                        // Clients must never locally destroy an NGO-spawned
                        // object; the server owns its lifetime and will
                        // despawn it.
                        Debug.LogError(
                            "[Run State] A spawned duplicate reached a " +
                            "client. The server must despawn it.",
                            this
                        );
                        return;
                    }

                    Debug.LogWarning(
                        "[Run State] Duplicate NetworkRunState destroyed.",
                        this
                    );

                    Destroy(gameObject);
                    return;
                }

                Debug.Log(
                    "[Run State] Replacing an unspawned state left by a " +
                    "previous network session.",
                    this
                );
                Destroy(Instance.gameObject);
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

        public override void OnDestroy()
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
            ShipSinkBonus.Value = 0f;
            ShipHealthBonus.Value = 0f;

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

        /// <summary>
        /// Applies one ship shop purchase: raises the crew-wide sink-meter
        /// and hull capacity bonuses, and -- if the player's ship happens
        /// to already be loaded in the current scene -- tops up its current
        /// values by the same amount so the purchase feels immediate rather
        /// than only applying the next time the ship is (re)spawned.
        /// </summary>
        public void GrantShipUpgradeServer()
        {
            RequireServer(nameof(GrantShipUpgradeServer));

            ShipSinkBonus.Value += ShipSinkBonusPerUpgrade;
            ShipHealthBonus.Value += ShipHealthBonusPerUpgrade;

            PlayerShipMarker playerShip = FindFirstObjectByType<PlayerShipMarker>();

            if (playerShip != null)
            {
                NetworkShipSinkMeter sinkMeter =
                    playerShip.GetComponent<NetworkShipSinkMeter>();
                sinkMeter?.RepairServer(ShipSinkBonusPerUpgrade);

                NetworkShipHealth shipHealth =
                    playerShip.GetComponent<NetworkShipHealth>();
                shipHealth?.RepairServer(ShipHealthBonusPerUpgrade);
            }

            Debug.Log(
                "[Run State] Ship upgrade purchased.\n" +
                $"Sink capacity bonus: {ShipSinkBonus.Value:0}\n" +
                $"Hull health bonus: {ShipHealthBonus.Value:0}",
                this
            );
        }

        /// <summary>
        /// Server-only: starts a fresh boat leg from zero at the given level.
        /// Called by the boat scene's progress bar once it is ready, so leg two
        /// does not begin already showing leg one's finished bar, and so
        /// clients learn which level -- and therefore how many fights -- this
        /// leg contains.
        /// </summary>
        public void BeginLegServer(int level)
        {
            RequireServer(nameof(BeginLegServer));

            LegProgress.Value = 0f;
            LegEventsCompleted.Value = 0;
            LegLevel.Value = Mathf.Max(1, level);
        }

        /// <summary>
        /// Server-only: publishes this frame's authoritative leg progress and
        /// resolved-event count for every client's progress bar to follow.
        /// </summary>
        public void PublishLegProgressServer(
            float progress01,
            int eventsCompleted
        )
        {
            RequireServer(nameof(PublishLegProgressServer));

            float clamped = Mathf.Clamp01(progress01);

            // Written only on change. A NetworkVariable set to the value it
            // already holds still marks itself dirty, so assigning every frame
            // would put an otherwise idle bar on the wire at the full tick
            // rate for the whole voyage.
            if (!Mathf.Approximately(LegProgress.Value, clamped))
            {
                LegProgress.Value = clamped;
            }

            int safeCount = Mathf.Max(0, eventsCompleted);

            if (LegEventsCompleted.Value != safeCount)
            {
                LegEventsCompleted.Value = safeCount;
            }
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
            ShipSinkBonus.Value = 0f;
            ShipHealthBonus.Value = 0f;
            LegProgress.Value = 0f;
            LegEventsCompleted.Value = 0;
            LegLevel.Value = 0;
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
