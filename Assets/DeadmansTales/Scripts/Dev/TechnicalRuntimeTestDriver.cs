using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DeadmansTales.Configuration;
using DeadmansTales.Networking;
using DeadmansTales.Telemetry;
using DeadmansTales.WorldGeneration;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Isolated runtime verification for the technical multiplayer foundation.
///
/// This component is only placed in the generated technical test scene. It
/// starts a local host, waits for the persistent network services, initializes
/// a deterministic run, and verifies the synchronized run/config/seed state.
/// It does not depend on the game UI, player mechanics, islands, or combat.
/// </summary>
public sealed class TechnicalRuntimeTestDriver : MonoBehaviour
{
    private const string LobbySceneName =
        "Lobby_Island_2D";

    private const string BoatSceneName =
        "Boat_Gameplay_2D";

    private const string IslandSceneName =
        "Island_After_Ocean_01_2D";

    [SerializeField]
    private int testSeed = 24680;

    [SerializeField]
    [Min(1f)]
    private float timeoutSeconds = 10f;

    private string currentStatus = "Waiting to enter Play Mode...";
    private bool testFinished;
    private bool testPassed;
    private readonly List<string> unexpectedRuntimeErrors =
        new List<string>();
    private readonly HashSet<string> completedNetworkSceneLoads =
        new HashSet<string>();
    private NetworkManager observedNetworkManager;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        Application.logMessageReceived += HandleRuntimeLog;
    }

    private void OnDestroy()
    {
        Application.logMessageReceived -= HandleRuntimeLog;

        if (
            observedNetworkManager != null &&
            observedNetworkManager.SceneManager != null
        )
        {
            observedNetworkManager.SceneManager.OnLoadEventCompleted -=
                HandleNetworkLoadCompleted;
        }
    }

    private IEnumerator Start()
    {
        yield return null;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            Fail("No NetworkManager exists in the technical test scene.");
            yield break;
        }

        if (IsClientTestRole())
        {
            yield return RunClientTest(networkManager);
            yield break;
        }

        currentStatus = "Starting local technical host...";

        if (!networkManager.IsListening && !networkManager.StartHost())
        {
            Fail("NetworkManager.StartHost returned false.");
            yield break;
        }

        observedNetworkManager = networkManager;
        networkManager.SceneManager.OnLoadEventCompleted +=
            HandleNetworkLoadCompleted;

        currentStatus = "Waiting for network services to spawn...";

        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (
            Time.realtimeSinceStartup < deadline &&
            !AreNetworkServicesReady()
        )
        {
            yield return null;
        }

        if (!AreNetworkServicesReady())
        {
            Fail(
                "Timed out waiting for NetworkRunState and " +
                "NetworkRunConfigAuthority."
            );
            yield break;
        }

        int expectedParticipants = GetExpectedParticipantCount();
        int initialParticipantTarget = IsLateJoinHostRole()
            ? 1
            : expectedParticipants;
        currentStatus =
            $"Waiting for {initialParticipantTarget} initial participant(s)...";
        deadline = Time.realtimeSinceStartup + timeoutSeconds * 2f;

        while (
            Time.realtimeSinceStartup < deadline &&
            networkManager.ConnectedClientsIds.Count < initialParticipantTarget
        )
        {
            yield return null;
        }

        if (networkManager.ConnectedClientsIds.Count < initialParticipantTarget)
        {
            Fail(
                "Timed out waiting for the requested host/client count. " +
                $"Expected {initialParticipantTarget}, connected " +
                $"{networkManager.ConnectedClientsIds.Count}."
            );
            yield break;
        }

        NetworkRunState runState = NetworkRunState.Instance;
        NetworkRunConfigAuthority configAuthority =
            NetworkRunConfigAuthority.Instance;

        runState.InitializeNewRunServer(
            testSeed,
            configAuthority.ConfigId,
            configAuthority.ConfigVersion,
            1
        );

        StageSeedProvider seedProvider =
            FindFirstObjectByType<StageSeedProvider>();

        if (seedProvider == null)
        {
            Fail("No StageSeedProvider exists in the technical test scene.");
            yield break;
        }

        currentStatus = "Waiting for stage seed context...";
        deadline = Time.realtimeSinceStartup + timeoutSeconds;

        while (
            Time.realtimeSinceStartup < deadline &&
            !seedProvider.IsReady
        )
        {
            yield return null;
        }

        if (!seedProvider.TryGetContext(out StageSeedContext context))
        {
            Fail("StageSeedProvider never produced a valid context.");
            yield break;
        }

        if (runState.MasterSeed.Value != testSeed)
        {
            Fail("The synchronized master seed does not match the test seed.");
            yield break;
        }

        if (runState.CurrentStage.Value != 1)
        {
            Fail("The synchronized stage index was not initialized to one.");
            yield break;
        }

        if (runState.Status.Value != NetworkRunStatus.Loading)
        {
            Fail("The synchronized run status was not set to Loading.");
            yield break;
        }

        if (context.MasterSeed != testSeed || context.StageIndex != 1)
        {
            Fail("The stage seed context does not match the run state.");
            yield break;
        }

        if (context.ConfigId != configAuthority.ConfigId)
        {
            Fail("The stage context config ID does not match the host config.");
            yield break;
        }

        PlaytestEventLogger logger = PlaytestEventLogger.Instance;
        if (logger == null || !logger.IsSessionOpen)
        {
            Fail("The playtest logger did not open a telemetry session.");
            yield break;
        }

        int enemySeed = seedProvider.DeriveSeed("EnemySpawns");
        int lootSeed = seedProvider.DeriveSeed("Loot");
        if (enemySeed == lootSeed)
        {
            Fail("Enemy and loot runtime streams unexpectedly share a seed.");
            yield break;
        }

        yield return VerifyLobbyRuntime(networkManager, runState);

        if (testFinished)
        {
            yield break;
        }

        yield return TransitionThroughProductionBoatPortal(
            networkManager,
            runState
        );

        if (testFinished)
        {
            yield break;
        }

        yield return VerifyIslandRuntime(networkManager, runState);

        if (testFinished)
        {
            yield break;
        }

        if (!FailOnUnexpectedRuntimeErrors("the host smoke test"))
        {
            Pass(
                "Local host, synchronized run state, lobby-to-boat-to-island " +
                "production portal loading, server-generated island content, " +
                "and authoritative player spawning are all ready."
            );
        }
    }

    private IEnumerator VerifyLobbyRuntime(
        NetworkManager networkManager,
        NetworkRunState runState
    )
    {
        if (!Application.CanStreamedLevelBeLoaded(LobbySceneName))
        {
            Fail("The lobby island is not present in Build Settings.");
            yield break;
        }

        currentStatus = "Loading the lobby island through NGO...";
        runState.SetStatusServer(NetworkRunStatus.Loading);
        completedNetworkSceneLoads.Remove(LobbySceneName);

        SceneEventProgressStatus loadStatus =
            networkManager.SceneManager.LoadScene(
                LobbySceneName,
                LoadSceneMode.Single
            );

        if (loadStatus != SceneEventProgressStatus.Started)
        {
            Fail($"NGO rejected the lobby scene load: {loadStatus}.");
            yield break;
        }

        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (
            Time.realtimeSinceStartup < deadline &&
            (
                SceneManager.GetActiveScene().name != LobbySceneName ||
                !completedNetworkSceneLoads.Contains(LobbySceneName)
            )
        )
        {
            yield return null;
        }

        if (
            SceneManager.GetActiveScene().name != LobbySceneName ||
            !completedNetworkSceneLoads.Contains(LobbySceneName)
        )
        {
            Fail("Timed out waiting for the synchronized lobby scene load.");
            yield break;
        }

        currentStatus =
            "Checking lobby NetworkObjects and authoritative spawn...";
        NetworkObject playerObject = null;
        int spawnedEnemyCount = 0;
        deadline = Time.realtimeSinceStartup + timeoutSeconds;

        while (Time.realtimeSinceStartup < deadline)
        {
            playerObject = networkManager.LocalClient?.PlayerObject;
            spawnedEnemyCount = FindObjectsByType<Enemy>(
                FindObjectsSortMode.None
            ).Count(enemy => enemy.IsSpawned);

            if (
                playerObject != null &&
                spawnedEnemyCount == 3 &&
                runState.Status.Value == NetworkRunStatus.Playing
            )
            {
                break;
            }

            yield return null;
        }

        if (spawnedEnemyCount != 3)
        {
            Fail(
                "The lobby did not server-spawn all three runtime enemy " +
                $"prefabs. Spawned: {spawnedEnemyCount}."
            );
            yield break;
        }

        if (playerObject == null)
        {
            Fail("The host PlayerObject was missing in the lobby.");
            yield break;
        }

        Vector2 expectedSpawn = new Vector2(0f, 12f);
        if (
            Vector2.Distance(
                playerObject.transform.position,
                expectedSpawn
            ) > 0.35f
        )
        {
            Fail(
                "The lobby host did not arrive at PlayerSpawn_0. " +
                $"Expected {expectedSpawn}, got " +
                $"{(Vector2)playerObject.transform.position}."
            );
            yield break;
        }
    }

    private IEnumerator TransitionThroughProductionBoatPortal(
        NetworkManager networkManager,
        NetworkRunState runState
    )
    {
        if (!Application.CanStreamedLevelBeLoaded(BoatSceneName))
        {
            Fail("Boat_Gameplay_2D is not present in the smoke build.");
            yield break;
        }

        if (!Application.CanStreamedLevelBeLoaded(IslandSceneName))
        {
            Fail("The generated island is not present in the smoke build.");
            yield break;
        }

        currentStatus = "Loading Boat_Gameplay_2D through NGO...";
        runState.SetStatusServer(NetworkRunStatus.Loading);
        completedNetworkSceneLoads.Remove(BoatSceneName);

        SceneEventProgressStatus boatLoadStatus =
            networkManager.SceneManager.LoadScene(
                BoatSceneName,
                LoadSceneMode.Single
            );

        if (boatLoadStatus != SceneEventProgressStatus.Started)
        {
            Fail($"NGO rejected the boat scene load: {boatLoadStatus}.");
            yield break;
        }

        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (
            Time.realtimeSinceStartup < deadline &&
            (
                SceneManager.GetActiveScene().name != BoatSceneName ||
                !completedNetworkSceneLoads.Contains(BoatSceneName)
            ) &&
            unexpectedRuntimeErrors.Count == 0
        )
        {
            yield return null;
        }

        if (FailOnUnexpectedRuntimeErrors("the synchronized boat scene load"))
        {
            yield break;
        }

        if (
            SceneManager.GetActiveScene().name != BoatSceneName ||
            !completedNetworkSceneLoads.Contains(BoatSceneName)
        )
        {
            Fail("Timed out waiting for the synchronized boat scene load.");
            yield break;
        }

        currentStatus = "Waiting for the production post-ocean portal...";
        NetworkObject playerObject = null;
        NetworkStagePortal portal = null;
        NetworkInteractionController2D interactionController = null;
        TopDownNetworkPlayer2D playerMovement = null;
        deadline = Time.realtimeSinceStartup + timeoutSeconds;

        while (
            Time.realtimeSinceStartup < deadline &&
            unexpectedRuntimeErrors.Count == 0
        )
        {
            playerObject = networkManager.LocalClient?.PlayerObject;
            portal = FindObjectsByType<NetworkStagePortal>(
                FindObjectsSortMode.None
            ).FirstOrDefault(candidate =>
                candidate.name == "PostOceanIslandPortal");

            interactionController = playerObject != null
                ? playerObject.GetComponent<NetworkInteractionController2D>()
                : null;
            playerMovement = playerObject != null
                ? playerObject.GetComponent<TopDownNetworkPlayer2D>()
                : null;

            if (
                playerObject != null &&
                playerObject.IsSpawned &&
                interactionController != null &&
                playerMovement != null &&
                portal != null &&
                portal.IsInteractionAvailable &&
                runState.Status.Value == NetworkRunStatus.Playing
            )
            {
                break;
            }

            yield return null;
        }

        if (FailOnUnexpectedRuntimeErrors("boat portal initialization"))
        {
            yield break;
        }

        if (
            playerObject == null ||
            !playerObject.IsSpawned ||
            interactionController == null ||
            playerMovement == null ||
            portal == null ||
            !portal.IsInteractionAvailable
        )
        {
            Fail(
                "The production PostOceanIslandPortal or host interaction " +
                "components did not spawn in Boat_Gameplay_2D."
            );
            yield break;
        }

        if (!playerMovement.TeleportToSpawnServer(portal.InteractionPoint))
        {
            Fail("The host could not move into range of the production portal.");
            yield break;
        }

        yield return null;

        currentStatus = "Using the production portal to load the island...";
        completedNetworkSceneLoads.Remove(IslandSceneName);

        bool interactionRequested = false;
        System.Exception interactionException = null;
        try
        {
            interactionRequested =
                interactionController.RequestInteraction(portal);
        }
        catch (System.Exception exception)
        {
            interactionException = exception;
        }

        if (interactionException != null)
        {
            Fail(
                "The production portal interaction threw an exception: " +
                interactionException
            );
            yield break;
        }

        if (!interactionRequested)
        {
            Fail("The host could not request the production portal interaction.");
            yield break;
        }

        deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (
            Time.realtimeSinceStartup < deadline &&
            (
                SceneManager.GetActiveScene().name != IslandSceneName ||
                !completedNetworkSceneLoads.Contains(IslandSceneName)
            ) &&
            unexpectedRuntimeErrors.Count == 0
        )
        {
            yield return null;
        }

        if (FailOnUnexpectedRuntimeErrors("the production portal transition"))
        {
            yield break;
        }

        if (
            SceneManager.GetActiveScene().name != IslandSceneName ||
            !completedNetworkSceneLoads.Contains(IslandSceneName)
        )
        {
            Fail(
                "The production PostOceanIslandPortal did not complete the " +
                "synchronized island load."
            );
            yield break;
        }

        if (runState.CurrentStage.Value != 2)
        {
            Fail(
                "The production portal did not advance the synchronized run " +
                $"to stage 2. Current stage: {runState.CurrentStage.Value}."
            );
        }
    }

    private IEnumerator VerifyIslandRuntime(
        NetworkManager networkManager,
        NetworkRunState runState
    )
    {
        if (SceneManager.GetActiveScene().name != IslandSceneName)
        {
            Fail("The island runtime check started outside the island scene.");
            yield break;
        }

        currentStatus = "Waiting for island generation and player spawn...";
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        SeededIslandContentGenerator generator = null;
        NetworkObject playerObject = null;

        while (Time.realtimeSinceStartup < deadline)
        {
            generator ??=
                FindFirstObjectByType<SeededIslandContentGenerator>();
            playerObject = networkManager.LocalClient?.PlayerObject;

            if (
                generator != null &&
                generator.GenerationComplete &&
                playerObject != null &&
                runState.Status.Value == NetworkRunStatus.Playing
            )
            {
                break;
            }

            yield return null;
        }

        if (generator == null || !generator.GenerationComplete)
        {
            Fail("The island's server-authoritative generation timed out.");
            yield break;
        }

        if (playerObject == null)
        {
            Fail("The host PlayerObject was missing after island loading.");
            yield break;
        }

        Vector2 expectedSpawn = new Vector2(-1.5f, -10f);
        float spawnError = Vector2.Distance(
            playerObject.transform.position,
            expectedSpawn
        );

        if (spawnError > 0.35f)
        {
            Fail(
                "The host did not arrive at PlayerSpawn_0. " +
                $"Expected {expectedSpawn}, got " +
                $"{(Vector2)playerObject.transform.position}."
            );
            yield break;
        }

        int enemyCount = FindObjectsByType<Enemy>(
            FindObjectsSortMode.None
        ).Count(enemy => enemy.IsSpawned);
        int rewardCount = FindObjectsByType<NetworkRewardChest>(
            FindObjectsSortMode.None
        ).Count(chest => chest.IsSpawned);

        if (enemyCount < 8 || enemyCount > 12)
        {
            Fail($"Enemy budget produced {enemyCount}; expected 8-12.");
            yield break;
        }

        if (rewardCount < 3 || rewardCount > 4)
        {
            Fail($"Reward budget produced {rewardCount}; expected 3-4.");
            yield break;
        }

        if (generator.SpawnedObjectCount != enemyCount + rewardCount)
        {
            Fail(
                "The island generator's spawned-object count does not " +
                "match the live network objects."
            );
            yield break;
        }

        if (!ValidateCollisionTilemaps())
        {
            yield break;
        }

        yield return VerifyLocalPlayerViewport(playerObject);
        if (testFinished)
        {
            yield break;
        }

        int expectedParticipants = GetExpectedParticipantCount();
        if (IsLateJoinHostRole() && expectedParticipants > 1)
        {
            currentStatus = "Island ready; waiting for a late-join client...";
            Debug.Log("[Technical Runtime Test] LATE_JOIN_READY");

            deadline = Time.realtimeSinceStartup + timeoutSeconds * 2f;
            while (
                Time.realtimeSinceStartup < deadline &&
                networkManager.ConnectedClientsIds.Count < expectedParticipants
            )
            {
                yield return null;
            }

            if (
                networkManager.ConnectedClientsIds.Count < expectedParticipants
            )
            {
                Fail("The requested late-join client never connected.");
                yield break;
            }
        }

        if (expectedParticipants > 1)
        {
            // Let the remote peer record the complete initial object set before
            // the host kills one enemy for the replicated-despawn assertion.
            yield return new WaitForSecondsRealtime(3f);
        }

        yield return VerifyServerAuthoritativeAttack(
            playerObject,
            generator,
            enemyCount
        );
        if (testFinished)
        {
            yield break;
        }

        if (expectedParticipants > 1)
        {
            // Give the remote standalone client time to observe that exact
            // NetworkObject disappearing before the host exits.
            yield return new WaitForSecondsRealtime(2f);
        }
    }

    private IEnumerator VerifyServerAuthoritativeAttack(
        NetworkObject playerObject,
        SeededIslandContentGenerator generator,
        int initialEnemyCount
    )
    {
        PlayerAttack attack = playerObject.GetComponent<PlayerAttack>();
        Enemy target = FindObjectsByType<Enemy>(FindObjectsSortMode.None)
            .FirstOrDefault(enemy => enemy.IsAlive);

        if (attack == null || target == null)
        {
            Fail("Could not find a live player attack and enemy to validate.");
            yield break;
        }

        EnemyAI enemyAI = target.GetComponent<EnemyAI>();
        if (enemyAI != null)
        {
            enemyAI.enabled = false;
        }

        Vector2 hitPosition =
            (Vector2)playerObject.transform.position + new Vector2(0f, 0.35f);
        Rigidbody2D targetBody = target.GetComponent<Rigidbody2D>();

        if (targetBody != null)
        {
            targetBody.position = hitPosition;
            targetBody.linearVelocity = Vector2.zero;
        }
        else
        {
            target.transform.position = hitPosition;
        }

        Physics2D.SyncTransforms();
        float healthBeforeAttack = target.CurrentHealth.Value;

        if (!attack.TryAttack())
        {
            Fail("The host player's first attack was rejected locally.");
            yield break;
        }

        yield return null;
        yield return new WaitForFixedUpdate();

        if (target.CurrentHealth.Value >= healthBeforeAttack)
        {
            Fail(
                "The anticipated player swing did not produce " +
                "server-authoritative enemy damage."
            );
            yield break;
        }

        ulong targetNetworkObjectId = target.NetworkObjectId;
        int initialGeneratedCount = generator.SpawnedObjectCount;
        NetworkStagePortal portal =
            FindFirstObjectByType<NetworkStagePortal>();
        int initialPortalEnemyCount = portal?.RemainingEnemies ?? -1;

        if (!target.TakeDamage(target.MaximumHealth))
        {
            Fail("The server rejected lethal damage to the test enemy.");
            yield break;
        }

        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (
            Time.realtimeSinceStartup < deadline &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.ContainsKey(
                targetNetworkObjectId
            )
        )
        {
            yield return null;
        }

        if (
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.ContainsKey(
                targetNetworkObjectId
            )
        )
        {
            Fail("The dead enemy NetworkObject never despawned.");
            yield break;
        }

        int remainingEnemyCount = FindObjectsByType<Enemy>(
            FindObjectsSortMode.None
        ).Count(enemy => enemy.IsSpawned);

        if (remainingEnemyCount != initialEnemyCount - 1)
        {
            Fail(
                "Enemy death did not remove exactly one live enemy. " +
                $"Before: {initialEnemyCount}, after: {remainingEnemyCount}."
            );
            yield break;
        }

        if (generator.SpawnedObjectCount != initialGeneratedCount - 1)
        {
            Fail(
                "The island generator retained a stale dead-enemy reference."
            );
            yield break;
        }

        if (
            portal == null ||
            portal.RemainingEnemies != initialPortalEnemyCount - 1
        )
        {
            Fail("The stage portal retained a dead enemy in its remaining count.");
        }
    }

    private IEnumerator RunClientTest(NetworkManager networkManager)
    {
        currentStatus = "Starting local technical client...";

        if (!networkManager.IsListening && !networkManager.StartClient())
        {
            Fail("NetworkManager.StartClient returned false.");
            yield break;
        }

        float deadline = Time.realtimeSinceStartup + timeoutSeconds * 2f;
        while (
            Time.realtimeSinceStartup < deadline &&
            (
                !networkManager.IsConnectedClient ||
                NetworkRunState.Instance == null ||
                !NetworkRunState.Instance.IsSpawned
            )
        )
        {
            yield return null;
        }

        if (
            !networkManager.IsConnectedClient ||
            NetworkRunState.Instance == null ||
            !NetworkRunState.Instance.IsSpawned
        )
        {
            Fail("The standalone client could not connect to the host.");
            yield break;
        }

        NetworkRunState runState = NetworkRunState.Instance;
        DontDestroyOnLoad(runState.gameObject);
        currentStatus = "Waiting for the host to synchronize the island...";
        deadline = Time.realtimeSinceStartup + timeoutSeconds * 2f;

        while (
            Time.realtimeSinceStartup < deadline &&
            SceneManager.GetActiveScene().name != IslandSceneName
        )
        {
            yield return null;
        }

        if (SceneManager.GetActiveScene().name != IslandSceneName)
        {
            Fail("The client timed out during the NGO island scene load.");
            yield break;
        }

        NetworkObject playerObject = null;
        int enemyCount = 0;
        int rewardCount = 0;
        Vector2 expectedSpawn = new Vector2(1.5f, -10f);
        deadline = Time.realtimeSinceStartup + timeoutSeconds;

        while (Time.realtimeSinceStartup < deadline)
        {
            playerObject = networkManager.LocalClient?.PlayerObject;
            enemyCount = FindObjectsByType<Enemy>(
                FindObjectsSortMode.None
            ).Count(enemy => enemy.IsSpawned);
            rewardCount = FindObjectsByType<NetworkRewardChest>(
                FindObjectsSortMode.None
            ).Count(chest => chest.IsSpawned);

            if (
                playerObject != null &&
                Vector2.Distance(
                    playerObject.transform.position,
                    expectedSpawn
                ) <= 0.35f &&
                enemyCount >= 8 &&
                rewardCount >= 3 &&
                runState.Status.Value == NetworkRunStatus.Playing
            )
            {
                break;
            }

            yield return null;
        }

        if (playerObject == null || !playerObject.IsOwner)
        {
            Fail("The client did not receive its owned PlayerObject.");
            yield break;
        }

        if (Vector2.Distance(playerObject.transform.position, expectedSpawn) > 0.35f)
        {
            Fail(
                "The additional client did not arrive at PlayerSpawn_1. " +
                $"Expected {expectedSpawn}, got " +
                $"{(Vector2)playerObject.transform.position}."
            );
            yield break;
        }

        if (
            enemyCount < 8 || enemyCount > 12 ||
            rewardCount < 3 || rewardCount > 4
        )
        {
            Fail(
                "The client did not receive the full generated content set. " +
                $"Enemies: {enemyCount}, rewards: {rewardCount}."
            );
            yield break;
        }

        if (!ValidateCollisionTilemaps())
        {
            yield break;
        }

        yield return VerifyLocalPlayerViewport(playerObject);
        if (testFinished)
        {
            yield break;
        }

        HashSet<ulong> initialEnemyIds = FindObjectsByType<Enemy>(
            FindObjectsSortMode.None
        )
            .Where(enemy => enemy.IsSpawned)
            .Select(enemy => enemy.NetworkObjectId)
            .ToHashSet();

        currentStatus = "Waiting for replicated enemy despawn...";
        deadline = Time.realtimeSinceStartup + timeoutSeconds;

        HashSet<ulong> remainingEnemyIds = initialEnemyIds;
        while (Time.realtimeSinceStartup < deadline)
        {
            remainingEnemyIds = FindObjectsByType<Enemy>(
                FindObjectsSortMode.None
            )
                .Where(enemy => enemy.IsSpawned)
                .Select(enemy => enemy.NetworkObjectId)
                .ToHashSet();

            if (remainingEnemyIds.Count == initialEnemyIds.Count - 1)
            {
                break;
            }

            yield return null;
        }

        if (
            remainingEnemyIds.Count != initialEnemyIds.Count - 1 ||
            remainingEnemyIds.Except(initialEnemyIds).Any()
        )
        {
            Fail(
                "The client did not observe exactly one authoritative enemy " +
                "NetworkObject despawn."
            );
            yield break;
        }

        if (!FailOnUnexpectedRuntimeErrors("the client smoke test"))
        {
            Pass(
                "Standalone client synchronized the generated island, arrived " +
                "inside the camera viewport, and observed authoritative enemy " +
                "despawn."
            );
        }
    }

    private bool ValidateCollisionTilemaps()
    {
        TilemapCollider2D[] colliders = FindObjectsByType<TilemapCollider2D>(
            FindObjectsSortMode.None
        );

        string[] requiredNames =
        {
            "Tilemap_WaterCollision",
            "Tilemap_ObstacleCollision",
        };

        foreach (string requiredName in requiredNames)
        {
            TilemapCollider2D collider = colliders.FirstOrDefault(candidate =>
                candidate.name == requiredName);

            if (collider == null || collider.shapeCount == 0)
            {
                Fail(
                    $"Collision layer '{requiredName}' produced no runtime " +
                    "physics shapes."
                );
                return false;
            }
        }

        return true;
    }

    private void HandleNetworkLoadCompleted(
        string sceneName,
        LoadSceneMode loadSceneMode,
        List<ulong> clientsCompleted,
        List<ulong> clientsTimedOut
    )
    {
        if (clientsTimedOut != null && clientsTimedOut.Count > 0)
        {
            unexpectedRuntimeErrors.Add(
                $"NGO timed out {clientsTimedOut.Count} client(s) while " +
                $"loading '{sceneName}'."
            );
            return;
        }

        completedNetworkSceneLoads.Add(sceneName);
    }

    private void HandleRuntimeLog(
        string condition,
        string stackTrace,
        LogType type
    )
    {
        if (
            type != LogType.Error &&
            type != LogType.Exception &&
            type != LogType.Assert
        )
        {
            return;
        }

        if (
            !string.IsNullOrEmpty(condition) &&
            condition.StartsWith("[Technical Runtime Test] FAIL")
        )
        {
            return;
        }

        string message = string.IsNullOrWhiteSpace(condition)
            ? stackTrace
            : condition;
        unexpectedRuntimeErrors.Add($"{type}: {message}");
    }

    private bool FailOnUnexpectedRuntimeErrors(string context)
    {
        if (unexpectedRuntimeErrors.Count == 0)
        {
            return false;
        }

        string firstError = unexpectedRuntimeErrors[0];
        if (firstError.Length > 1000)
        {
            firstError = firstError.Substring(0, 1000) + "...";
        }

        Fail(
            $"An unexpected error was logged during {context}:\n" +
            firstError
        );
        return true;
    }

    private IEnumerator VerifyLocalPlayerViewport(NetworkObject playerObject)
    {
        yield return null;
        yield return new WaitForEndOfFrame();

        Camera camera = Camera.main;
        if (camera == null)
        {
            Fail("The island has no active MainCamera.");
            yield break;
        }

        Vector3 viewportPosition = camera.WorldToViewportPoint(
            playerObject.transform.position
        );
        if (
            viewportPosition.z <= 0f ||
            viewportPosition.x < 0.15f ||
            viewportPosition.x > 0.85f ||
            viewportPosition.y < 0.15f ||
            viewportPosition.y > 0.85f
        )
        {
            Fail(
                "The owned player spawned clipped against/outside the camera " +
                $"viewport: {viewportPosition}."
            );
            yield break;
        }

        if (
            Vector2.Distance(
                camera.transform.position,
                playerObject.transform.position
            ) > 0.75f
        )
        {
            Fail(
                "The local camera did not bind to and center its owned player."
            );
        }
    }

    private static bool IsClientTestRole()
    {
        return System.Environment.GetCommandLineArgs().Any(argument =>
            argument == "-dmtTestClient");
    }

    private static bool IsLateJoinHostRole()
    {
        return System.Environment.GetCommandLineArgs().Any(argument =>
            argument == "-dmtLateJoinHost");
    }

    private static int GetExpectedParticipantCount()
    {
        const string prefix = "-dmtExpectedParticipants=";
        string argument = System.Environment.GetCommandLineArgs()
            .FirstOrDefault(value => value.StartsWith(prefix));

        return argument != null &&
            int.TryParse(argument.Substring(prefix.Length), out int count)
                ? Mathf.Max(1, count)
                : 1;
    }

    private static bool AreNetworkServicesReady()
    {
        return
            NetworkRunState.Instance != null &&
            NetworkRunState.Instance.IsSpawned &&
            NetworkRunState.Instance.IsServer &&
            NetworkRunConfigAuthority.Instance != null &&
            NetworkRunConfigAuthority.Instance.IsSpawned &&
            NetworkRunConfigAuthority.Instance.IsServer &&
            NetworkRunConfigAuthority.Instance.IsConfigReady;
    }

    private void Pass(string message)
    {
        testFinished = true;
        testPassed = true;
        currentStatus = "PASS: " + message;

        Debug.Log(
            "[Technical Runtime Test] PASS\n" + message,
            this
        );

        TryScheduleAutomaticExit(0);
    }

    private void Fail(string message)
    {
        testFinished = true;
        testPassed = false;
        currentStatus = "FAIL: " + message;

        Debug.LogError(
            "[Technical Runtime Test] FAIL\n" + message,
            this
        );

        TryScheduleAutomaticExit(1);
    }

    private void TryScheduleAutomaticExit(int exitCode)
    {
        if (!System.Environment.GetCommandLineArgs().Any(argument =>
            argument == "-dmtAutoTest"))
        {
            return;
        }

        StartCoroutine(ExitAfterLogsFlush(exitCode));
    }

    private static IEnumerator ExitAfterLogsFlush(int exitCode)
    {
        yield return null;
        yield return null;
        Application.Quit(exitCode);
    }

    private void OnGUI()
    {
        const float width = 640f;
        const float height = 150f;

        Rect panel = new Rect(
            20f,
            20f,
            width,
            height
        );

        GUI.Box(panel, "Technical Network Runtime Test");
        GUI.Label(
            new Rect(40f, 55f, width - 40f, 55f),
            currentStatus
        );

        if (
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening &&
            GUI.Button(
                new Rect(40f, 115f, 180f, 30f),
                "Shutdown Test Host"
            )
        )
        {
            NetworkManager.Singleton.Shutdown();
            currentStatus = testFinished
                ? (testPassed ? "Test passed. Host stopped." : "Test failed. Host stopped.")
                : "Host stopped before the test finished.";
        }
    }
}
