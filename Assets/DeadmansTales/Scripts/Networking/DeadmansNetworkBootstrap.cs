using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace DeadmansTales.Networking
{
    /// <summary>
    /// Creates and configures the project-owned NGO NetworkManager after the
    /// initial scene loads. This replaces the deleted 3D template Core prefab.
    ///
    /// If a scene already provides a NetworkManager (for example the isolated
    /// technical runtime test), that manager is reused instead of duplicated.
    /// </summary>
    public static class DeadmansNetworkBootstrap
    {
        private const string SettingsResourcePath =
            "Networking/DeadmansNetworkBootstrapSettings";

        private const string ManagerObjectName =
            "[DMT] NetworkManager";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureNetworkManager()
        {
            NetworkManager networkManager =
                UnityEngine.Object.FindFirstObjectByType<NetworkManager>();

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

            ConfigureNetworkManager(networkManager);
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

            networkManager.NetworkConfig.PlayerPrefab =
                settings.PlayerPrefab;

            networkManager.NetworkConfig.EnableSceneManagement = true;
            networkManager.NetworkConfig.ForceSamePrefabs = true;
            networkManager.NetworkConfig.ConnectionApproval = false;
            networkManager.NetworkConfig.AutoSpawnPlayerPrefabClientSide = true;

            RegisterNetworkPrefab(
                networkManager,
                settings.PlayerPrefab
            );

            foreach (
                GameObject additionalPrefab
                in settings.AdditionalNetworkPrefabs
            )
            {
                RegisterNetworkPrefab(
                    networkManager,
                    additionalPrefab
                );
            }

            Debug.Log(
                "[Network Bootstrap] Project-owned NetworkManager ready.\n" +
                $"Player Prefab: {settings.PlayerPrefab.name}",
                networkManager
            );
        }

        private static void RegisterNetworkPrefab(
            NetworkManager networkManager,
            GameObject prefab
        )
        {
            if (prefab == null)
            {
                return;
            }

            if (prefab.GetComponent<NetworkObject>() == null)
            {
                Debug.LogError(
                    $"[Network Bootstrap] '{prefab.name}' does not have a " +
                    "NetworkObject component and cannot be registered.",
                    prefab
                );
                return;
            }

            try
            {
                networkManager.AddNetworkPrefab(prefab);
            }
            catch (Exception exception)
            {
                // An existing scene NetworkManager may already contain the same
                // prefab. NGO rejects duplicate registrations, but that is safe.
                Debug.LogWarning(
                    $"[Network Bootstrap] Could not add '{prefab.name}' to " +
                    $"the runtime prefab list: {exception.Message}",
                    networkManager
                );
            }
        }
    }
}
