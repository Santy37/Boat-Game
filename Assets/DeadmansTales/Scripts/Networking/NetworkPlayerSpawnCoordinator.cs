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
        private const string IslandStageSceneName =
            "Island_After_Ocean_01_2D";
        private const string ShopSceneName = "Island_Shop_2D";

        /// <summary>
        /// Scenes this coordinator positions players in.
        ///
        /// A scene missing from this list fails SILENTLY and in the worst
        /// possible way: no spawn pass is scheduled, so not even the "no
        /// marker found" error fires, and NGO leaves every player standing
        /// at the player prefab's own origin — off the island, in the sea.
        /// Any new playable scene must be added here as well as to Build
        /// Settings and the menu.
        /// </summary>
        private static readonly string[] GameplaySceneNames =
        {
            LobbySceneName,
            BoatSceneName,
            IslandStageSceneName,
            ShopSceneName,
        };

        private readonly Dictionary<ulong, int> clientSlots =
            new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, int> lastPlacedSceneHandles =
            new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, string> readySceneNames =
            new Dictionary<ulong, string>();
        private readonly HashSet<ulong> synchronizingClients =
            new HashSet<ulong>();

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
            readySceneNames.Clear();
            synchronizingClients.Clear();
            EnsureConnectedClientSlots();
            BindSceneCallbacks();

            Scene activeScene = SceneManager.GetActiveScene();
            if (
                IsGameplayScene(activeScene.name) &&
                networkManager.ConnectedClients.ContainsKey(
                    networkManager.LocalClientId
                )
            )
            {
                readySceneNames[networkManager.LocalClientId] =
                    activeScene.name;
                ScheduleSpawnPass(activeScene.name);
            }
        }

        private void HandleServerStopped(bool wasHost)
        {
            UnbindSceneCallbacks();
            clientSlots.Clear();
            lastPlacedSceneHandles.Clear();
            readySceneNames.Clear();
            synchronizingClients.Clear();

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
            RefreshRunPlayerCount();

            string activeSceneName = SceneManager.GetActiveScene().name;
            if (
                clientId != networkManager.LocalClientId &&
                IsGameplayScene(activeSceneName)
            )
            {
                // Auto-created PlayerObjects exist before a late joiner has
                // synchronized the active gameplay scene. Set the server-owned
                // transform now so NGO's synchronization snapshot contains the
                // real spawn, never the prefab origin. This does not mark the
                // player placed; completion still does that below.
                StartCoroutine(
                    PrepositionLateJoinBeforeSynchronization(
                        clientId,
                        activeSceneName
                    )
                );
            }

            // The server/host already owns its active scene. Remote clients
            // must wait for LoadComplete or SynchronizeComplete before they
            // are eligible for authoritative placement.
            if (clientId == networkManager.LocalClientId)
            {
                string sceneName = SceneManager.GetActiveScene().name;
                if (IsGameplayScene(sceneName))
                {
                    readySceneNames[clientId] = sceneName;
                    ScheduleSpawnPass(sceneName);
                }
            }
        }

        private IEnumerator PrepositionLateJoinBeforeSynchronization(
            ulong clientId,
            string sceneName
        )
        {
            NetworkClient client = null;

            for (int attempt = 0; attempt < 120; attempt++)
            {
                if (
                    !networkManager.IsServer ||
                    !networkManager.ConnectedClients.TryGetValue(
                        clientId,
                        out client
                    )
                )
                {
                    yield break;
                }

                if (client.PlayerObject != null)
                {
                    break;
                }

                yield return null;
            }

            if (
                client?.PlayerObject == null ||
                SceneManager.GetActiveScene().name != sceneName
            )
            {
                yield break;
            }

            Dictionary<int, PlayerSpawnPoint2D> spawnPoints =
                FindSpawnPoints(sceneName);
            int slot = GetOrAssignSlot(clientId);

            if (!spawnPoints.TryGetValue(slot, out PlayerSpawnPoint2D marker))
            {
                yield break;
            }

            TopDownNetworkPlayer2D player =
                client.PlayerObject.GetComponent<TopDownNetworkPlayer2D>();

            if (
                player != null &&
                player.TeleportToSpawnServer(marker.transform.position)
            )
            {
                Debug.Log(
                    $"[Player Spawn] Prepositioned late client {clientId} " +
                    $"at {marker.name} before scene synchronization.",
                    player
                );
            }
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (networkManager.IsServer)
            {
                clientSlots.Remove(clientId);
                lastPlacedSceneHandles.Remove(clientId);
                readySceneNames.Remove(clientId);
                synchronizingClients.Remove(clientId);
                RefreshRunPlayerCount();
            }
        }

        private static void RefreshRunPlayerCount()
        {
            NetworkRunState runState = NetworkRunState.Instance;
            if (runState != null && runState.IsSpawned && runState.IsServer)
            {
                runState.RefreshPlayerCountServer();
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
            networkManager.SceneManager.OnLoad += HandleLoadStarted;
            networkManager.SceneManager.OnLoadComplete += HandleLoadComplete;
            networkManager.SceneManager.OnSynchronize +=
                HandleSynchronizeStarted;
            networkManager.SceneManager.OnSynchronizeComplete +=
                HandleSynchronizeComplete;
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
            networkManager.SceneManager.OnLoad -= HandleLoadStarted;
            networkManager.SceneManager.OnLoadComplete -= HandleLoadComplete;
            networkManager.SceneManager.OnSynchronize -=
                HandleSynchronizeStarted;
            networkManager.SceneManager.OnSynchronizeComplete -=
                HandleSynchronizeComplete;
            sceneCallbacksBound = false;
        }

        private void HandleLoadStarted(
            ulong clientId,
            string sceneName,
            LoadSceneMode loadSceneMode,
            AsyncOperation asyncOperation
        )
        {
            if (!networkManager.IsServer || !IsGameplayScene(sceneName))
            {
                return;
            }

            readySceneNames.Remove(clientId);
            lastPlacedSceneHandles.Remove(clientId);
        }

        private void HandleLoadComplete(
            ulong clientId,
            string sceneName,
            LoadSceneMode loadSceneMode
        )
        {
            if (
                !networkManager.IsServer ||
                !IsGameplayScene(sceneName) ||
                synchronizingClients.Contains(clientId)
            )
            {
                return;
            }

            MarkClientReady(clientId, sceneName);
        }

        private void HandleSynchronizeStarted(ulong clientId)
        {
            if (!networkManager.IsServer)
            {
                return;
            }

            synchronizingClients.Add(clientId);
            readySceneNames.Remove(clientId);
            lastPlacedSceneHandles.Remove(clientId);
        }

        private void HandleSynchronizeComplete(ulong clientId)
        {
            if (!networkManager.IsServer)
            {
                return;
            }

            synchronizingClients.Remove(clientId);

            string sceneName = SceneManager.GetActiveScene().name;
            if (IsGameplayScene(sceneName))
            {
                MarkClientReady(clientId, sceneName);
            }
        }

        private void MarkClientReady(ulong clientId, string sceneName)
        {
            if (!networkManager.ConnectedClients.ContainsKey(clientId))
            {
                return;
            }

            readySceneNames[clientId] = sceneName;
            ScheduleSpawnPass(sceneName);
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

            if (clientsCompleted != null)
            {
                foreach (ulong clientId in clientsCompleted)
                {
                    if (!synchronizingClients.Contains(clientId))
                    {
                        readySceneNames[clientId] = sceneName;
                    }
                }
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

                if (AllReadyPlayersExist(sceneName))
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

            int placedPlayerCount = PlaceConnectedPlayers(sceneName);

            NetworkRunState runState = NetworkRunState.Instance;
            if (
                placedPlayerCount > 0 &&
                runState != null &&
                runState.IsSpawned
            )
            {
                runState.SetStatusServer(NetworkRunStatus.Playing);
            }

            pendingSpawnPass = null;
        }

        private int PlaceConnectedPlayers(string sceneName)
        {
            Dictionary<int, PlayerSpawnPoint2D> spawnPoints =
                FindSpawnPoints(sceneName);
            int sceneHandle = SceneManager.GetActiveScene().handle;
            int placedPlayerCount = 0;

            EnsureConnectedClientSlots();

            foreach (NetworkClient client in networkManager.ConnectedClientsList)
            {
                if (
                    !readySceneNames.TryGetValue(
                        client.ClientId,
                        out string readySceneName
                    ) ||
                    readySceneName != sceneName
                )
                {
                    continue;
                }

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
                    placedPlayerCount++;
                    Debug.Log(
                        $"[Player Spawn] Client {client.ClientId} assigned " +
                        $"to {marker.name} at {spawnPosition} in {sceneName}.",
                        player
                    );
                }
            }

            return placedPlayerCount;
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

        private bool AllReadyPlayersExist(string sceneName)
        {
            bool foundReadyPlayer = false;

            foreach (NetworkClient client in networkManager.ConnectedClientsList)
            {
                if (
                    !readySceneNames.TryGetValue(
                        client.ClientId,
                        out string readySceneName
                    ) ||
                    readySceneName != sceneName
                )
                {
                    continue;
                }

                foundReadyPlayer = true;
                if (client.PlayerObject == null)
                {
                    return false;
                }
            }

            return foundReadyPlayer;
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
            return System.Array.IndexOf(GameplaySceneNames, sceneName) >= 0;
        }
    }
}
