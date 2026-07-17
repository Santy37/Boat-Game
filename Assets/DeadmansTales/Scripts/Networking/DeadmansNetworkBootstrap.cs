using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeadmansTales.Networking
{
    /// <summary>
    /// Creates and configures the project-owned NGO NetworkManager after the
    /// initial scene loads. This replaces the deleted 3D template Core prefab.
    ///
    /// If a scene already provides a NetworkManager (for example the isolated
    /// technical runtime test), that manager is reused instead of duplicated.
    /// Network prefabs are supplied by NGO's generated DefaultNetworkPrefabs
    /// registry, so this bootstrap must not register the same prefab again.
    /// </summary>
    public static class DeadmansNetworkBootstrap
    {
        private const string SettingsResourcePath =
            "Networking/DeadmansNetworkBootstrapSettings";

        private const string ManagerObjectName =
            "[DMT] NetworkManager";

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.BeforeSceneLoad
        )]
        private static void InstallSceneBootstrap()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private static void HandleSceneLoaded(
            Scene scene,
            LoadSceneMode loadSceneMode
        )
        {
            EnsureNetworkManager();
        }

        private static void EnsureNetworkManager()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            NetworkManager[] managers =
                UnityEngine.Object.FindObjectsByType<NetworkManager>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None
                );

            if (networkManager == null && managers.Length > 0)
            {
                networkManager = managers[0];
            }

            foreach (NetworkManager candidate in managers)
            {
                if (candidate == networkManager)
                {
                    continue;
                }

                Debug.LogWarning(
                    "[Network Bootstrap] Removing a duplicate " +
                    $"NetworkManager from scene '{candidate.gameObject.scene.name}'.",
                    candidate
                );
                UnityEngine.Object.Destroy(candidate.gameObject);
            }

            if (networkManager == null)
            {
                GameObject managerObject = new GameObject(ManagerObjectName);
                UnityEngine.Object.DontDestroyOnLoad(managerObject);

                UnityTransport transport =
                    managerObject.AddComponent<UnityTransport>();

                networkManager =
                    managerObject.AddComponent<NetworkManager>();

                networkManager.NetworkConfig.NetworkTransport = transport;
            }

            // NGO locks most NetworkConfig values after listening begins.
            // The persistent manager was already configured in MainMenu, so
            // synchronized gameplay loads only need duplicate reconciliation.
            if (!networkManager.IsListening)
            {
                ConfigureNetworkManager(networkManager);
            }
        }

        private static void ConfigureNetworkManager(
            NetworkManager networkManager
        )
        {
            if (networkManager == null)
            {
                Debug.LogError(
                    "[Network Bootstrap] Could not create a NetworkManager."
                );
                return;
            }

            if (networkManager.NetworkConfig.NetworkTransport == null)
            {
                UnityTransport transport =
                    networkManager.GetComponent<UnityTransport>();

                if (transport == null)
                {
                    transport =
                        networkManager.gameObject.AddComponent<UnityTransport>();
                }

                networkManager.NetworkConfig.NetworkTransport = transport;
            }

            DeadmansNetworkBootstrapSettings settings =
                Resources.Load<DeadmansNetworkBootstrapSettings>(
                    SettingsResourcePath
                );

            if (settings == null)
            {
                Debug.LogError(
                    "[Network Bootstrap] Missing Resources settings asset at " +
                    $"'{SettingsResourcePath}'."
                );
                return;
            }

            if (settings.PlayerPrefab == null)
            {
                Debug.LogError(
                    "[Network Bootstrap] The custom 2D player prefab is not " +
                    "assigned in the bootstrap settings asset."
                );
                return;
            }

            if (settings.PlayerPrefab.GetComponent<NetworkObject>() == null)
            {
                Debug.LogError(
                    $"[Network Bootstrap] '{settings.PlayerPrefab.name}' does " +
                    "not have a NetworkObject component.",
                    settings.PlayerPrefab
                );
                return;
            }

            networkManager.NetworkConfig.PlayerPrefab =
                settings.PlayerPrefab;

            networkManager.NetworkConfig.EnableSceneManagement = true;
            networkManager.NetworkConfig.ForceSamePrefabs = true;
            networkManager.NetworkConfig.ConnectionApproval = false;
            networkManager.NetworkConfig.NetworkTopology =
                NetworkTopologyTypes.ClientServer;
            networkManager.NetworkConfig.UseCMBService = false;

            if (
                networkManager.GetComponent<NetworkPlayerSpawnCoordinator>() ==
                null
            )
            {
                networkManager.gameObject.AddComponent<
                    NetworkPlayerSpawnCoordinator
                >();
            }

            // Do not call NetworkManager.AddNetworkPrefab here. Unity NGO's
            // generated DefaultNetworkPrefabs registry already contains the
            // project network prefabs. Registering them again produces a
            // duplicate GlobalObjectIdHash error when the host starts.

            Debug.Log(
                "[Network Bootstrap] Project-owned NetworkManager ready.\n" +
                $"Player Prefab: {settings.PlayerPrefab.name}\n" +
                "Topology: ClientServer",
                networkManager
            );
        }
    }
}
