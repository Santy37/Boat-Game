using System;
using System.Collections.Generic;
using System.Linq;
using DeadmansTales.Configuration;
using DeadmansTales.Networking;
using DeadmansTales.WorldGeneration;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

internal sealed class NetworkingArchitectureTests
{
    private const string NetworkManagerPrefabPath =
        "Assets/DeadmansTales/Prefabs/Networking/NetworkManager.prefab";
    private const string MainMenuScenePath =
        "Assets/DeadmansTales/Scenes/MainMenu.unity";
    private const string LobbyScenePath =
        "Assets/DeadmansTales/Scenes/Lobby_Island_2D.unity";
    private const string BoatScenePath =
        "Assets/DeadmansTales/Scenes/Boat_Gameplay_2D.unity";
    private const string IslandScenePath =
        "Assets/DeadmansTales/Scenes/Island_After_Ocean_01_2D.unity";
    private const string EnemyPrefabPath =
        "Assets/DeadmansTales/Prefabs/basicenemy.prefab";
    private const string PlayerPrefabPath =
        "Assets/DeadmansTales/Prefabs/Player_2D_Network.prefab";
    private const string RewardChestPrefabPath =
        "Assets/DeadmansTales/Prefabs/Gameplay/NetworkRewardChest.prefab";
    private const string RunStatePrefabPath =
        "Assets/DeadmansTales/Prefabs/Gameplay/NetworkRunState.prefab";
    private const string NetworkPrefabsPath =
        "Assets/DefaultNetworkPrefabs.asset";
    private const string IslandPalettePath =
        "Assets/DeadmansTales/Palettes/Island_Stage_02_Palette.prefab";
    private const string WaterCollisionTilePath =
        "Assets/DeadmansTales/Palettes/Island_WaterCollision_Grid.asset";
    private const string ObstacleCollisionTilePath =
        "Assets/DeadmansTales/Palettes/Island_ObstacleCollision_Grid.asset";

    private static readonly Vector3Int[] TopologyNeighbors =
    {
        new Vector3Int(-1, 1, 0),
        new Vector3Int(0, 1, 0),
        new Vector3Int(1, 1, 0),
        new Vector3Int(-1, 0, 0),
        new Vector3Int(1, 0, 0),
        new Vector3Int(-1, -1, 0),
        new Vector3Int(0, -1, 0),
        new Vector3Int(1, -1, 0),
    };

    private sealed class LobbyIslandReference
    {
        public Dictionary<int, HashSet<string>> GroundShoreTilesByMask;
        public Dictionary<int, HashSet<string>> WaterEdgeTilesByMask;
        public Dictionary<int, HashSet<string>> CoastalWaterTilesByLandMask;
        public float WaterCollisionCoverage;
    }

    [Test]
    public void NetworkPrefabsUseServerAuthoritativeClientServerSetup()
    {
        GameObject managerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            NetworkManagerPrefabPath
        );

        Assert.That(managerPrefab, Is.Not.Null);

        NetworkManager manager =
            managerPrefab.GetComponent<NetworkManager>();

        Assert.That(manager, Is.Not.Null);
        Assert.That(
            manager.NetworkConfig.NetworkTopology,
            Is.EqualTo(NetworkTopologyTypes.ClientServer)
        );
        Assert.That(manager.NetworkConfig.EnableSceneManagement, Is.True);
        Assert.That(manager.NetworkConfig.PlayerPrefab, Is.Not.Null);

        GameObject playerPrefab = manager.NetworkConfig.PlayerPrefab;

        Assert.That(
            playerPrefab.GetComponent<NetworkObject>(),
            Is.Not.Null
        );
        Assert.That(
            playerPrefab.GetComponent<TopDownNetworkPlayer2D>(),
            Is.Not.Null
        );
        Assert.That(playerPrefab.GetComponent<PlayerHealth>(), Is.Not.Null);
        Assert.That(playerPrefab.GetComponent<PlayerAttack>(), Is.Not.Null);
        Assert.That(
            playerPrefab.GetComponent<NetworkInteractionController2D>(),
            Is.Not.Null
        );
        Assert.That(
            playerPrefab.GetComponent<NetworkInteractionInput2D>(),
            Is.Not.Null
        );

        NetworkTransform networkTransform =
            playerPrefab.GetComponent<NetworkTransform>();

        Assert.That(networkTransform, Is.Not.Null);
        Assert.That(
            networkTransform.AuthorityMode,
            Is.EqualTo(NetworkTransform.AuthorityModes.Server)
        );

        NetworkRigidbody2D networkRigidbody =
            playerPrefab.GetComponent<NetworkRigidbody2D>();

        Assert.That(networkRigidbody, Is.Not.Null);
        Assert.That(networkRigidbody.UseRigidBodyForMotion, Is.True);
        Assert.That(networkRigidbody.AutoUpdateKinematicState, Is.True);
    }

    [Test]
    public void OnlyBootstrapSceneContainsNetworkManager()
    {
        Assert.That(CountSceneComponents<NetworkManager>(MainMenuScenePath),
            Is.EqualTo(1));
        Assert.That(CountSceneComponents<NetworkManager>(LobbyScenePath),
            Is.Zero);
        Assert.That(CountSceneComponents<NetworkManager>(BoatScenePath),
            Is.Zero);
        Assert.That(CountSceneComponents<NetworkManager>(IslandScenePath),
            Is.Zero);
    }

    [TestCase(LobbyScenePath)]
    [TestCase(BoatScenePath)]
    [TestCase(IslandScenePath)]
    public void GameplaySceneHasFourExplicitSpawnMarkers(string scenePath)
    {
        IReadOnlyList<string> markerNames = ReadSceneComponents<
            PlayerSpawnPoint2D,
            IReadOnlyList<string>
        >(
            scenePath,
            markers => markers.Select(marker => marker.name).ToArray()
        );

        Assert.That(markerNames.Count, Is.EqualTo(4));
        Assert.That(
            markerNames.OrderBy(name => name),
            Is.EqualTo(new[]
            {
                "PlayerSpawn_0",
                "PlayerSpawn_1",
                "PlayerSpawn_2",
                "PlayerSpawn_3",
            })
        );
    }

    [TestCase(LobbyScenePath)]
    [TestCase(BoatScenePath)]
    [TestCase(IslandScenePath)]
    public void GameplaySceneNetworkObjectsHaveUniqueNonzeroIdentities(
        string scenePath
    )
    {
        ReadSceneComponents<NetworkObject, bool>(
            scenePath,
            components =>
            {
                NetworkObject[] networkObjects = components.ToArray();

                Assert.That(
                    networkObjects.Any(networkObject =>
                        networkObject.PrefabIdHash == 0),
                    Is.False,
                    $"{scenePath} contains an NGO scene object with no identity."
                );

                IGrouping<uint, NetworkObject> duplicate = networkObjects
                    .GroupBy(networkObject => networkObject.PrefabIdHash)
                    .FirstOrDefault(group => group.Count() > 1);

                Assert.That(
                    duplicate,
                    Is.Null,
                    duplicate == null
                        ? string.Empty
                        : $"{scenePath} contains duplicate NGO hash " +
                          $"{duplicate.Key}: " +
                          string.Join(", ", duplicate.Select(item => item.name))
                );

                return true;
            }
        );
    }

    [Test]
    public void SpawnedGameplayPrefabsAreRegisteredAndHaveUniqueHashes()
    {
        GameObject managerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            NetworkManagerPrefabPath
        );
        NetworkManager manager = managerPrefab.GetComponent<NetworkManager>();
        NetworkPrefabsList configuredList =
            AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(
                NetworkPrefabsPath
            );

        Assert.That(configuredList, Is.Not.Null);
        Assert.That(
            manager.NetworkConfig.Prefabs.NetworkPrefabsLists,
            Does.Contain(configuredList)
        );

        GameObject[] requiredPrefabs =
        {
            manager.NetworkConfig.PlayerPrefab,
            AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath),
            AssetDatabase.LoadAssetAtPath<GameObject>(RewardChestPrefabPath),
            AssetDatabase.LoadAssetAtPath<GameObject>(RunStatePrefabPath),
        };

        foreach (GameObject requiredPrefab in requiredPrefabs)
        {
            Assert.That(requiredPrefab, Is.Not.Null);
        }

        Assert.That(
            requiredPrefabs[3].GetComponent<NetworkRunConfigAuthority>(),
            Is.Not.Null,
            "Persistent synchronized config must share the runtime-spawned " +
            "run-state prefab so late joiners can resolve it by prefab hash."
        );

        uint[] prefabHashes = requiredPrefabs
            .Select(prefab =>
            {
                Assert.That(
                    configuredList.Contains(prefab),
                    Is.True,
                    $"{prefab.name} is not registered in the NGO prefab list."
                );

                NetworkObject networkObject =
                    prefab.GetComponent<NetworkObject>();
                Assert.That(networkObject, Is.Not.Null, prefab.name);
                Assert.That(
                    networkObject.PrefabIdHash,
                    Is.Not.Zero,
                    prefab.name
                );
                return networkObject.PrefabIdHash;
            })
            .ToArray();

        Assert.That(prefabHashes.Distinct().Count(),
            Is.EqualTo(prefabHashes.Length));
    }

    [Test]
    public void EnemyPrefabUsesServerAuthoritativeNetworkPhysics()
    {
        GameObject enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            EnemyPrefabPath
        );

        Assert.That(enemyPrefab, Is.Not.Null);
        Assert.That(enemyPrefab.GetComponent<NetworkObject>(), Is.Not.Null);
        Assert.That(enemyPrefab.GetComponent<Enemy>(), Is.Not.Null);
        Assert.That(enemyPrefab.GetComponent<EnemyAI>(), Is.Not.Null);

        NetworkTransform networkTransform =
            enemyPrefab.GetComponent<NetworkTransform>();
        Assert.That(networkTransform, Is.Not.Null);
        Assert.That(
            networkTransform.AuthorityMode,
            Is.EqualTo(NetworkTransform.AuthorityModes.Server)
        );

        NetworkRigidbody2D networkRigidbody =
            enemyPrefab.GetComponent<NetworkRigidbody2D>();
        Assert.That(networkRigidbody, Is.Not.Null);
        Assert.That(networkRigidbody.UseRigidBodyForMotion, Is.True);
        Assert.That(networkRigidbody.AutoUpdateKinematicState, Is.True);
    }

    [Test]
    public void LobbyEnemiesAreServerSpawnedRuntimePrefabs()
    {
        ReadSceneComponents<Component, bool>(
            LobbyScenePath,
            _ =>
            {
                Scene scene = SceneManager.GetSceneByPath(LobbyScenePath);
                Enemy[] authoredEnemies = scene
                    .GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Enemy>(true))
                    .ToArray();
                NetworkSceneEnemySpawner2D[] spawners = scene
                    .GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<
                        NetworkSceneEnemySpawner2D>(true))
                    .ToArray();

                Assert.That(
                    authoredEnemies,
                    Is.Empty,
                    "Lobby enemies must not remain scene-placed NetworkObjects."
                );
                Assert.That(spawners.Length, Is.EqualTo(1));
                Assert.That(spawners[0].SpawnPositionCount, Is.EqualTo(3));

                SerializedObject serializedSpawner =
                    new SerializedObject(spawners[0]);
                Assert.That(
                    serializedSpawner.FindProperty("enemyPrefab")
                        .objectReferenceValue,
                    Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(
                        EnemyPrefabPath))
                );

                return true;
            }
        );
    }

    [Test]
    public void PlayerAttackUsesResponsiveAnticipatedTiming()
    {
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            PlayerPrefabPath
        );
        PlayerAttack attack = playerPrefab.GetComponent<PlayerAttack>();

        Assert.That(attack, Is.Not.Null);

        SerializedObject serializedAttack = new SerializedObject(attack);
        float cooldown = serializedAttack
            .FindProperty("attackCooldown")
            .floatValue;
        float inputBuffer = serializedAttack
            .FindProperty("inputBufferSeconds")
            .floatValue;

        Assert.That(cooldown, Is.InRange(0.25f, 0.5f));
        Assert.That(inputBuffer, Is.InRange(0.08f, 0.2f));
    }

    [Test]
    public void IslandSceneHasPaintedLayersAndSeededGameplayMarkers()
    {
        LobbyIslandReference lobbyReference =
            CaptureLobbyIslandReference();

        GameObject islandPalette =
            AssetDatabase.LoadAssetAtPath<GameObject>(IslandPalettePath);
        Assert.That(
            islandPalette,
            Is.Not.Null,
            "The island Tile Palette was not created."
        );
        Assert.That(
            islandPalette.GetComponentInChildren<Tilemap>(true)
                .GetUsedTilesCount(),
            Is.GreaterThanOrEqualTo(390),
            "The stage palette must preserve both source atlases."
        );

        foreach (string collisionTilePath in new[]
        {
            WaterCollisionTilePath,
            ObstacleCollisionTilePath,
        })
        {
            Tile collisionTile = AssetDatabase.LoadAssetAtPath<Tile>(
                collisionTilePath
            );
            Assert.That(collisionTile, Is.Not.Null);
            Assert.That(
                collisionTile.colliderType,
                Is.EqualTo(Tile.ColliderType.Grid)
            );
        }

        ReadSceneComponents<Component, bool>(
            IslandScenePath,
            _ =>
            {
                Scene scene = SceneManager.GetSceneByPath(IslandScenePath);
                Dictionary<string, Tilemap> tilemaps = scene
                    .GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<Tilemap>(true))
                    .ToDictionary(tilemap => tilemap.name);

                string[] requiredLayers =
                {
                    "Tilemap_Water",
                    "Tilemap_Ground",
                    "Tilemap_GroundDetail",
                    "Tilemap_Props",
                    "Tilemap_Overhead",
                    "Tilemap_WaterCollision",
                    "Tilemap_ObstacleCollision",
                };

                Assert.That(tilemaps.Keys, Is.SupersetOf(requiredLayers));
                Assert.That(
                    tilemaps["Tilemap_Water"].GetUsedTilesCount(),
                    Is.GreaterThan(0)
                );
                Assert.That(
                    tilemaps["Tilemap_Ground"].GetUsedTilesCount(),
                    Is.GreaterThanOrEqualTo(20)
                );
                Assert.That(
                    tilemaps["Tilemap_GroundDetail"].GetUsedTilesCount(),
                    Is.GreaterThan(0)
                );
                Assert.That(
                    tilemaps["Tilemap_Props"].GetUsedTilesCount(),
                    Is.GreaterThan(0)
                );
                Assert.That(
                    tilemaps["Tilemap_Overhead"].GetUsedTilesCount(),
                    Is.GreaterThan(0)
                );
                Assert.That(
                    CountPaintedCells(tilemaps["Tilemap_GroundDetail"]),
                    Is.GreaterThan(40),
                    "The island ground dressing is still too sparse."
                );
                Assert.That(
                    CountPaintedCells(tilemaps["Tilemap_Props"]),
                    Is.GreaterThan(150),
                    "The island needs substantially more foreground props."
                );
                Assert.That(
                    CountPaintedCells(tilemaps["Tilemap_Overhead"]),
                    Is.GreaterThan(140),
                    "The palm canopy/overhead layer is still too sparse."
                );
                Assert.That(tilemaps["Tilemap_WaterCollision"].gameObject.activeSelf,
                    Is.True);
                Assert.That(tilemaps["Tilemap_ObstacleCollision"].gameObject.activeSelf,
                    Is.True);
                Tilemap water = tilemaps["Tilemap_Water"];
                Tilemap ground = tilemaps["Tilemap_Ground"];
                Tilemap waterCollision =
                    tilemaps["Tilemap_WaterCollision"];
                Tilemap obstacleCollision =
                    tilemaps["Tilemap_ObstacleCollision"];

                AssertCollisionTilemapConfiguration(waterCollision);
                AssertCollisionTilemapConfiguration(obstacleCollision);
                AssertGroundTopologyAndPalette(
                    ground,
                    lobbyReference.GroundShoreTilesByMask
                );
                AssertWaterTopologyAndPalette(
                    water,
                    ground,
                    lobbyReference.WaterEdgeTilesByMask,
                    lobbyReference.CoastalWaterTilesByLandMask
                );
                AssertWaterCollisionCoverage(
                    water,
                    ground,
                    waterCollision,
                    lobbyReference.WaterCollisionCoverage
                );
                AssertSafeIslandSpawns(
                    scene,
                    ground,
                    waterCollision,
                    obstacleCollision
                );
                AssertIslandCameraFraming(
                    scene,
                    ground
                );
                AssertPierIsAttachedOnlyAtEastShore(
                    ground,
                    tilemaps["Tilemap_Props"]
                );

                for (int y = -11; y <= 14; y++)
                {
                    int pathX = Mathf.RoundToInt(
                        Mathf.Sin((y + 10f) * 0.24f) * 1.7f
                    );

                    Assert.That(
                        obstacleCollision.HasTile(
                            new Vector3Int(pathX, y, 0)
                        ),
                        Is.False,
                        $"The main island route is blocked at ({pathX}, {y})."
                    );
                }

                for (int x = 0; x <= 21; x++)
                {
                    int pathY = Mathf.RoundToInt(
                        2f + Mathf.Sin(x * 0.24f)
                    );

                    Assert.That(
                        obstacleCollision.HasTile(
                            new Vector3Int(x, pathY, 0)
                        ),
                        Is.False,
                        $"The east dock route is blocked at ({x}, {pathY})."
                    );
                }

                SeededSpawnMarker2D[] markers = scene
                    .GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<SeededSpawnMarker2D>(true))
                    .ToArray();

                Assert.That(markers.Count(marker =>
                    marker.Category == SeededContentCategory.Enemy),
                    Is.EqualTo(20));
                Assert.That(markers.Count(marker =>
                    marker.Category == SeededContentCategory.Loot),
                    Is.EqualTo(8));
                Assert.That(markers.Count(marker =>
                    marker.Category == SeededContentCategory.Reward &&
                    marker.AlwaysSpawn),
                    Is.EqualTo(1));

                Assert.That(
                    scene.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<
                            SeededIslandContentGenerator>(true))
                        .Count(),
                    Is.EqualTo(1)
                );
                Assert.That(
                    scene.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<
                            StageSeedProvider>(true))
                        .Count(),
                    Is.EqualTo(1)
                );
                Assert.That(
                    scene.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<
                            NetworkStagePortal>(true))
                        .Count(),
                    Is.EqualTo(1)
                );

                return true;
            }
        );
    }

    [Test]
    public void BoatSceneTransitionsToIslandThroughNetworkPortal()
    {
        int portalCount = CountSceneComponents<NetworkStagePortal>(
            BoatScenePath
        );

        Assert.That(portalCount, Is.EqualTo(1));
        Assert.That(
            EditorBuildSettings.scenes.Any(scene =>
                scene.enabled && scene.path == IslandScenePath),
            Is.True
        );
    }

    private static void AssertGroundTopologyAndPalette(
        Tilemap ground,
        IReadOnlyDictionary<int, HashSet<string>> allowedShoreTilesByMask
    )
    {
        List<Vector3Int> cells = GetPaintedCells(ground).ToList();
        HashSet<Vector3Int> occupied = cells.ToHashSet();

        Assert.That(cells.Count, Is.InRange(1000, 1300));

        Queue<Vector3Int> pending = new Queue<Vector3Int>();
        HashSet<Vector3Int> visited = new HashSet<Vector3Int>();
        pending.Enqueue(cells[0]);
        visited.Add(cells[0]);

        Vector3Int[] cardinalDirections =
        {
            Vector3Int.left,
            Vector3Int.right,
            Vector3Int.up,
            Vector3Int.down,
        };

        while (pending.Count > 0)
        {
            Vector3Int current = pending.Dequeue();
            foreach (Vector3Int direction in cardinalDirections)
            {
                Vector3Int neighbor = current + direction;
                if (occupied.Contains(neighbor) && visited.Add(neighbor))
                {
                    pending.Enqueue(neighbor);
                }
            }
        }

        Assert.That(
            visited.Count,
            Is.EqualTo(cells.Count),
            "The island must be one connected playable landmass."
        );

        Dictionary<string, int> interiorCounts = Enumerable.Range(0, 7)
            .ToDictionary(index => $"tf_beach_tileB_{index}", _ => 0);
        int rotatedInteriorCount = 0;

        foreach (Vector3Int cell in cells)
        {
            int mask = GetNeighborMask(occupied, cell);

            TileBase tile = ground.GetTile(cell);
            if (mask == 255)
            {
                Assert.That(interiorCounts.ContainsKey(tile.name), Is.True);
                interiorCounts[tile.name]++;

                if (ground.GetTransformMatrix(cell) != Matrix4x4.identity)
                {
                    rotatedInteriorCount++;
                }

                continue;
            }

            Assert.That(
                allowedShoreTilesByMask.TryGetValue(
                    mask,
                    out HashSet<string> allowedTileNames
                ),
                Is.True,
                $"Lobby island has no shoreline example for mask {mask} " +
                $"used at {cell}."
            );
            Assert.That(
                allowedTileNames.Contains(tile.name),
                Is.True,
                $"Shore at {cell} uses {tile.name}; lobby allows " +
                $"[{string.Join(", ", allowedTileNames)}] for mask {mask}."
            );
        }

        Assert.That(interiorCounts.Values.All(count => count > 100), Is.True);
        Assert.That(rotatedInteriorCount, Is.GreaterThan(300));
    }

    private static void AssertWaterTopologyAndPalette(
        Tilemap water,
        Tilemap ground,
        IReadOnlyDictionary<int, HashSet<string>> allowedWaterEdgeTilesByMask,
        IReadOnlyDictionary<int, HashSet<string>>
            allowedCoastalWaterTilesByLandMask
    )
    {
        HashSet<Vector3Int> waterCells = GetPaintedCells(water);
        HashSet<Vector3Int> groundCells = GetPaintedCells(ground);

        // The island coast is much larger than the lobby reference, so it can
        // legitimately produce local water shapes the lobby never authored.
        // Those cells must still use tiles from the lobby's water vocabulary.
        HashSet<string> lobbyWaterTileNames = allowedWaterEdgeTilesByMask
            .Values
            .SelectMany(names => names)
            .ToHashSet();

        foreach (Vector3Int cell in waterCells)
        {
            TileBase tile = water.GetTile(cell);
            int waterMask = GetNeighborMask(waterCells, cell);

            if (waterMask != 255)
            {
                if (allowedWaterEdgeTilesByMask.ContainsKey(waterMask))
                {
                    AssertTileIsAllowedForMask(
                        tile,
                        cell,
                        waterMask,
                        allowedWaterEdgeTilesByMask,
                        "water edge"
                    );
                }
                else
                {
                    Assert.That(
                        tile,
                        Is.Not.Null,
                        $"Missing water edge tile at {cell}."
                    );
                    Assert.That(
                        lobbyWaterTileNames.Contains(tile.name),
                        Is.True,
                        $"water edge at {cell} uses {tile.name} for novel " +
                        $"mask {waterMask}; the lobby water layer never " +
                        "uses that tile."
                    );
                }
            }

            if (groundCells.Contains(cell))
            {
                continue;
            }

            int landMask = GetNeighborMask(groundCells, cell);
            if (landMask == 0)
            {
                continue;
            }

            if (allowedCoastalWaterTilesByLandMask.ContainsKey(landMask))
            {
                AssertTileIsAllowedForMask(
                    tile,
                    cell,
                    landMask,
                    allowedCoastalWaterTilesByLandMask,
                    "coastal water"
                );
            }
            else
            {
                HashSet<string> coastalVocabulary =
                    allowedCoastalWaterTilesByLandMask
                        .Values
                        .SelectMany(names => names)
                        .ToHashSet();
                Assert.That(
                    tile,
                    Is.Not.Null,
                    $"Missing coastal water tile at {cell}."
                );
                Assert.That(
                    coastalVocabulary.Contains(tile.name),
                    Is.True,
                    $"coastal water at {cell} uses {tile.name} for novel " +
                    $"land mask {landMask}; the lobby coast never uses " +
                    "that tile."
                );
            }
        }
    }

    private static void AssertWaterCollisionCoverage(
        Tilemap water,
        Tilemap ground,
        Tilemap waterCollision,
        float lobbyCoverage
    )
    {
        HashSet<Vector3Int> waterCells = GetPaintedCells(water);
        HashSet<Vector3Int> groundCells = GetPaintedCells(ground);
        HashSet<Vector3Int> collisionCells =
            GetPaintedCells(waterCollision);

        foreach (Vector3Int cell in collisionCells)
        {
            Assert.That(
                waterCells.Contains(cell),
                Is.True,
                $"Water collision at {cell} has no rendered water tile."
            );
            Assert.That(
                groundCells.Contains(cell),
                Is.False,
                $"Water collision overlaps playable land at {cell}."
            );
        }

        HashSet<Vector3Int> eligibleWater = new HashSet<Vector3Int>(
            waterCells.Where(cell => !groundCells.Contains(cell))
        );
        int coveredWaterCells = eligibleWater.Count(collisionCells.Contains);
        float coverage = eligibleWater.Count == 0
            ? 0f
            : (float)coveredWaterCells / eligibleWater.Count;
        float minimumCoverage = Mathf.Max(0.9f, lobbyCoverage - 0.05f);

        Assert.That(
            coverage,
            Is.GreaterThanOrEqualTo(minimumCoverage),
            $"Water collision covers {coveredWaterCells}/" +
            $"{eligibleWater.Count} non-land water cells ({coverage:P1}); " +
            $"the lobby reference covers {lobbyCoverage:P1}."
        );
    }

    private static void AssertCollisionTilemapConfiguration(Tilemap tilemap)
    {
        TilemapRenderer renderer = tilemap.GetComponent<TilemapRenderer>();
        TilemapCollider2D tilemapCollider =
            tilemap.GetComponent<TilemapCollider2D>();
        CompositeCollider2D compositeCollider =
            tilemap.GetComponent<CompositeCollider2D>();
        Rigidbody2D body = tilemap.GetComponent<Rigidbody2D>();

        Assert.That(renderer, Is.Not.Null, tilemap.name);
        Assert.That(renderer.enabled, Is.False, tilemap.name);
        Assert.That(tilemapCollider, Is.Not.Null, tilemap.name);
        Assert.That(tilemapCollider.enabled, Is.True, tilemap.name);
        Assert.That(tilemapCollider.isTrigger, Is.False, tilemap.name);
        Assert.That(
            tilemapCollider.compositeOperation,
            Is.EqualTo(Collider2D.CompositeOperation.Merge),
            $"{tilemap.name} must merge its tile shapes into a composite."
        );
        Assert.That(compositeCollider, Is.Not.Null, tilemap.name);
        Assert.That(compositeCollider.enabled, Is.True, tilemap.name);
        Assert.That(compositeCollider.isTrigger, Is.False, tilemap.name);
        Assert.That(body, Is.Not.Null, tilemap.name);
        Assert.That(body.simulated, Is.True, tilemap.name);
        Assert.That(body.bodyType, Is.EqualTo(RigidbodyType2D.Static));
    }

    private static void AssertSafeIslandSpawns(
        Scene scene,
        Tilemap ground,
        Tilemap waterCollision,
        Tilemap obstacleCollision
    )
    {
        PlayerSpawnPoint2D[] spawns = scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<
                PlayerSpawnPoint2D>(true))
            .OrderBy(spawn => spawn.name)
            .ToArray();

        Vector2[] expected =
        {
            new Vector2(-1.5f, -10f),
            new Vector2(1.5f, -10f),
            new Vector2(-1.5f, -8f),
            new Vector2(1.5f, -8f),
        };

        Assert.That(spawns.Length, Is.EqualTo(expected.Length));

        for (int index = 0; index < spawns.Length; index++)
        {
            Assert.That(
                Vector2.Distance(
                    spawns[index].transform.position,
                    expected[index]
                ),
                Is.LessThan(0.01f)
            );

            Vector3Int center = ground.WorldToCell(
                spawns[index].transform.position
            );
            Assert.That(ground.HasTile(center), Is.True);
            Assert.That(waterCollision.HasTile(center), Is.False);
            Assert.That(obstacleCollision.HasTile(center), Is.False);

            for (int x = -3; x <= 3; x++)
            {
                for (int y = -3; y <= 3; y++)
                {
                    Assert.That(
                        ground.HasTile(center + new Vector3Int(x, y, 0)),
                        Is.True,
                        $"{spawns[index].name} is too close to the shoreline."
                    );
                }
            }
        }
    }

    private static void AssertIslandCameraFraming(Scene scene, Tilemap ground)
    {
        Camera camera = scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Camera>(true))
            .Single();
        Camera2DFollow follow = camera.GetComponent<Camera2DFollow>();
        BoxCollider2D bounds = scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<BoxCollider2D>(true))
            .Single(collider => collider.name == "CameraBounds");
        SerializedObject serializedFollow = new SerializedObject(follow);

        Assert.That(follow, Is.Not.Null);
        Assert.That(
            serializedFollow.FindProperty("followLocalPlayer").boolValue,
            Is.True
        );
        Assert.That(
            Vector2.Distance(
                serializedFollow.FindProperty("islandCenter").vector2Value,
                new Vector2(0f, -9f)
            ),
            Is.LessThan(0.01f)
        );
        Assert.That(
            Vector2.Distance(
                camera.transform.position,
                new Vector2(0f, -9f)
            ),
            Is.LessThan(0.01f)
        );

        const float halfHeight = 8f;
        const float halfWidth = halfHeight * 16f / 9f;
        Bounds worldBounds = bounds.bounds;

        Assert.That(
            worldBounds.min.x,
            Is.LessThanOrEqualTo(ground.cellBounds.xMin - halfWidth + 0.5f)
        );
        Assert.That(
            worldBounds.max.x,
            Is.GreaterThanOrEqualTo(ground.cellBounds.xMax + halfWidth - 0.5f)
        );
        Assert.That(
            worldBounds.min.y,
            Is.LessThanOrEqualTo(ground.cellBounds.yMin - halfHeight)
        );
        Assert.That(
            worldBounds.max.y,
            Is.GreaterThanOrEqualTo(ground.cellBounds.yMax + halfHeight)
        );
    }

    private static void AssertPierIsAttachedOnlyAtEastShore(
        Tilemap ground,
        Tilemap props
    )
    {
        HashSet<string> pierTileNames = new HashSet<string>(
            new[]
            {
                10, 11, 12, 13, 14,
                26, 27, 28, 29, 30,
                42, 43, 44, 45, 46,
            }.Select(index => $"tf_beach_tileB_{index}")
        );

        List<Vector3Int> pierCells = new List<Vector3Int>();
        foreach (Vector3Int position in props.cellBounds.allPositionsWithin)
        {
            TileBase tile = props.GetTile(position);
            if (tile != null && pierTileNames.Contains(tile.name))
            {
                pierCells.Add(position);
            }
        }

        Assert.That(pierCells.Count, Is.GreaterThanOrEqualTo(15));
        Assert.That(pierCells.All(cell => cell.x >= 20), Is.True);
        Assert.That(pierCells.Any(ground.HasTile), Is.True);
        Assert.That(pierCells.Any(cell => !ground.HasTile(cell)), Is.True);
    }

    private static LobbyIslandReference CaptureLobbyIslandReference()
    {
        return ReadSceneComponents<Tilemap, LobbyIslandReference>(
            LobbyScenePath,
            components =>
            {
                Dictionary<string, Tilemap> tilemaps = components
                    .ToDictionary(tilemap => tilemap.name);
                Tilemap water = tilemaps["Tilemap_Water"];
                Tilemap ground = tilemaps["Tilemap_Ground"];
                Tilemap waterCollision =
                    tilemaps["Tilemap_WaterCollision"];
                HashSet<Vector3Int> waterCells = GetPaintedCells(water);
                HashSet<Vector3Int> groundCells = GetPaintedCells(ground);
                HashSet<Vector3Int> collisionCells =
                    GetPaintedCells(waterCollision);
                HashSet<Vector3Int> eligibleWater =
                    new HashSet<Vector3Int>(
                        waterCells.Where(cell => !groundCells.Contains(cell))
                    );
                int coveredWaterCells =
                    eligibleWater.Count(collisionCells.Contains);

                Assert.That(eligibleWater.Count, Is.GreaterThan(0));

                return new LobbyIslandReference
                {
                    GroundShoreTilesByMask =
                        CaptureEdgeTileChoices(ground, groundCells),
                    WaterEdgeTilesByMask =
                        CaptureEdgeTileChoices(water, waterCells),
                    CoastalWaterTilesByLandMask =
                        CaptureCoastalWaterTileChoices(
                            water,
                            waterCells,
                            groundCells
                        ),
                    WaterCollisionCoverage =
                        (float)coveredWaterCells / eligibleWater.Count,
                };
            }
        );
    }

    private static Dictionary<int, HashSet<string>>
        CaptureEdgeTileChoices(
            Tilemap tilemap,
            HashSet<Vector3Int> occupiedCells
        )
    {
        Dictionary<int, HashSet<string>> choices =
            new Dictionary<int, HashSet<string>>();

        foreach (Vector3Int cell in occupiedCells)
        {
            int mask = GetNeighborMask(occupiedCells, cell);
            if (mask == 255)
            {
                continue;
            }

            AddTileChoice(choices, mask, tilemap.GetTile(cell));
        }

        return choices;
    }

    private static Dictionary<int, HashSet<string>>
        CaptureCoastalWaterTileChoices(
            Tilemap water,
            HashSet<Vector3Int> waterCells,
            HashSet<Vector3Int> groundCells
        )
    {
        Dictionary<int, HashSet<string>> choices =
            new Dictionary<int, HashSet<string>>();

        foreach (Vector3Int cell in waterCells)
        {
            if (groundCells.Contains(cell))
            {
                continue;
            }

            int landMask = GetNeighborMask(groundCells, cell);
            if (landMask != 0)
            {
                AddTileChoice(choices, landMask, water.GetTile(cell));
            }
        }

        return choices;
    }

    private static void AddTileChoice(
        IDictionary<int, HashSet<string>> choices,
        int mask,
        TileBase tile
    )
    {
        Assert.That(tile, Is.Not.Null);

        if (!choices.TryGetValue(mask, out HashSet<string> tileNames))
        {
            tileNames = new HashSet<string>(StringComparer.Ordinal);
            choices.Add(mask, tileNames);
        }

        tileNames.Add(tile.name);
    }

    private static void AssertTileIsAllowedForMask(
        TileBase tile,
        Vector3Int cell,
        int mask,
        IReadOnlyDictionary<int, HashSet<string>> allowedTilesByMask,
        string topologyName
    )
    {
        Assert.That(tile, Is.Not.Null, $"Missing {topologyName} tile at {cell}.");
        Assert.That(
            allowedTilesByMask.TryGetValue(
                mask,
                out HashSet<string> allowedTileNames
            ),
            Is.True,
            $"Lobby island has no {topologyName} example for mask {mask} " +
            $"used at {cell}."
        );
        Assert.That(
            allowedTileNames.Contains(tile.name),
            Is.True,
            $"{topologyName} at {cell} uses {tile.name}; lobby allows " +
            $"[{string.Join(", ", allowedTileNames)}] for mask {mask}."
        );
    }

    private static int GetNeighborMask(
        HashSet<Vector3Int> occupiedCells,
        Vector3Int cell
    )
    {
        int mask = 0;

        for (int index = 0; index < TopologyNeighbors.Length; index++)
        {
            if (occupiedCells.Contains(cell + TopologyNeighbors[index]))
            {
                mask |= 1 << index;
            }
        }

        return mask;
    }

    private static HashSet<Vector3Int> GetPaintedCells(Tilemap tilemap)
    {
        HashSet<Vector3Int> cells = new HashSet<Vector3Int>();

        foreach (Vector3Int position in tilemap.cellBounds.allPositionsWithin)
        {
            if (tilemap.HasTile(position))
            {
                cells.Add(position);
            }
        }

        return cells;
    }

    private static int CountSceneComponents<T>(string scenePath)
        where T : Component
    {
        return ReadSceneComponents<T, int>(
            scenePath,
            components => components.Count()
        );
    }

    private static int CountPaintedCells(Tilemap tilemap)
    {
        return GetPaintedCells(tilemap).Count;
    }

    private static TResult ReadSceneComponents<T, TResult>(
        string scenePath,
        Func<IEnumerable<T>, TResult> read
    )
        where T : Component
    {
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool wasAlreadyLoaded = scene.isLoaded;

        if (!wasAlreadyLoaded)
        {
            scene = EditorSceneManager.OpenScene(
                scenePath,
                OpenSceneMode.Additive
            );
        }

        try
        {
            IEnumerable<T> components = scene
                .GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true));

            return read(components);
        }
        finally
        {
            if (!wasAlreadyLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
