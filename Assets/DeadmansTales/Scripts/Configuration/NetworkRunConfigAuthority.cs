using System;
using DeadmansTales.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace DeadmansTales.Configuration
{
    [Serializable]
    public sealed class ConfigStringEvent : UnityEvent<string>
    {
    }

    [Serializable]
    public sealed class ConfigBoolEvent : UnityEvent<bool>
    {
    }

    /// <summary>
    /// Loads run configuration only on the server and synchronizes the exact
    /// validated values to every client.
    ///
    /// This prevents separate local JSON override files from becoming separate
    /// sources of gameplay truth in an online session.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkRunConfigAuthority : NetworkBehaviour
    {
        public static NetworkRunConfigAuthority Instance
        {
            get;
            private set;
        }

        [Header("Lifecycle")]
        [SerializeField]
        private bool persistAcrossScenes = true;

        [Header("Host Defaults")]
        [SerializeField]
        private string defaultConfigId = "boat_default";

        [SerializeField]
        private bool loadDefaultConfigOnNetworkSpawn = true;

        [Header("Local Output Events")]
        [SerializeField]
        private ConfigBoolEvent onConfigReadyChanged = new ConfigBoolEvent();

        [SerializeField]
        private ConfigStringEvent onConfigSummaryChanged =
            new ConfigStringEvent();

        public readonly NetworkVariable<NetworkBoatRunConfig> ActiveConfig =
            new NetworkVariable<NetworkBoatRunConfig>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server
            );

        private global::BoatRunConfig cachedRuntimeConfig;
        private string hostLoadedFrom = string.Empty;

        public event Action<global::BoatRunConfig> ConfigChanged;

        public bool IsConfigReady => ActiveConfig.Value.IsValid;

        public string ConfigId => ActiveConfig.Value.Id.ToString();

        public int ConfigVersion => ActiveConfig.Value.ConfigVersion;

        public string HostLoadedFrom => hostLoadedFrom;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning(
                    "[Run Config Authority] Duplicate instance destroyed.",
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
            defaultConfigId = string.IsNullOrWhiteSpace(defaultConfigId)
                ? "boat_default"
                : defaultConfigId.Trim();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ActiveConfig.OnValueChanged += HandleConfigChanged;

            if (
                IsServer &&
                loadDefaultConfigOnNetworkSpawn &&
                !ActiveConfig.Value.IsValid
            )
            {
                SelectConfigServer(defaultConfigId);
            }
            else
            {
                PublishSnapshot(ActiveConfig.Value);
            }
        }

        public override void OnNetworkDespawn()
        {
            ActiveConfig.OnValueChanged -= HandleConfigChanged;
            cachedRuntimeConfig = null;
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
        /// Host/server entry point for selecting the active JSON configuration.
        /// Clients must never call BoatRunConfigLoader for authoritative values.
        /// </summary>
        public bool SelectConfigServer(string requestedConfigId)
        {
            RequireServer(nameof(SelectConfigServer));

            string safeConfigId = string.IsNullOrWhiteSpace(requestedConfigId)
                ? defaultConfigId
                : requestedConfigId.Trim();

            global::BoatRunConfig loadedConfig =
                global::BoatRunConfigLoader.Load(
                    safeConfigId,
                    out string loadedFrom
                );

            if (loadedConfig == null)
            {
                Debug.LogError(
                    $"[Run Config Authority] Failed to load '{safeConfigId}'.",
                    this
                );
                return false;
            }

            loadedConfig.Validate();
            hostLoadedFrom = loadedFrom;
            ActiveConfig.Value =
                NetworkBoatRunConfig.FromRuntimeConfig(loadedConfig);

            ApplyIdentityToRunStateServer();

            Debug.Log(
                "[Run Config Authority] Host selected configuration.\n" +
                $"ID: {ActiveConfig.Value.Id}\n" +
                $"Version: {ActiveConfig.Value.ConfigVersion}\n" +
                $"Loaded From: {hostLoadedFrom}",
                this
            );

            return true;
        }

        /// <summary>
        /// Returns a normal BoatRunConfig built from the synchronized host copy.
        /// Both host and clients receive the same values.
        /// </summary>
        public bool TryGetRuntimeConfig(out global::BoatRunConfig config)
        {
            if (!IsConfigReady)
            {
                config = null;
                return false;
            }

            if (cachedRuntimeConfig == null)
            {
                cachedRuntimeConfig = ActiveConfig.Value.ToRuntimeConfig();
            }

            config = cachedRuntimeConfig;
            return true;
        }

        public global::BoatRunConfig RequireRuntimeConfig()
        {
            if (!TryGetRuntimeConfig(out global::BoatRunConfig config))
            {
                throw new InvalidOperationException(
                    "The synchronized host run configuration is not ready."
                );
            }

            return config;
        }

        /// <summary>
        /// Copies the selected config identity into the persistent run state.
        /// Safe to call again after NetworkRunState has spawned.
        /// </summary>
        public void ApplyIdentityToRunStateServer()
        {
            RequireServer(nameof(ApplyIdentityToRunStateServer));

            NetworkRunState runState = NetworkRunState.Instance;
            if (
                runState == null ||
                !runState.IsSpawned ||
                !ActiveConfig.Value.IsValid
            )
            {
                return;
            }

            runState.ConfigId.Value = ActiveConfig.Value.Id;
            runState.ConfigVersion.Value = ActiveConfig.Value.ConfigVersion;
        }

        private void HandleConfigChanged(
            NetworkBoatRunConfig previousValue,
            NetworkBoatRunConfig currentValue
        )
        {
            cachedRuntimeConfig = null;
            PublishSnapshot(currentValue);
        }

        private void PublishSnapshot(NetworkBoatRunConfig snapshot)
        {
            bool ready = snapshot.IsValid;
            onConfigReadyChanged.Invoke(ready);

            if (!ready)
            {
                onConfigSummaryChanged.Invoke("No synchronized run config");
                return;
            }

            cachedRuntimeConfig = snapshot.ToRuntimeConfig();
            string summary =
                $"{snapshot.Id} v{snapshot.ConfigVersion}";

            onConfigSummaryChanged.Invoke(summary);
            ConfigChanged?.Invoke(cachedRuntimeConfig);
        }

        private void RequireServer(string methodName)
        {
            if (!IsSpawned)
            {
                throw new InvalidOperationException(
                    $"NetworkRunConfigAuthority.{methodName} was called " +
                    "before its NetworkObject spawned."
                );
            }

            if (!IsServer)
            {
                throw new InvalidOperationException(
                    $"NetworkRunConfigAuthority.{methodName} may only be " +
                    "called by the host or server."
                );
            }
        }
    }
}
