using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeadmansTales.Networking
{
    /// <summary>
    /// Assigns stable player slots and positions PlayerObjects only after NGO
    /// reports that every participant completed a synchronized scene load.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkManager))]
    public sealed class NetworkPlayerSpawnCoordinator : MonoBehaviour
    {
        private const int SupportedPlayers = 4;
        private const string SpawnPrefix = "PlayerSpawn_";
        private const string LobbySceneName = "Lobby_Island_2D";
        private const string BoatSceneName = "Boat_Gameplay_2D";

        private readonly Dictionary<ulong, int> clientSlots =
            new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, int> lastPlacedSceneHandles =
            new Dictionary<ulong, int>();

        private NetworkManager networkManager;
        private Coroutine pendingSpawnPass;
        private bool sceneCallbacksBound;

        private void Awake()
        {
            networkManager = GetComponent<NetworkManager>();
        }

        private void OnEnable()
        {
            networkManager ??= GetComponent<NetworkManager>();

            networkManager.OnServerStarted += HandleServerStarted;
            networkManager.OnServerStopped += HandleServerStopped;
            networkManager.OnClientConnectedCallback += HandleClientConnected;
            networkManager.OnClientDisconnectCallback +=
                HandleClientDisconnected;

            if (networkManager.IsServer)
            {
                HandleServerStarted();
            }
        }

        private void OnDisable()
        {
            if (networkManager == null)
            {
                return;
            }

            networkManager.OnServerStarted -= HandleServerStarted;
            networkManager.OnServerStopped -= HandleServerStopped;
            networkManager.OnClientConnectedCallback -= HandleClientConnected;
            networkManager.OnClientDisconnectCallback -=
                HandleClientDisconnected;

            UnbindSceneCallbacks();

            if (pendingSpawnPass != null)
            {
                StopCoroutine(pendingSpawnPass);
                pendingSpawnPass = null;
            }
        }

        private void HandleServerStarted()
        {
            if (!networkManager.IsServer)
            {
                return;
            }

            clientSlots.Clear();
            lastPlacedSceneHandles.Clear();
            EnsureConnectedClientSlots();
            BindSceneCallbacks();
            ScheduleSpawnPass(SceneManager.GetActiveScene().name);
        }

        private void HandleServerStopped(bool wasHost)
        {
            UnbindSceneCallbacks();
            clientSlots.Clear();
            lastPlacedSceneHandles.Clear();

            if (pendingSpawnPass != null)
            {
                StopCoroutine(pendingSpawnPass);
                pendingSpawnPass = null;
            }
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (!networkManager.IsServer)
            {
                return;
            }

            GetOrAssignSlot(clientId);
            ScheduleSpawnPass(SceneManager.GetActiveScene().name);
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (networkManager.IsServer)
            {
                clientSlots.Remove(clientId);
                lastPlacedSceneHandles.Remove(clientId);
            }
        }

        private void BindSceneCallbacks()
        {
            if (
                sceneCallbacksBound ||
                networkManager.SceneManager == null
            )
            {
                return;
            }

            networkManager.SceneManager.OnLoadEventCompleted +=
                HandleLoadEventCompleted;
            sceneCallbacksBound = true;
        }

        private void UnbindSceneCallbacks()
        {
            if (
                !sceneCallbacksBound ||
                networkManager == null ||
                networkManager.SceneManager == null
            )
            {
                sceneCallbacksBound = false;
                return;
            }

            networkManager.SceneManager.OnLoadEventCompleted -=
                HandleLoadEventCompleted;
            sceneCallbacksBound = false;
        }

        private void HandleLoadEventCompleted(
            string sceneName,
            LoadSceneMode loadSceneMode,
            List<ulong> clientsCompleted,
            List<ulong> clientsTimedOut
        )
        {
            if (!networkManager.IsServer)
            {
                return;
            }

            if (clientsTimedOut != null && clientsTimedOut.Count > 0)
            {
                Debug.LogWarning(
                    $"[Player Spawn] {clientsTimedOut.Count} client(s) timed " +
                    $"out while loading {sceneName}.",
                    this
                );
            }

            ScheduleSpawnPass(sceneName);
        }

        private void ScheduleSpawnPass(string sceneName)
        {
            if (!IsGameplayScene(sceneName) || !networkManager.IsServer)
            {
                return;
            }

            if (pendingSpawnPass != null)
            {
                StopCoroutine(pendingSpawnPass);
            }

            pendingSpawnPass = StartCoroutine(
                SpawnPlayersWhenReady(sceneName)
            );
        }

        private IEnumerator SpawnPlayersWhenReady(string sceneName)
        {
            // A late join can connect before its PlayerObject is available on
            // the server. NGO load completion normally needs no wait, but this
            // bounded retry also covers direct scene play and late joins.
            for (int attempt = 0; attempt < 120; attempt++)
            {
                if (!networkManager.IsServer)
                {
                    pendingSpawnPass = null;
                    yield break;
                }

                if (AllConnectedPlayersExist())
                {
                    break;
                }

                yield return null;
            }

            yield return new WaitForFixedUpdate();

            if (
                !networkManager.IsServer ||
                SceneManager.GetActiveScene().name != sceneName
            )
            {
                pendingSpawnPass = null;
                yield break;
            }

            PlaceConnectedPlayers(sceneName);
            pendingSpawnPass = null;
        }

        private void PlaceConnectedPlayers(string sceneName)
        {
            Dictionary<int, PlayerSpawnPoint2D> spawnPoints =
                FindSpawnPoints(sceneName);
            int sceneHandle = SceneManager.GetActiveScene().handle;

            EnsureConnectedClientSlots();

            foreach (NetworkClient client in networkManager.ConnectedClientsList)
            {
                if (
                    lastPlacedSceneHandles.TryGetValue(
                        client.ClientId,
                        out int placedSceneHandle
                    ) &&
                    placedSceneHandle == sceneHandle
                )
                {
                    continue;
                }

                int slot = GetOrAssignSlot(client.ClientId);

                if (slot < 0 || !spawnPoints.TryGetValue(slot, out var marker))
                {
                    Debug.LogError(
                        $"[Player Spawn] No {SpawnPrefix}{slot} marker exists " +
                        $"in {sceneName} for client {client.ClientId}.",
                        this
                    );
                    continue;
                }

                if (client.PlayerObject == null)
                {
                    Debug.LogError(
                        $"[Player Spawn] Client {client.ClientId} has no " +
                        "PlayerObject after scene synchronization.",
                        this
                    );
                    continue;
                }

                TopDownNetworkPlayer2D player =
                    client.PlayerObject.GetComponent<TopDownNetworkPlayer2D>();

                if (player == null)
                {
                    Debug.LogError(
                        $"[Player Spawn] Client {client.ClientId}'s " +
                        "PlayerObject has no TopDownNetworkPlayer2D.",
                        client.PlayerObject
                    );
                    continue;
                }

                Vector2 spawnPosition = marker.transform.position;

                if (player.TeleportToSpawnServer(spawnPosition))
                {
                    lastPlacedSceneHandles[client.ClientId] = sceneHandle;
                    Debug.Log(
                        $"[Player Spawn] Client {client.ClientId} assigned " +
                        $"to {marker.name} at {spawnPosition} in {sceneName}.",
                        player
                    );
                }
            }
        }

        private Dictionary<int, PlayerSpawnPoint2D> FindSpawnPoints(
            string sceneName
        )
        {
            var result = new Dictionary<int, PlayerSpawnPoint2D>();
            PlayerSpawnPoint2D[] markers =
                FindObjectsByType<PlayerSpawnPoint2D>(
                    FindObjectsSortMode.None
                );

            foreach (PlayerSpawnPoint2D marker in markers)
            {
                if (
                    marker.gameObject.scene.name != sceneName ||
                    !TryGetSpawnIndex(marker.name, out int index) ||
                    index < 0 ||
                    index >= SupportedPlayers
                )
                {
                    continue;
                }

                if (!result.TryAdd(index, marker))
                {
                    Debug.LogError(
                        $"[Player Spawn] Duplicate {SpawnPrefix}{index} " +
                        $"markers exist in {sceneName}.",
                        marker
                    );
                }
            }

            return result;
        }

        private void EnsureConnectedClientSlots()
        {
            foreach (NetworkClient client in networkManager.ConnectedClientsList)
            {
                GetOrAssignSlot(client.ClientId);
            }
        }

        private int GetOrAssignSlot(ulong clientId)
        {
            if (clientSlots.TryGetValue(clientId, out int existingSlot))
            {
                return existingSlot;
            }

            for (int slot = 0; slot < SupportedPlayers; slot++)
            {
                if (!clientSlots.ContainsValue(slot))
                {
                    clientSlots.Add(clientId, slot);
                    return slot;
                }
            }

            Debug.LogError(
                $"[Player Spawn] No free spawn slot remains for client " +
                $"{clientId}. Maximum supported players: {SupportedPlayers}.",
                this
            );
            return -1;
        }

        private bool AllConnectedPlayersExist()
        {
            foreach (NetworkClient client in networkManager.ConnectedClientsList)
            {
                if (client.PlayerObject == null)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryGetSpawnIndex(
            string markerName,
            out int index
        )
        {
            index = -1;

            return
                markerName.StartsWith(SpawnPrefix) &&
                int.TryParse(
                    markerName.Substring(SpawnPrefix.Length),
                    out index
                );
        }

        private static bool IsGameplayScene(string sceneName)
        {
            return
                sceneName == LobbySceneName ||
                sceneName == BoatSceneName;
        }
    }
}
