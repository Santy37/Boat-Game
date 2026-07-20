using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DeadmansTales.Configuration;
using DeadmansTales.Networking;
using DeadmansTales.WorldGeneration;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Tilemaps;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Builds the post-Ocean island and the network assets it depends on.
/// The command is deterministic and safe to rerun after palette or marker edits.
/// </summary>
internal static class IslandStageBuilder
{
    private const string MenuPath =
        "Deadman's Tales/Build Networked Post-Ocean Island";

    private const string SourceIslandScenePath =
        "Assets/DeadmansTales/Scenes/Lobby_Island_2D.unity";

    private const string BoatScenePath =
        "Assets/DeadmansTales/Scenes/Boat_Gameplay_2D.unity";

    private const string IslandScenePath =
        "Assets/DeadmansTales/Scenes/Island_After_Ocean_01_2D.unity";

    private const string PlayerPrefabPath =
        "Assets/DeadmansTales/Prefabs/Player_2D_Network.prefab";

    private const string EnemyPrefabPath =
        "Assets/DeadmansTales/Prefabs/basicenemy.prefab";

    private const string NetworkManagerPrefabPath =
        "Assets/DeadmansTales/Prefabs/Networking/NetworkManager.prefab";

    private const string GeneratedNetworkPrefabsPath =
        "Assets/DefaultNetworkPrefabs.asset";

    private const string LegacyNetworkPrefabsPath =
        "Assets/DeadmansTales/Prefabs/Networking/DefaultNetworkPrefabs.asset";

    private const string BootstrapSettingsPath =
        "Assets/DeadmansTales/Resources/Networking/DeadmansNetworkBootstrapSettings.asset";

    private const string GameplayPrefabFolder =
        "Assets/DeadmansTales/Prefabs/Gameplay";

    private const string RunStatePrefabPath =
        GameplayPrefabFolder + "/NetworkRunState.prefab";

    private const string RewardChestPrefabPath =
        GameplayPrefabFolder + "/NetworkRewardChest.prefab";

    private const string WeaponChestPrefabPath =
        GameplayPrefabFolder + "/NetworkRewardChest_Weapon.prefab";

    private const string UpgradeChestPrefabPath =
        GameplayPrefabFolder + "/NetworkRewardChest_Upgrade.prefab";

    private const string CrabSkitterPrefabPath =
        GameplayPrefabFolder + "/Enemy_CrabSkitter.prefab";

    private const string BoneBrutePrefabPath =
        GameplayPrefabFolder + "/Enemy_BoneBrute.prefab";

    private const string PaletteFolder =
        "Assets/DeadmansTales/Palettes";

    private const string PaletteName = "Island_Stage_02_Palette";

    private const string PalettePath =
        PaletteFolder + "/" + PaletteName + ".prefab";

    private const string WaterCollisionTilePath =
        PaletteFolder + "/Island_WaterCollision_Grid.asset";

    private const string ObstacleCollisionTilePath =
        PaletteFolder + "/Island_ObstacleCollision_Grid.asset";

    private const string PropTileFolder =
        "Assets/DeadmansTales/Art_Pixel/Tiles/BeachPropTiles";

    private const string TerrainTileFolder =
        "Assets/DeadmansTales/Art_Pixel/Tiles/BeachTerrainTiles";

    private const string RowboatSpritePath =
        "Assets/DeadmansTales/Art_Pixel/Props/rowboat.png";

    private static readonly Vector3Int[] NeighborOffsets =
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

    private static readonly Vector2Int[] EnemyMarkerPositions =
    {
        new Vector2Int(-4, -4),
        new Vector2Int(0, -3),
        new Vector2Int(4, -2),
        new Vector2Int(-3, 1),
        new Vector2Int(3, 2),
        new Vector2Int(-11, -7),
        new Vector2Int(-12, -2),
        new Vector2Int(-10, 3),
        new Vector2Int(-12, 7),
        new Vector2Int(-8, 9),
        new Vector2Int(9, -7),
        new Vector2Int(12, -4),
        new Vector2Int(10, 1),
        new Vector2Int(13, 5),
        new Vector2Int(9, 8),
        new Vector2Int(-5, 10),
        new Vector2Int(0, 9),
        new Vector2Int(5, 11),
        new Vector2Int(-4, 14),
        new Vector2Int(4, 14),
    };

    private static readonly Vector2Int[] LootMarkerPositions =
    {
        new Vector2Int(-18, -10),
        new Vector2Int(-18, 7),
        new Vector2Int(-13, 11),
        new Vector2Int(-7, 5),
        new Vector2Int(9, -10),
        new Vector2Int(18, 4),
        new Vector2Int(14, 11),
        new Vector2Int(0, 5),
    };

    private const int ShoreReferenceRadius = 3;

    private const int WaterMinX = -38;
    private const int WaterMaxX = 38;
    private const int WaterMinY = -29;
    private const int WaterMaxY = 30;

    private static readonly HashSet<int> LoadedPrefabContentInstanceIds =
        new HashSet<int>();

    private sealed class SourceIslandArt
    {
        public TileBase WaterTile;
        public TileBase WaterCollisionTile;
        public TileBase InteriorGroundTile;
        public readonly List<TileBase> InteriorGroundTiles =
            new List<TileBase>();
        public readonly List<ShoreReferenceSample> GroundShoreSamples =
            new List<ShoreReferenceSample>();
        public readonly List<ShoreReferenceSample> WaterShoreSamples =
            new List<ShoreReferenceSample>();
        public readonly List<TileBase> OpenWaterTiles =
            new List<TileBase>();
        public readonly Dictionary<int, HashSet<TileBase>>
            WaterTilesByWaterMask =
                new Dictionary<int, HashSet<TileBase>>();
        public readonly Dictionary<int, HashSet<TileBase>>
            CoastalWaterTilesByLandMask =
                new Dictionary<int, HashSet<TileBase>>();
        public readonly HashSet<TileBase> AllWaterLayerTiles =
            new HashSet<TileBase>();
        public readonly List<ShoreReferenceSample> AllWaterLayerSamples =
            new List<ShoreReferenceSample>();
        public readonly List<TileBase> PaletteTiles =
            new List<TileBase>();
        public readonly Dictionary<string, PropStamp> PropStamps =
            new Dictionary<string, PropStamp>(StringComparer.Ordinal);
    }

    private sealed class ShoreReferenceSample
    {
        public Vector3Int SourcePosition;
        public int ImmediateLandMask;
        public ulong LandSignature;
        public TileBase Tile;
        public Matrix4x4 Transform = Matrix4x4.identity;
        public TileBase UnderlayWaterTile;
        public Matrix4x4 UnderlayWaterTransform = Matrix4x4.identity;
    }

    private sealed class PropStamp
    {
        public string Name;
        public Vector3Int SourceOrigin;
        public readonly List<StampCell> Cells = new List<StampCell>();

        public int Width =>
            Cells.Count == 0
                ? 0
                : Cells.Max(cell => cell.RelativePosition.x) + 1;

        public int Height =>
            Cells.Count == 0
                ? 0
                : Cells.Max(cell => cell.RelativePosition.y) + 1;
    }

    private readonly struct StampCell
    {
        public readonly Vector3Int RelativePosition;
        public readonly TileBase PropTile;
        public readonly TileBase OverheadTile;
        public readonly TileBase CollisionTile;

        public StampCell(
            Vector3Int relativePosition,
            TileBase propTile,
            TileBase overheadTile,
            TileBase collisionTile
        )
        {
            RelativePosition = relativePosition;
            PropTile = propTile;
            OverheadTile = overheadTile;
            CollisionTile = collisionTile;
        }
    }

    [MenuItem(MenuPath)]
    public static void BuildAll()
    {
        EnsureFolder(PaletteFolder);
        EnsureFolder(GameplayPrefabFolder);

        ConfigureEnemyPrefab();
        ConfigurePlayerPrefab();

        GameObject runStatePrefab = CreateRunStatePrefab();
        GameObject rewardChestPrefab = CreateRewardChestPrefab();
        GameObject weaponChestPrefab = CreateChestVariantPrefab(
            WeaponChestPrefabPath,
            1,
            new Color(1f, 0.78f, 0.55f, 1f)
        );
        GameObject upgradeChestPrefab = CreateChestVariantPrefab(
            UpgradeChestPrefabPath,
            2,
            new Color(0.62f, 0.8f, 1f, 1f)
        );
        GameObject crabSkitterPrefab = CreateEnemyVariantPrefab(
            CrabSkitterPrefabPath,
            maxHealth: 55f,
            chaseSpeed: 4.2f,
            wanderSpeed: 2.2f,
            attackDamage: 6f,
            tint: new Color(1f, 0.58f, 0.45f, 1f),
            scale: 0.85f
        );
        GameObject boneBrutePrefab = CreateEnemyVariantPrefab(
            BoneBrutePrefabPath,
            maxHealth: 190f,
            chaseSpeed: 1.9f,
            wanderSpeed: 1f,
            attackDamage: 18f,
            tint: new Color(0.72f, 0.78f, 0.9f, 1f),
            scale: 1.25f
        );

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        GameObject enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            EnemyPrefabPath
        );
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            PlayerPrefabPath
        );

        GameObject[] enemyPrefabs =
        {
            enemyPrefab,
            crabSkitterPrefab,
            boneBrutePrefab,
        };
        GameObject[] lootChestPrefabs =
        {
            rewardChestPrefab,
            weaponChestPrefab,
            upgradeChestPrefab,
        };

        ConfigureNetworkingAssets(
            playerPrefab,
            enemyPrefab,
            rewardChestPrefab,
            runStatePrefab,
            new[]
            {
                crabSkitterPrefab,
                boneBrutePrefab,
                weaponChestPrefab,
                upgradeChestPrefab,
            }
        );

        ConfigureLobbyRuntimeEnemies(enemyPrefab);
        SourceIslandArt sourceArt = CaptureSourceIslandArt();
        BuildPalette();
        Tile waterCollisionTile = CreateGridCollisionTile(
            WaterCollisionTilePath
        );
        Tile obstacleCollisionTile = CreateGridCollisionTile(
            ObstacleCollisionTilePath
        );
        BuildIslandScene(
            sourceArt,
            enemyPrefabs,
            lootChestPrefabs,
            weaponChestPrefab,
            waterCollisionTile,
            obstacleCollisionTile
        );
        AddIslandPortalToBoatScene();
        EnsureBuildSettings();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        NetworkSceneIdentityRepair.RepairNow();
        AssetDatabase.Refresh();

        Debug.Log(
            "[Island Builder] Networked post-Ocean island, palette, " +
            "prefabs, portals, and Build Settings are ready."
        );
    }

    public static void BuildAllFromCommandLine()
    {
        BuildAll();
    }

    public static void CapturePreviewFromCommandLine()
    {
        Scene scene = EditorSceneManager.OpenScene(
            IslandScenePath,
            OpenSceneMode.Single
        );

        Camera camera = scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Camera>(true))
            .FirstOrDefault();

        if (camera == null)
        {
            throw new InvalidOperationException(
                "The island scene has no camera for preview rendering."
            );
        }

        Camera2DFollow follow = camera.GetComponent<Camera2DFollow>();
        bool originalFollowEnabled = follow != null && follow.enabled;
        Vector3 originalPosition = camera.transform.position;
        Quaternion originalRotation = camera.transform.rotation;
        bool originalOrthographic = camera.orthographic;
        float originalOrthographicSize = camera.orthographicSize;
        bool originalHdr = camera.allowHDR;

        try
        {
            if (follow != null)
            {
                follow.enabled = false;
            }

            string outputFolder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Logs"
            );
            Directory.CreateDirectory(outputFolder);

            camera.orthographic = true;
            camera.allowHDR = false;
            camera.transform.SetPositionAndRotation(
                new Vector3(0f, 0.5f, -10f),
                Quaternion.identity
            );
            camera.orthographicSize = 21.5f;
            RenderCameraPreview(
                camera,
                Path.Combine(outputFolder, "codex-island-preview.png"),
                1440,
                900
            );

            camera.transform.SetPositionAndRotation(
                new Vector3(0f, -9f, -10f),
                Quaternion.identity
            );
            camera.orthographicSize = 8f;
            RenderCameraPreview(
                camera,
                Path.Combine(outputFolder, "codex-island-spawn-preview.png"),
                1280,
                720
            );
        }
        finally
        {
            camera.transform.SetPositionAndRotation(
                originalPosition,
                originalRotation
            );
            camera.orthographic = originalOrthographic;
            camera.orthographicSize = originalOrthographicSize;
            camera.allowHDR = originalHdr;

            if (follow != null)
            {
                follow.enabled = originalFollowEnabled;
            }
        }
    }

    private static void RenderCameraPreview(
        Camera camera,
        string outputPath,
        int width,
        int height
    )
    {
        RenderTexture renderTexture = new RenderTexture(
            width,
            height,
            24,
            RenderTextureFormat.ARGB32
        );
        Texture2D screenshot = new Texture2D(
            width,
            height,
            TextureFormat.RGBA32,
            false
        );
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;

        try
        {
            camera.targetTexture = renderTexture;
            RenderTexture.active = renderTexture;
            camera.Render();
            screenshot.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            screenshot.Apply();
            File.WriteAllBytes(outputPath, screenshot.EncodeToPNG());
            Debug.Log($"[Island Builder] Preview written to {outputPath}");
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            UnityEngine.Object.DestroyImmediate(screenshot);
            UnityEngine.Object.DestroyImmediate(renderTexture);
        }
    }

    private static void ConfigureEnemyPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(EnemyPrefabPath);

        try
        {
            EnsureComponent<NetworkObject>(root);
            NetworkTransform networkTransform =
                EnsureComponent<NetworkTransform>(root);
            NetworkRigidbody2D networkRigidbody =
                EnsureComponent<NetworkRigidbody2D>(root);

            Rigidbody2D body = EnsureComponent<Rigidbody2D>(root);
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            networkRigidbody.UseRigidBodyForMotion = true;
            networkRigidbody.AutoUpdateKinematicState = true;

            EditorUtility.SetDirty(networkTransform);
            PrefabUtility.SaveAsPrefabAsset(root, EnemyPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigurePlayerPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);

        try
        {
            EnsureComponent<NetworkInteractionController2D>(root);
            NetworkInteractionInput2D interactionInput =
                EnsureComponent<NetworkInteractionInput2D>(root);
            EnsureComponent<NetworkPlayerLoadout>(root);
            PlayerAttack attack = EnsureComponent<PlayerAttack>(root);
            SetSerializedFloat(attack, "attackCooldown", 0.4f);
            SetSerializedFloat(attack, "inputBufferSeconds", 0.12f);

            // Everything a dead player must not do: move, attack, interact.
            PlayerHealth health = EnsureComponent<PlayerHealth>(root);
            SerializedObject healthObject = new SerializedObject(health);
            SerializedProperty disableOnDeath =
                healthObject.FindProperty("disableOnDeath");
            MonoBehaviour[] deathDisabled =
            {
                root.GetComponent<TopDownNetworkPlayer2D>(),
                attack,
                interactionInput,
            };
            disableOnDeath.arraySize = deathDisabled.Length;
            for (int index = 0; index < deathDisabled.Length; index++)
            {
                disableOnDeath
                    .GetArrayElementAtIndex(index)
                    .objectReferenceValue = deathDisabled[index];
            }

            healthObject.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static GameObject CreateRunStatePrefab()
    {
        GameObject root = LoadOrCreatePrefabContents(
            RunStatePrefabPath,
            "NetworkRunState"
        );

        try
        {
            EnsureComponent<NetworkObject>(root);
            EnsureComponent<NetworkRunState>(root);
            EnsureComponent<NetworkRunConfigAuthority>(root);
            PrefabUtility.SaveAsPrefabAsset(root, RunStatePrefabPath);
        }
        finally
        {
            UnloadOrDestroyPrefabContents(root, RunStatePrefabPath);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(RunStatePrefabPath);
    }

    private static GameObject CreateRewardChestPrefab()
    {
        GameObject root = LoadOrCreatePrefabContents(
            RewardChestPrefabPath,
            "NetworkRewardChest"
        );

        try
        {
            for (int index = root.transform.childCount - 1; index >= 0; index--)
            {
                UnityEngine.Object.DestroyImmediate(
                    root.transform.GetChild(index).gameObject
                );
            }

            EnsureComponent<NetworkObject>(root);
            BoxCollider2D collider = EnsureComponent<BoxCollider2D>(root);
            collider.isTrigger = false;
            collider.size = new Vector2(1.7f, 1.25f);
            collider.offset = new Vector2(0f, 0.25f);

            NetworkRewardChest chest =
                EnsureComponent<NetworkRewardChest>(root);

            GameObject closedVisual = CreateChestVisual(
                root.transform,
                "ClosedVisual",
                Color.white
            );

            GameObject openedVisual = CreateChestVisual(
                root.transform,
                "OpenedVisual",
                new Color(0.55f, 0.55f, 0.55f, 0.75f)
            );
            openedVisual.SetActive(false);

            SetSerializedObject(chest, "closedVisual", closedVisual);
            SetSerializedObject(chest, "openedVisual", openedVisual);
            SetSerializedBool(chest, "allowRepeatedInteraction", false);

            PrefabUtility.SaveAsPrefabAsset(root, RewardChestPrefabPath);
        }
        finally
        {
            UnloadOrDestroyPrefabContents(root, RewardChestPrefabPath);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(
            RewardChestPrefabPath
        );
    }

    /// <summary>
    /// Creates (or refreshes) a reward-chest prefab variant with a different
    /// reward kind and tinted closed visual so players can tell chest types
    /// apart at a glance.
    /// </summary>
    private static GameObject CreateChestVariantPrefab(
        string variantPath,
        int rewardKindIndex,
        Color closedTint
    )
    {
        EnsurePrefabVariantExists(RewardChestPrefabPath, variantPath);

        GameObject root = PrefabUtility.LoadPrefabContents(variantPath);

        try
        {
            NetworkRewardChest chest =
                root.GetComponent<NetworkRewardChest>();
            SerializedObject chestObject = new SerializedObject(chest);
            chestObject.FindProperty("rewardKind").enumValueIndex =
                rewardKindIndex;
            chestObject.ApplyModifiedPropertiesWithoutUndo();

            Transform closedVisual = root.transform.Find("ClosedVisual");
            if (closedVisual != null)
            {
                foreach (SpriteRenderer renderer in closedVisual
                    .GetComponentsInChildren<SpriteRenderer>(true))
                {
                    renderer.color = closedTint;
                }
            }

            PrefabUtility.SaveAsPrefabAsset(root, variantPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(variantPath);
    }

    /// <summary>
    /// Creates (or refreshes) an enemy prefab variant with distinct combat
    /// stats, tint, and size so island encounters have visible variety.
    /// </summary>
    private static GameObject CreateEnemyVariantPrefab(
        string variantPath,
        float maxHealth,
        float chaseSpeed,
        float wanderSpeed,
        float attackDamage,
        Color tint,
        float scale
    )
    {
        EnsurePrefabVariantExists(EnemyPrefabPath, variantPath);

        GameObject root = PrefabUtility.LoadPrefabContents(variantPath);

        try
        {
            root.transform.localScale = new Vector3(scale, scale, 1f);

            Enemy enemy = root.GetComponent<Enemy>();
            if (enemy != null)
            {
                SetSerializedFloat(enemy, "maxHealth", maxHealth);
            }

            EnemyAI ai = root.GetComponentInChildren<EnemyAI>(true);
            if (ai != null)
            {
                SetSerializedFloat(ai, "chaseSpeed", chaseSpeed);
                SetSerializedFloat(ai, "wanderSpeed", wanderSpeed);
            }

            EnemyAttack enemyAttack =
                root.GetComponentInChildren<EnemyAttack>(true);
            if (enemyAttack != null)
            {
                SetSerializedFloat(enemyAttack, "damage", attackDamage);
            }

            foreach (SpriteRenderer renderer in root
                .GetComponentsInChildren<SpriteRenderer>(true))
            {
                renderer.color = tint;
            }

            PrefabUtility.SaveAsPrefabAsset(root, variantPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(variantPath);
    }

    private static void EnsurePrefabVariantExists(
        string sourcePath,
        string variantPath
    )
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(variantPath) != null)
        {
            return;
        }

        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(
            sourcePath
        );

        if (source == null)
        {
            throw new InvalidOperationException(
                $"Cannot create a variant of a missing prefab: {sourcePath}"
            );
        }

        GameObject instance =
            (GameObject)PrefabUtility.InstantiatePrefab(source);

        try
        {
            PrefabUtility.SaveAsPrefabAsset(instance, variantPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    private static GameObject CreateChestVisual(
        Transform parent,
        string name,
        Color tint
    )
    {
        GameObject visualRoot = new GameObject(name);
        visualRoot.transform.SetParent(parent, false);

        int[] tileIndices = { 63, 64, 78, 79 };
        Vector2[] offsets =
        {
            new Vector2(-0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(-0.5f, -0.5f),
            new Vector2(0.5f, -0.5f),
        };

        for (int index = 0; index < tileIndices.Length; index++)
        {
            Tile tile = LoadPropTile(tileIndices[index]);
            if (tile == null || tile.sprite == null)
            {
                continue;
            }

            GameObject tileObject = new GameObject($"ChestTile_{index}");
            tileObject.transform.SetParent(visualRoot.transform, false);
            tileObject.transform.localPosition = offsets[index];

            SpriteRenderer renderer =
                tileObject.AddComponent<SpriteRenderer>();
            renderer.sprite = tile.sprite;
            renderer.color = tint;
            renderer.sortingOrder = 15;
        }

        return visualRoot;
    }

    private static void ConfigureNetworkingAssets(
        GameObject playerPrefab,
        GameObject enemyPrefab,
        GameObject rewardChestPrefab,
        GameObject runStatePrefab,
        GameObject[] extraSpawnablePrefabs
    )
    {
        NetworkPrefabsList generatedList =
            AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(
                GeneratedNetworkPrefabsPath
            );

        if (generatedList == null)
        {
            generatedList = ScriptableObject.CreateInstance<NetworkPrefabsList>();
            generatedList.name = "DefaultNetworkPrefabs";
            AssetDatabase.CreateAsset(
                generatedList,
                GeneratedNetworkPrefabsPath
            );
        }

        SetSerializedBool(generatedList, "IsDefault", true);

        GameObject[] requiredPrefabs =
        {
            playerPrefab,
            enemyPrefab,
            rewardChestPrefab,
            runStatePrefab,
        };

        foreach (GameObject prefab in requiredPrefabs)
        {
            EnsureRegistered(generatedList, prefab);
        }

        foreach (GameObject prefab in extraSpawnablePrefabs)
        {
            EnsureRegistered(generatedList, prefab);
        }

        NetworkPrefabsList legacyList =
            AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(
                LegacyNetworkPrefabsPath
            );

        if (legacyList != null)
        {
            SetSerializedBool(legacyList, "IsDefault", false);
        }

        GameObject managerRoot = PrefabUtility.LoadPrefabContents(
            NetworkManagerPrefabPath
        );

        try
        {
            NetworkManager manager = managerRoot.GetComponent<NetworkManager>();
            manager.NetworkConfig.PlayerPrefab = playerPrefab;
            manager.NetworkConfig.EnableSceneManagement = true;
            manager.NetworkConfig.ForceSamePrefabs = true;
            manager.NetworkConfig.NetworkTopology =
                NetworkTopologyTypes.ClientServer;
            manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Clear();
            manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(generatedList);

            EditorUtility.SetDirty(manager);
            PrefabUtility.SaveAsPrefabAsset(
                managerRoot,
                NetworkManagerPrefabPath
            );
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(managerRoot);
        }

        DeadmansNetworkBootstrapSettings settings =
            AssetDatabase.LoadAssetAtPath<DeadmansNetworkBootstrapSettings>(
                BootstrapSettingsPath
            );

        if (settings == null)
        {
            throw new InvalidOperationException(
                "Bootstrap settings asset was not found."
            );
        }

        SerializedObject settingsObject = new SerializedObject(settings);
        settingsObject.FindProperty("playerPrefab").objectReferenceValue =
            playerPrefab;

        SerializedProperty additional =
            settingsObject.FindProperty("additionalNetworkPrefabs");

        List<GameObject> additionalPrefabs =
            settings.AdditionalNetworkPrefabs
                .Where(prefab => prefab != null)
                .Distinct()
                .ToList();

        foreach (GameObject required in new[]
        {
            enemyPrefab,
            rewardChestPrefab,
            runStatePrefab,
        }.Concat(extraSpawnablePrefabs))
        {
            if (required != null && !additionalPrefabs.Contains(required))
            {
                additionalPrefabs.Add(required);
            }
        }

        additional.arraySize = additionalPrefabs.Count;
        for (int index = 0; index < additionalPrefabs.Count; index++)
        {
            additional.GetArrayElementAtIndex(index).objectReferenceValue =
                additionalPrefabs[index];
        }

        settingsObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(settings);
    }

    private static void ConfigureLobbyRuntimeEnemies(GameObject enemyPrefab)
    {
        Scene scene = EditorSceneManager.OpenScene(
            SourceIslandScenePath,
            OpenSceneMode.Single
        );

        NetworkSceneEnemySpawner2D existingSpawner = scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<
                NetworkSceneEnemySpawner2D>(true))
            .FirstOrDefault();

        Vector2[] positions = scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Enemy>(true))
            .Select(enemy => (Vector2)enemy.transform.position)
            .OrderBy(position => position.x)
            .ThenBy(position => position.y)
            .ToArray();

        if (positions.Length == 0 && existingSpawner != null)
        {
            SerializedObject existing = new SerializedObject(existingSpawner);
            SerializedProperty serializedPositions =
                existing.FindProperty("spawnPositions");
            positions = new Vector2[serializedPositions.arraySize];

            for (int index = 0; index < positions.Length; index++)
            {
                positions[index] = serializedPositions
                    .GetArrayElementAtIndex(index)
                    .vector2Value;
            }
        }

        if (positions.Length == 0)
        {
            throw new InvalidOperationException(
                "The lobby needs at least one authored enemy spawn position."
            );
        }

        GameObject[] authoredEnemyRoots = scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Enemy>(true))
            .Select(enemy =>
                PrefabUtility.GetOutermostPrefabInstanceRoot(enemy.gameObject)
                    ?? enemy.gameObject)
            .Distinct()
            .ToArray();

        foreach (GameObject authoredEnemy in authoredEnemyRoots)
        {
            UnityEngine.Object.DestroyImmediate(authoredEnemy);
        }

        if (existingSpawner != null)
        {
            UnityEngine.Object.DestroyImmediate(existingSpawner.gameObject);
        }

        GameObject spawnerObject = new GameObject("Lobby_Runtime_Enemies");
        NetworkSceneEnemySpawner2D spawner =
            spawnerObject.AddComponent<NetworkSceneEnemySpawner2D>();
        SerializedObject serializedSpawner = new SerializedObject(spawner);
        serializedSpawner.FindProperty("enemyPrefab").objectReferenceValue =
            enemyPrefab;

        SerializedProperty spawnPositions =
            serializedSpawner.FindProperty("spawnPositions");
        spawnPositions.arraySize = positions.Length;
        for (int index = 0; index < positions.Length; index++)
        {
            spawnPositions.GetArrayElementAtIndex(index).vector2Value =
                positions[index];
        }

        serializedSpawner.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);

        if (!EditorSceneManager.SaveScene(scene, SourceIslandScenePath))
        {
            throw new InvalidOperationException(
                "Unity failed to save the runtime-spawned lobby enemies."
            );
        }
    }

    private static SourceIslandArt CaptureSourceIslandArt()
    {
        Scene sourceScene = EditorSceneManager.OpenScene(
            SourceIslandScenePath,
            OpenSceneMode.Single
        );

        Tilemap water = FindTilemap(sourceScene, "Tilemap_Water");
        Tilemap ground = FindTilemap(sourceScene, "Tilemap_Ground");
        Tilemap props = FindTilemap(sourceScene, "Tilemap_Props");
        Tilemap overhead = FindTilemap(sourceScene, "Tilemap_Overhead");
        Tilemap waterCollision = FindTilemap(
            sourceScene,
            "Tilemap_WaterCollision"
        );
        Tilemap obstacleCollision = FindTilemap(
            sourceScene,
            "Tilemap_ObstacleCollision"
        );

        Dictionary<Vector3Int, TileBase> waterCells = CaptureCells(water);
        Dictionary<Vector3Int, TileBase> groundCells = CaptureCells(ground);
        Dictionary<Vector3Int, TileBase> propCells = CaptureCells(props);
        Dictionary<Vector3Int, TileBase> overheadCells = CaptureCells(overhead);
        Dictionary<Vector3Int, TileBase> waterCollisionCells =
            CaptureCells(waterCollision);
        Dictionary<Vector3Int, TileBase> obstacleCells =
            CaptureCells(obstacleCollision);

        SourceIslandArt art = new SourceIslandArt
        {
            WaterTile = MostCommonTile(waterCells.Values),
            WaterCollisionTile = MostCommonTile(
                waterCollisionCells.Values
            ),
            InteriorGroundTile = MostCommonTile(groundCells.Values),
        };

        foreach (int interiorIndex in Enumerable.Range(0, 7))
        {
            Tile tile = LoadPropTile(interiorIndex);
            if (tile != null)
            {
                art.InteriorGroundTiles.Add(tile);
            }
        }

        HashSet<Vector3Int> sourceLandCells =
            groundCells.Keys.ToHashSet();

        foreach (KeyValuePair<Vector3Int, TileBase> groundCell in groundCells)
        {
            int mask = GetNeighborMask(sourceLandCells, groundCell.Key);
            if (mask == 255)
            {
                continue;
            }

            ShoreReferenceSample sample = new ShoreReferenceSample
            {
                SourcePosition = groundCell.Key,
                ImmediateLandMask = mask,
                LandSignature = GetLandSignature(
                    sourceLandCells,
                    groundCell.Key
                ),
                Tile = groundCell.Value,
                Transform = ground.GetTransformMatrix(groundCell.Key),
            };

            if (
                waterCells.TryGetValue(
                    groundCell.Key,
                    out TileBase underlayWater
                )
            )
            {
                sample.UnderlayWaterTile = underlayWater;
                sample.UnderlayWaterTransform =
                    water.GetTransformMatrix(groundCell.Key);
            }

            art.GroundShoreSamples.Add(sample);
        }

        foreach (KeyValuePair<Vector3Int, TileBase> waterCell in waterCells)
        {
            if (sourceLandCells.Contains(waterCell.Key))
            {
                continue;
            }

            int mask = GetNeighborMask(sourceLandCells, waterCell.Key);
            if (mask == 0)
            {
                // Keep repeated entries so deterministic selection naturally
                // preserves the lobby's A1_62/A1_191 frequency balance.
                art.OpenWaterTiles.Add(waterCell.Value);
                continue;
            }

            art.WaterShoreSamples.Add(
                new ShoreReferenceSample
                {
                    SourcePosition = waterCell.Key,
                    ImmediateLandMask = mask,
                    LandSignature = GetLandSignature(
                        sourceLandCells,
                        waterCell.Key
                    ),
                    Tile = waterCell.Value,
                    Transform = water.GetTransformMatrix(waterCell.Key),
                }
            );
        }

        if (art.GroundShoreSamples.Count == 0)
        {
            throw new InvalidOperationException(
                "The lobby ground contains no shoreline reference cells."
            );
        }

        if (art.WaterShoreSamples.Count == 0)
        {
            throw new InvalidOperationException(
                "The lobby water contains no A1 shoreline transition cells."
            );
        }

        // The lobby also authors its outer map boundary and every coastal
        // water cell. Record which tiles it uses for each water-adjacency
        // mask and each land-adjacency mask so the island reproduces both
        // conventions simultaneously.
        HashSet<Vector3Int> paintedWaterCells =
            waterCells.Keys.ToHashSet();

        foreach (KeyValuePair<Vector3Int, TileBase> waterCell in waterCells)
        {
            int waterMask = GetNeighborMask(
                paintedWaterCells,
                waterCell.Key
            );
            bool underLand = sourceLandCells.Contains(waterCell.Key);
            int coastalMask = underLand
                ? 0
                : GetNeighborMask(sourceLandCells, waterCell.Key);

            if (waterMask != 255)
            {
                if (
                    !art.WaterTilesByWaterMask.TryGetValue(
                        waterMask,
                        out HashSet<TileBase> waterMaskTiles
                    )
                )
                {
                    waterMaskTiles = new HashSet<TileBase>();
                    art.WaterTilesByWaterMask[waterMask] = waterMaskTiles;
                }

                waterMaskTiles.Add(waterCell.Value);
            }

            if (coastalMask != 0)
            {
                if (
                    !art.CoastalWaterTilesByLandMask.TryGetValue(
                        coastalMask,
                        out HashSet<TileBase> coastalTiles
                    )
                )
                {
                    coastalTiles = new HashSet<TileBase>();
                    art.CoastalWaterTilesByLandMask[coastalMask] =
                        coastalTiles;
                }

                coastalTiles.Add(waterCell.Value);
            }

            art.AllWaterLayerTiles.Add(waterCell.Value);
            art.AllWaterLayerSamples.Add(
                new ShoreReferenceSample
                {
                    SourcePosition = waterCell.Key,
                    Tile = waterCell.Value,
                    Transform = water.GetTransformMatrix(waterCell.Key),
                }
            );
        }

        if (art.OpenWaterTiles.Count == 0 && art.WaterTile != null)
        {
            art.OpenWaterTiles.Add(art.WaterTile);
        }

        BuildNamedPropStamps(
            propCells,
            overheadCells,
            obstacleCells,
            art.PropStamps
        );

        HashSet<TileBase> allTiles = new HashSet<TileBase>();
        AddTiles(allTiles, waterCells.Values);
        AddTiles(allTiles, groundCells.Values);
        AddTiles(allTiles, propCells.Values);
        AddTiles(allTiles, overheadCells.Values);
        AddTiles(allTiles, waterCollisionCells.Values);
        AddTiles(allTiles, obstacleCells.Values);

        foreach (int detailIndex in new[] { 0, 1, 2, 3, 4, 5, 6 })
        {
            Tile detailTile = LoadPropTile(detailIndex);
            if (detailTile != null)
            {
                allTiles.Add(detailTile);
            }
        }

        art.PaletteTiles.AddRange(
            allTiles
                .Where(tile => tile != null)
                .OrderBy(tile => AssetDatabase.GetAssetPath(tile))
                .ThenBy(tile => tile.name)
        );

        if (
            art.WaterTile == null ||
            art.WaterCollisionTile == null ||
            art.InteriorGroundTile == null
        )
        {
            throw new InvalidOperationException(
                "Could not learn the required water, ground, and collision " +
                "tiles from Lobby_Island_2D."
            );
        }

        return art;
    }

    private static void BuildPalette()
    {
        GameObject paletteAsset =
            AssetDatabase.LoadAssetAtPath<GameObject>(PalettePath);

        if (paletteAsset == null)
        {
            paletteAsset = GridPaletteUtility.CreateNewPalette(
                PaletteFolder,
                PaletteName,
                GridLayout.CellLayout.Rectangle,
                GridPalette.CellSizing.Manual,
                new Vector3(1f, 1f, 0f),
                GridLayout.CellSwizzle.XYZ
            );
        }

        string assetPath = AssetDatabase.GetAssetPath(paletteAsset);
        GameObject contents = PrefabUtility.LoadPrefabContents(assetPath);

        try
        {
            Tilemap tilemap = contents.GetComponentInChildren<Tilemap>(true);
            tilemap.ClearAllTiles();

            // Preserve both FinalBossBlues source atlases exactly. Designers
            // can recognize/select the same neighboring tiles as the lobby
            // instead of searching an alphabetically scrambled subset.
            for (int index = 0; index < 192; index++)
            {
                Tile terrainTile = LoadTerrainTile(index);
                if (terrainTile != null)
                {
                    tilemap.SetTile(
                        new Vector3Int(index % 16, -(index / 16), 0),
                        terrainTile
                    );
                }
            }

            const int propPaletteOffset = 18;
            for (int index = 0; index < 206; index++)
            {
                Tile propTile = LoadPropTile(index);
                if (propTile != null)
                {
                    tilemap.SetTile(
                        new Vector3Int(
                            propPaletteOffset + index % 15,
                            -(index / 15),
                            0
                        ),
                        propTile
                    );
                }
            }

            tilemap.CompressBounds();
            PrefabUtility.SaveAsPrefabAsset(contents, assetPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static Tile CreateGridCollisionTile(string assetPath)
    {
        Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(assetPath);
        if (tile == null)
        {
            tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(tile, assetPath);
        }

        // Hidden collision layers do not need a sprite. Grid geometry avoids
        // runtime sprite-outline generation on unreadable pixel textures.
        tile.sprite = null;
        tile.color = Color.white;
        tile.transform = Matrix4x4.identity;
        tile.colliderType = Tile.ColliderType.Grid;
        EditorUtility.SetDirty(tile);
        return tile;
    }

    private static void BuildIslandScene(
        SourceIslandArt sourceArt,
        GameObject[] enemyPrefabs,
        GameObject[] lootChestPrefabs,
        GameObject guaranteedRewardPrefab,
        TileBase waterCollisionGridTile,
        TileBase obstacleCollisionGridTile
    )
    {
        Scene scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Additive
        );
        scene.name = "Island_After_Ocean_01_2D";

        // Keep the source lobby scene loaded while painting. Several of its
        // TileBase references are scene-owned objects and become Unity-null if
        // the source scene is closed before SetTile completes.
        SceneManager.SetActiveScene(scene);

        GameObject environmentRoot = new GameObject(
            "Island_Stage_02_Environment"
        );
        GameObject gridObject = new GameObject("Grid_Island_Stage_02");
        gridObject.transform.SetParent(environmentRoot.transform, false);
        Grid grid = gridObject.AddComponent<Grid>();
        grid.cellSize = new Vector3(1f, 1f, 0f);

        Tilemap water = CreateTilemap(gridObject, "Tilemap_Water", 0);
        Tilemap ground = CreateTilemap(gridObject, "Tilemap_Ground", 1);
        Tilemap groundDetail = CreateTilemap(
            gridObject,
            "Tilemap_GroundDetail",
            2
        );
        Tilemap props = CreateTilemap(gridObject, "Tilemap_Props", 5);
        Tilemap overhead = CreateTilemap(
            gridObject,
            "Tilemap_Overhead",
            20
        );
        overhead.GetComponent<TilemapRenderer>().mode =
            TilemapRenderer.Mode.Individual;

        Tilemap waterCollision = CreateCollisionTilemap(
            gridObject,
            "Tilemap_WaterCollision"
        );
        Tilemap obstacleCollision = CreateCollisionTilemap(
            gridObject,
            "Tilemap_ObstacleCollision"
        );

        HashSet<Vector3Int> landCells = BuildLandCells(sourceArt);
        ValidateTerrainMasks(landCells, sourceArt);

        // Plain open-water tiles are valid mask matches but hide the painted
        // shoreline. Whenever the lobby offers a real A1 transition sample
        // for a mask, prefer it over plain water.
        HashSet<TileBase> plainWaterTiles = new HashSet<TileBase>(
            sourceArt.OpenWaterTiles
        );
        if (sourceArt.WaterTile != null)
        {
            plainWaterTiles.Add(sourceArt.WaterTile);
        }

        for (int x = WaterMinX; x <= WaterMaxX; x++)
        {
            for (int y = WaterMinY; y <= WaterMaxY; y++)
            {
                Vector3Int cell = new Vector3Int(x, y, 0);
                if (landCells.Contains(cell))
                {
                    continue;
                }

                int mask = GetNeighborMask(landCells, cell);
                if (mask == 0)
                {
                    water.SetTile(cell, SelectOpenWaterTile(sourceArt, cell));
                }
                else
                {
                    ShoreReferenceSample reference = SelectShoreReference(
                        sourceArt.WaterShoreSamples,
                        landCells,
                        cell,
                        mask,
                        "water",
                        plainWaterTiles
                    );
                    water.SetTile(cell, reference.Tile);
                    water.SetTransformMatrix(cell, reference.Transform);
                }

                // Match the lobby: the ocean is a solid boundary everywhere
                // outside the sand, except for the explicitly cleared pier.
                waterCollision.SetTile(cell, waterCollisionGridTile);
            }
        }

        foreach (Vector3Int cell in landCells)
        {
            int mask = GetNeighborMask(landCells, cell);
            if (mask == 255)
            {
                ground.SetTile(
                    cell,
                    SelectInteriorGroundTile(sourceArt, cell)
                );
                ground.SetTransformMatrix(
                    cell,
                    GetInteriorSandTransform(cell)
                );

                // Continuous water under the whole landmass keeps every
                // coastal water cell's painted-water neighborhood complete,
                // exactly like a full hand-painted background layer.
                water.SetTile(cell, SelectOpenWaterTile(sourceArt, cell));
                continue;
            }

            ShoreReferenceSample reference = SelectShoreReference(
                sourceArt.GroundShoreSamples,
                landCells,
                cell,
                mask,
                "ground"
            );
            ground.SetTile(cell, reference.Tile);
            ground.SetTransformMatrix(cell, reference.Transform);

            // The lobby's shoreline is a coordinated two-layer stamp. Its
            // A1 shallow-water transition continues beneath the B-series sand
            // edge, preventing deep water from touching the beach directly.
            if (reference.UnderlayWaterTile != null)
            {
                water.SetTile(cell, reference.UnderlayWaterTile);
                water.SetTransformMatrix(
                    cell,
                    reference.UnderlayWaterTransform
                );
            }
            else
            {
                water.SetTile(cell, SelectOpenWaterTile(sourceArt, cell));
            }
        }

        PaintGroundDetails(groundDetail, landCells);
        PaintPropStamps(
            sourceArt.PropStamps,
            landCells,
            props,
            overhead,
            waterCollision,
            obstacleCollision,
            obstacleCollisionGridTile
        );

        RepaintWaterEdgesToMatchLobby(water, sourceArt, landCells);

        water.CompressBounds();
        ground.CompressBounds();
        groundDetail.CompressBounds();
        props.CompressBounds();
        overhead.CompressBounds();
        waterCollision.CompressBounds();
        obstacleCollision.CompressBounds();

        ValidatePaintedTilemaps(
            water,
            ground,
            groundDetail,
            props,
            overhead,
            waterCollision,
            obstacleCollision
        );

        GameObject gameplayRoot = new GameObject(
            "Island_Stage_02_Gameplay"
        );

        CreatePlayerSpawns(gameplayRoot.transform);
        CreateStageSystems(gameplayRoot.transform);
        CreateContentMarkers(
            gameplayRoot.transform,
            enemyPrefabs,
            lootChestPrefabs,
            guaranteedRewardPrefab
        );
        CreateArrivalAndExit(gameplayRoot.transform);
        CreateCameraAndLighting();

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, IslandScenePath))
        {
            throw new InvalidOperationException(
                $"Unity failed to save the island scene at {IslandScenePath}."
            );
        }
    }

    private static void ValidatePaintedTilemaps(params Tilemap[] tilemaps)
    {
        foreach (Tilemap tilemap in tilemaps)
        {
            if (tilemap == null || tilemap.GetUsedTilesCount() == 0)
            {
                throw new InvalidOperationException(
                    $"Island layer '{tilemap?.name ?? "<missing>"}' has no " +
                    "painted tiles before the scene is saved."
                );
            }
        }
    }

    private static void CreatePlayerSpawns(Transform gameplayRoot)
    {
        GameObject spawnRoot = new GameObject("PlayerSpawns");
        spawnRoot.transform.SetParent(gameplayRoot, false);

        Vector2[] positions =
        {
            new Vector2(-1.5f, -10f),
            new Vector2(1.5f, -10f),
            new Vector2(-1.5f, -8f),
            new Vector2(1.5f, -8f),
        };

        for (int index = 0; index < positions.Length; index++)
        {
            GameObject marker = new GameObject($"PlayerSpawn_{index}");
            marker.transform.SetParent(spawnRoot.transform, false);
            marker.transform.position = positions[index];
            marker.AddComponent<PlayerSpawnPoint2D>();
        }
    }

    private static void CreateStageSystems(Transform gameplayRoot)
    {
        GameObject systems = new GameObject("StageSystems");
        systems.transform.SetParent(gameplayRoot, false);

        StageSeedProvider seedProvider =
            systems.AddComponent<StageSeedProvider>();
        SeededIslandContentGenerator generator =
            systems.AddComponent<SeededIslandContentGenerator>();

        SetSerializedObject(generator, "seedProvider", seedProvider);
        SetSerializedBool(generator, "generateAutomatically", true);

        SerializedObject generatorObject = new SerializedObject(generator);
        SerializedProperty budgets =
            generatorObject.FindProperty("contentBudgets");
        budgets.arraySize = 3;

        ConfigureBudget(
            budgets.GetArrayElementAtIndex(0),
            SeededContentCategory.Enemy,
            8,
            12
        );
        ConfigureBudget(
            budgets.GetArrayElementAtIndex(1),
            SeededContentCategory.Loot,
            2,
            3
        );
        ConfigureBudget(
            budgets.GetArrayElementAtIndex(2),
            SeededContentCategory.Reward,
            1,
            1
        );

        generatorObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CreateContentMarkers(
        Transform gameplayRoot,
        GameObject[] enemyPrefabs,
        GameObject[] lootChestPrefabs,
        GameObject guaranteedRewardPrefab
    )
    {
        GameObject markerRoot = new GameObject("SeededContentMarkers");
        markerRoot.transform.SetParent(gameplayRoot, false);

        GameObject enemyRoot = new GameObject("EnemyMarkers");
        enemyRoot.transform.SetParent(markerRoot.transform, false);

        for (int index = 0; index < EnemyMarkerPositions.Length; index++)
        {
            CreateMarker(
                enemyRoot.transform,
                $"EnemyMarker_{index:D2}",
                EnemyMarkerPositions[index],
                SeededContentCategory.Enemy,
                enemyPrefabs,
                false,
                0.72f
            );
        }

        GameObject lootRoot = new GameObject("LootMarkers");
        lootRoot.transform.SetParent(markerRoot.transform, false);

        for (int index = 0; index < LootMarkerPositions.Length; index++)
        {
            CreateMarker(
                lootRoot.transform,
                $"LootMarker_{index:D2}",
                LootMarkerPositions[index],
                SeededContentCategory.Loot,
                lootChestPrefabs,
                false,
                0.8f
            );
        }

        GameObject rewardRoot = new GameObject("GuaranteedRewardMarker");
        rewardRoot.transform.SetParent(markerRoot.transform, false);

        CreateMarker(
            rewardRoot.transform,
            "FinalReward",
            new Vector2(0f, 14f),
            SeededContentCategory.Reward,
            new[] { guaranteedRewardPrefab },
            true,
            1f
        );
    }

    private static void CreateMarker(
        Transform parent,
        string markerName,
        Vector2 position,
        SeededContentCategory category,
        GameObject[] prefabCandidates,
        bool alwaysSpawn,
        float chance
    )
    {
        GameObject markerObject = new GameObject(markerName);
        markerObject.transform.SetParent(parent, false);
        markerObject.transform.position = position;

        SeededSpawnMarker2D marker =
            markerObject.AddComponent<SeededSpawnMarker2D>();
        SerializedObject markerSerialized = new SerializedObject(marker);

        markerSerialized.FindProperty("category").enumValueIndex =
            (int)category;

        SerializedProperty prefabs =
            markerSerialized.FindProperty("networkPrefabs");
        prefabs.arraySize = prefabCandidates.Length;
        for (int index = 0; index < prefabCandidates.Length; index++)
        {
            prefabs.GetArrayElementAtIndex(index).objectReferenceValue =
                prefabCandidates[index];
        }

        markerSerialized.FindProperty("alwaysSpawn").boolValue = alwaysSpawn;
        markerSerialized.FindProperty("spawnChance").floatValue = chance;
        markerSerialized.FindProperty("minimumStage").intValue = 2;
        markerSerialized.FindProperty("maximumStage").intValue = 0;
        markerSerialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CreateArrivalAndExit(Transform gameplayRoot)
    {
        GameObject arrival = new GameObject("Arrival_Rowboat");
        arrival.transform.SetParent(gameplayRoot, false);
        arrival.transform.position = new Vector3(0f, -18f, 0f);
        AddRowboatVisual(arrival, 12);

        GameObject exit = new GameObject("Island_Exit_Rowboat");
        exit.transform.SetParent(gameplayRoot, false);
        exit.transform.position = new Vector3(29f, 2f, 0f);

        exit.AddComponent<NetworkObject>();
        BoxCollider2D exitCollider = exit.AddComponent<BoxCollider2D>();
        exitCollider.isTrigger = true;
        exitCollider.size = new Vector2(3f, 2f);

        NetworkStagePortal portal = exit.AddComponent<NetworkStagePortal>();
        SetSerializedString(
            portal,
            "destinationSceneName",
            "Boat_Gameplay_2D"
        );
        SetSerializedBool(portal, "requireGenerationComplete", true);
        SetSerializedBool(portal, "requireAllEnemiesDefeated", true);
        SetSerializedBool(portal, "advanceStage", true);
        SetSerializedBool(portal, "allowRepeatedInteraction", false);
        AddRowboatVisual(exit, 18);
    }

    private static void CreateCameraAndLighting()
    {
        GameObject boundsObject = new GameObject("CameraBounds");
        BoxCollider2D bounds = boundsObject.AddComponent<BoxCollider2D>();
        bounds.isTrigger = true;
        bounds.size = new Vector2(76f, 58f);
        boundsObject.transform.position = new Vector3(0f, 0.5f, 0f);

        GameObject cameraObject = new GameObject("Alpha_Main_Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 8f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color32(65, 91, 169, 255);
        cameraObject.AddComponent<UniversalAdditionalCameraData>();
        cameraObject.AddComponent<AudioListener>();

        Camera2DFollow follow = cameraObject.AddComponent<Camera2DFollow>();
        SetSerializedBool(follow, "followLocalPlayer", true);
        SetSerializedVector2(follow, "islandCenter", new Vector2(0f, -9f));
        SetSerializedBool(follow, "clampToBounds", true);
        SetSerializedObject(follow, "movementBounds", bounds);
        SetSerializedFloat(follow, "orthographicSize", 8f);
        SetSerializedFloat(follow, "pixelsPerUnit", 32f);

        cameraObject.transform.position = new Vector3(0f, -9f, -10f);

        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.96f, 0.84f);
        light.intensity = 1f;
        lightObject.AddComponent<UniversalAdditionalLightData>();
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    private static void AddIslandPortalToBoatScene()
    {
        Scene scene = EditorSceneManager.OpenScene(
            BoatScenePath,
            OpenSceneMode.Single
        );

        GameObject existing = scene
            .GetRootGameObjects()
            .FirstOrDefault(root => root.name == "PostOceanIslandPortal");

        if (existing != null)
        {
            UnityEngine.Object.DestroyImmediate(existing);
        }

        PlayerSpawnPoint2D[] spawns = scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<PlayerSpawnPoint2D>(true))
            .ToArray();

        Vector2 center = spawns.Length == 0
            ? Vector2.zero
            : new Vector2(
                spawns.Average(spawn => spawn.transform.position.x),
                spawns.Average(spawn => spawn.transform.position.y)
            );

        float maximumX = spawns.Length == 0
            ? 0f
            : spawns.Max(spawn => spawn.transform.position.x);

        GameObject portalObject = new GameObject("PostOceanIslandPortal");
        portalObject.transform.position = new Vector3(
            maximumX + 4f,
            center.y,
            0f
        );

        portalObject.AddComponent<NetworkObject>();
        BoxCollider2D collider = portalObject.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;
        collider.size = new Vector2(3f, 2f);

        NetworkStagePortal portal =
            portalObject.AddComponent<NetworkStagePortal>();
        SetSerializedString(
            portal,
            "destinationSceneName",
            "Island_After_Ocean_01_2D"
        );
        SetSerializedBool(portal, "requireGenerationComplete", false);
        SetSerializedBool(portal, "requireAllEnemiesDefeated", false);
        SetSerializedBool(portal, "advanceStage", true);
        SetSerializedBool(portal, "allowRepeatedInteraction", false);
        AddRowboatVisual(portalObject, 20);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, BoatScenePath);
    }

    private static void EnsureBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes =
            EditorBuildSettings.scenes.ToList();

        if (!scenes.Any(scene => scene.path == IslandScenePath))
        {
            scenes.Add(new EditorBuildSettingsScene(IslandScenePath, true));
        }

        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static HashSet<Vector3Int> BuildLandCells(SourceIslandArt art)
    {
        HashSet<Vector3Int> land = new HashSet<Vector3Int>();

        // Five overlapping hand-placed beach masses create readable coves and
        // lobes instead of a single procedural ellipse. Small cuts on the west
        // and northeast break the silhouette around landmark zones.
        for (int x = -28; x <= 28; x++)
        {
            for (int y = -22; y <= 22; y++)
            {
                bool island =
                    InsideEllipse(x, y, 0f, 0f, 19f, 13f) ||
                    InsideEllipse(x, y, -15f, 1f, 9f, 9f) ||
                    InsideEllipse(x, y, 15f, 1f, 9f, 9f) ||
                    InsideEllipse(x, y, -2f, 10f, 13f, 8f) ||
                    InsideEllipse(x, y, 0f, -11f, 12f, 7f);

                bool cove =
                    InsideEllipse(x, y, -22f, -9f, 4f, 4f) ||
                    InsideEllipse(x, y, 23f, 10f, 4f, 3f);

                if (island && !cove)
                {
                    land.Add(new Vector3Int(x, y, 0));
                }
            }
        }

        // This single contour notch avoids a diagonal-only water mask that is
        // absent from the hand-authored lobby shoreline. The neighboring A1
        // transition can therefore be copied exactly instead of guessed.
        land.Remove(new Vector3Int(10, 12, 0));

        HashSet<int> supportedGroundMasks = art.GroundShoreSamples
            .Select(sample => sample.ImmediateLandMask)
            .ToHashSet();

        // Remove only unsupported leaf tips. Removing their thicker neighbor at
        // the same time would recursively cut channels through the island.
        for (int pass = 0; pass < 4; pass++)
        {
            Vector3Int[] unsupported = land
                .Where(cell =>
                {
                    int mask = GetNeighborMask(land, cell);
                    return
                        mask != 255 &&
                        !supportedGroundMasks.Contains(mask) &&
                        CountBits(mask) <= 3;
                })
                .ToArray();

            if (unsupported.Length == 0)
            {
                break;
            }

            foreach (Vector3Int cell in unsupported)
            {
                land.Remove(cell);
            }
        }

        return land;
    }

    private static bool InsideEllipse(
        float x,
        float y,
        float centerX,
        float centerY,
        float radiusX,
        float radiusY
    )
    {
        float normalizedX = (x - centerX) / radiusX;
        float normalizedY = (y - centerY) / radiusY;
        return normalizedX * normalizedX + normalizedY * normalizedY <= 1f;
    }

    /// <summary>
    /// The lobby authors specific tiles wherever the painted water
    /// neighborhood is incomplete (outer map border) and wherever water
    /// touches land. After all water painting completes, replace any island
    /// water tile that violates either lobby convention with a lobby-authored
    /// tile satisfying both the water-adjacency and land-adjacency masks.
    /// Novel masks the small lobby never produced only require the tile to
    /// exist somewhere on the lobby water layer.
    /// </summary>
    private static void RepaintWaterEdgesToMatchLobby(
        Tilemap water,
        SourceIslandArt sourceArt,
        HashSet<Vector3Int> landCells
    )
    {
        HashSet<Vector3Int> painted = new HashSet<Vector3Int>();
        foreach (Vector3Int cell in water.cellBounds.allPositionsWithin)
        {
            if (water.HasTile(cell))
            {
                painted.Add(cell);
            }
        }

        // Novel masks fall back to the lobby's edge vocabulary — the same
        // set the architecture test accepts for shapes the lobby never
        // authored.
        HashSet<TileBase> edgeVocabulary = sourceArt
            .WaterTilesByWaterMask
            .Values
            .SelectMany(tiles => tiles)
            .ToHashSet();

        bool SatisfiesWaterMask(TileBase tile, int waterMask)
        {
            if (waterMask == 255)
            {
                return true;
            }

            return sourceArt.WaterTilesByWaterMask.TryGetValue(
                waterMask,
                out HashSet<TileBase> allowed
            )
                ? allowed.Contains(tile)
                : edgeVocabulary.Contains(tile);
        }

        HashSet<TileBase> coastalVocabulary = sourceArt
            .CoastalWaterTilesByLandMask
            .Values
            .SelectMany(tiles => tiles)
            .ToHashSet();

        bool SatisfiesCoastalMask(TileBase tile, int coastalMask)
        {
            if (coastalMask == 0)
            {
                return true;
            }

            return sourceArt.CoastalWaterTilesByLandMask.TryGetValue(
                coastalMask,
                out HashSet<TileBase> allowed
            )
                ? allowed.Contains(tile)
                : coastalVocabulary.Contains(tile);
        }

        int repaintedCells = 0;
        int unresolvedCells = 0;

        foreach (Vector3Int cell in painted)
        {
            int waterMask = GetNeighborMask(painted, cell);
            int coastalMask = landCells.Contains(cell)
                ? 0
                : GetNeighborMask(landCells, cell);

            TileBase currentTile = water.GetTile(cell);
            if (
                SatisfiesWaterMask(currentTile, waterMask) &&
                SatisfiesCoastalMask(currentTile, coastalMask)
            )
            {
                continue;
            }

            ShoreReferenceSample[] candidates = sourceArt
                .AllWaterLayerSamples
                .Where(sample =>
                    SatisfiesWaterMask(sample.Tile, waterMask) &&
                    SatisfiesCoastalMask(sample.Tile, coastalMask))
                .OrderBy(sample => sample.SourcePosition.y)
                .ThenBy(sample => sample.SourcePosition.x)
                .ToArray();

            if (candidates.Length == 0)
            {
                unresolvedCells++;
                continue;
            }

            ShoreReferenceSample chosen =
                candidates[GetCellHash(cell, 0x3E77) % candidates.Length];

            water.SetTile(cell, chosen.Tile);
            water.SetTransformMatrix(cell, chosen.Transform);
            repaintedCells++;
        }

        if (unresolvedCells > 0)
        {
            Debug.LogWarning(
                $"[Island Builder] {unresolvedCells} water cell(s) have no " +
                "lobby tile satisfying both edge constraints."
            );
        }

        Debug.Log(
            $"[Island Builder] Water border pass repainted {repaintedCells} " +
            "cell(s) to match the lobby's edge conventions."
        );
    }

    private static void PaintGroundDetails(
        Tilemap groundDetail,
        HashSet<Vector3Int> landCells
    )
    {
        Tile[] detailTiles = new[]
            {
                15, 16, 17, 18, 19, 20, 21, 22,
                31, 32, 34, 35, 66, 67, 82, 83, 103, 104, 105,
            }
            .Select(LoadPropTile)
            .Where(tile => tile != null)
            .ToArray();

        if (detailTiles.Length == 0)
        {
            return;
        }

        foreach (Vector3Int cell in landCells)
        {
            if (IsReservedGameplayCell(cell))
            {
                continue;
            }

            int hash = GetCellHash(cell, 0x2D4F);

            if (hash % 15 != 0)
            {
                continue;
            }

            groundDetail.SetTile(
                cell,
                detailTiles[hash % detailTiles.Length]
            );
            groundDetail.SetTransformMatrix(
                cell,
                GetInteriorSandTransform(cell + new Vector3Int(37, -19, 0))
            );
        }
    }

    private static void PaintPropStamps(
        Dictionary<string, PropStamp> stamps,
        HashSet<Vector3Int> landCells,
        Tilemap props,
        Tilemap overhead,
        Tilemap waterCollision,
        Tilemap obstacleCollision,
        TileBase obstacleCollisionGridTile
    )
    {
        HashSet<Vector3Int> occupied = new HashSet<Vector3Int>();

        Vector2Int[] smallPalmAnchors =
        {
            new Vector2Int(-21, -11),
            new Vector2Int(-20, -1),
            new Vector2Int(-19, 7),
            new Vector2Int(-13, 11),
            new Vector2Int(-7, 13),
            new Vector2Int(7, 13),
            new Vector2Int(15, 9),
            new Vector2Int(19, 1),
            new Vector2Int(17, -10),
            new Vector2Int(10, -13),
            new Vector2Int(-15, -13),
            new Vector2Int(-8, -6),
            new Vector2Int(-8, 3),
            new Vector2Int(7, -5),
            new Vector2Int(7, 4),
            new Vector2Int(-3, 8),
            new Vector2Int(-16, -7),
            new Vector2Int(-15, -1),
            new Vector2Int(-15, 4),
            new Vector2Int(-10, -4),
            new Vector2Int(-7, 1),
            new Vector2Int(-7, 7),
            new Vector2Int(5, 6),
            new Vector2Int(8, -2),
            new Vector2Int(12, -1),
            new Vector2Int(12, -8),
            new Vector2Int(6, -11),
        };

        Vector2Int[] largePalmAnchors =
        {
            new Vector2Int(-16, 8),
            new Vector2Int(14, 7),
        };

        Vector2Int[] westGrassAnchors =
        {
            new Vector2Int(-18, -3),
            new Vector2Int(-17, 5),
            new Vector2Int(-8, 11),
            new Vector2Int(5, 11),
            new Vector2Int(14, 5),
            new Vector2Int(14, -10),
            new Vector2Int(-13, -7),
            new Vector2Int(-13, 1),
            new Vector2Int(8, 5),
            new Vector2Int(11, -1),
        };

        foreach (Vector2Int anchor in smallPalmAnchors)
        {
            PlaceNamedStamp(
                stamps,
                "SmallPalm",
                anchor,
                landCells,
                occupied,
                props,
                overhead,
                obstacleCollision,
                obstacleCollisionGridTile
            );
        }

        foreach (Vector2Int anchor in largePalmAnchors)
        {
            PlaceNamedStamp(
                stamps,
                "LargePalm",
                anchor,
                landCells,
                occupied,
                props,
                overhead,
                obstacleCollision,
                obstacleCollisionGridTile
            );
        }

        PlaceNamedStamp(
            stamps,
            "CrateCamp",
            new Vector2Int(15, -3),
            landCells,
            occupied,
            props,
            overhead,
            obstacleCollision,
            obstacleCollisionGridTile
        );

        for (int index = 0; index < westGrassAnchors.Length; index++)
        {
            PlaceNamedStamp(
                stamps,
                index % 2 == 0 ? "GrassWest" : "GrassEast",
                westGrassAnchors[index],
                landCells,
                occupied,
                props,
                overhead,
                obstacleCollision,
                obstacleCollisionGridTile
            );
        }

        Vector2Int[] detailAnchors =
        {
            new Vector2Int(-19, -12), new Vector2Int(-14, -9),
            new Vector2Int(-20, 4), new Vector2Int(-14, 6),
            new Vector2Int(-9, -12), new Vector2Int(-7, -6),
            new Vector2Int(-6, 6), new Vector2Int(-10, 14),
            new Vector2Int(6, -11), new Vector2Int(7, -5),
            new Vector2Int(7, 6), new Vector2Int(10, 14),
            new Vector2Int(14, -8), new Vector2Int(18, -4),
            new Vector2Int(18, 7), new Vector2Int(12, 10),
            new Vector2Int(-2, 6), new Vector2Int(4, 5),
            new Vector2Int(-16, -6), new Vector2Int(-13, -3),
            new Vector2Int(-15, 2), new Vector2Int(-11, 5),
            new Vector2Int(-7, -9), new Vector2Int(-7, -1),
            new Vector2Int(-7, 8), new Vector2Int(-3, 11),
            new Vector2Int(5, -8), new Vector2Int(8, -3),
            new Vector2Int(7, 3), new Vector2Int(5, 8),
            new Vector2Int(12, -7), new Vector2Int(14, -2),
            new Vector2Int(13, 3), new Vector2Int(10, 7),
        };

        for (int index = 0; index < detailAnchors.Length; index++)
        {
            PlaceNamedStamp(
                stamps,
                $"Detail_{index % 6:D2}",
                detailAnchors[index],
                landCells,
                occupied,
                props,
                overhead,
                obstacleCollision,
                obstacleCollisionGridTile
            );
        }

        PaintCoastalPier(
            stamps["Pier"],
            new Vector3Int(20, 1, 0),
            props,
            overhead,
            waterCollision
        );
    }

    private static void PlaceNamedStamp(
        IReadOnlyDictionary<string, PropStamp> stamps,
        string stampName,
        Vector2Int anchor,
        HashSet<Vector3Int> landCells,
        HashSet<Vector3Int> occupied,
        Tilemap props,
        Tilemap overhead,
        Tilemap obstacleCollision,
        TileBase obstacleCollisionGridTile
    )
    {
        if (!stamps.TryGetValue(stampName, out PropStamp stamp))
        {
            throw new InvalidOperationException(
                $"Missing named lobby art stamp '{stampName}'."
            );
        }

        const int searchRadius = 5;

        for (int radius = 0; radius <= searchRadius; radius++)
        {
            for (int offsetY = -radius; offsetY <= radius; offsetY++)
            {
                for (int offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    if (Mathf.Max(Mathf.Abs(offsetX), Mathf.Abs(offsetY)) != radius)
                    {
                        continue;
                    }

                    if (TryPaintStamp(
                        stamp,
                        new Vector3Int(
                            anchor.x + offsetX,
                            anchor.y + offsetY,
                            0
                        ),
                        landCells,
                        occupied,
                        props,
                        overhead,
                        obstacleCollision,
                        obstacleCollisionGridTile
                    ))
                    {
                        return;
                    }
                }
            }
        }

        Debug.LogWarning(
            $"[Island Builder] Could not place art stamp {stampName} near " +
            $"{anchor}."
        );
    }

    private static void PaintCoastalPier(
        PropStamp pier,
        Vector3Int anchor,
        Tilemap props,
        Tilemap overhead,
        Tilemap waterCollision
    )
    {
        foreach (StampCell cell in pier.Cells)
        {
            Vector3Int position = anchor + cell.RelativePosition;

            if (cell.PropTile != null)
            {
                props.SetTile(position, cell.PropTile);
            }

            if (cell.OverheadTile != null)
            {
                overhead.SetTile(position, cell.OverheadTile);
            }

            // The pier is the only intentional walkable path over water.
            waterCollision.SetTile(position, null);
        }
    }

    private static bool TryPaintStamp(
        PropStamp stamp,
        Vector3Int anchor,
        HashSet<Vector3Int> landCells,
        HashSet<Vector3Int> occupied,
        Tilemap props,
        Tilemap overhead,
        Tilemap obstacleCollision,
        TileBase obstacleCollisionGridTile
    )
    {
        bool fits = stamp.Cells.All(cell =>
        {
            Vector3Int position = anchor + cell.RelativePosition;
            bool needsDryGround =
                cell.PropTile != null || cell.CollisionTile != null;

            return
                (!needsDryGround || landCells.Contains(position)) &&
                !occupied.Contains(position) &&
                (cell.CollisionTile == null ||
                    !IsReservedGameplayCell(position));
        });

        if (!fits)
        {
            return false;
        }

        foreach (StampCell cell in stamp.Cells)
        {
            Vector3Int position = anchor + cell.RelativePosition;
            occupied.Add(position);

            if (cell.PropTile != null)
            {
                props.SetTile(position, cell.PropTile);
            }

            if (cell.OverheadTile != null)
            {
                overhead.SetTile(position, cell.OverheadTile);
            }

            if (cell.CollisionTile != null)
            {
                obstacleCollision.SetTile(
                    position,
                    obstacleCollisionGridTile
                );
            }
        }

        return true;
    }

    private static bool IsReservedGameplayCell(Vector3Int cell)
    {
        Vector2 position = new Vector2(cell.x, cell.y);

        if (Vector2.Distance(position, new Vector2(0f, -9f)) <= 5f)
        {
            return true;
        }

        if (Vector2.Distance(position, new Vector2(0f, -1f)) <= 5.25f)
        {
            return true;
        }

        float pathCenter = Mathf.Sin((cell.y + 10f) * 0.24f) * 1.7f;
        if (
            cell.y >= -11 &&
            cell.y <= 14 &&
            Mathf.Abs(cell.x - pathCenter) <= 2.25f
        )
        {
            return true;
        }

        if (Vector2.Distance(position, new Vector2(0f, 14f)) <= 3.5f)
        {
            return true;
        }

        if (
            cell.x >= 0 &&
            cell.x <= 21 &&
            Mathf.Abs(cell.y - (2f + Mathf.Sin(cell.x * 0.24f))) <= 1.75f
        )
        {
            return true;
        }

        foreach (Vector2Int marker in EnemyMarkerPositions)
        {
            if (Vector2.Distance(position, marker) <= 1.8f)
            {
                return true;
            }
        }

        foreach (Vector2Int marker in LootMarkerPositions)
        {
            if (Vector2.Distance(position, marker) <= 1.25f)
            {
                return true;
            }
        }

        return false;
    }

    private static Tilemap CreateTilemap(
        GameObject gridObject,
        string name,
        int sortingOrder
    )
    {
        GameObject tilemapObject = new GameObject(name);
        tilemapObject.transform.SetParent(gridObject.transform, false);
        Tilemap tilemap = tilemapObject.AddComponent<Tilemap>();
        TilemapRenderer renderer =
            tilemapObject.AddComponent<TilemapRenderer>();
        renderer.sortingOrder = sortingOrder;
        return tilemap;
    }

    private static Tilemap CreateCollisionTilemap(
        GameObject gridObject,
        string name
    )
    {
        Tilemap tilemap = CreateTilemap(gridObject, name, 50);
        tilemap.GetComponent<TilemapRenderer>().enabled = false;

        Rigidbody2D body = tilemap.gameObject.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Static;

        CompositeCollider2D composite =
            tilemap.gameObject.AddComponent<CompositeCollider2D>();
        composite.geometryType = CompositeCollider2D.GeometryType.Outlines;
        composite.generationType =
            CompositeCollider2D.GenerationType.Synchronous;

        TilemapCollider2D tilemapCollider =
            tilemap.gameObject.AddComponent<TilemapCollider2D>();
        tilemapCollider.compositeOperation =
            Collider2D.CompositeOperation.Merge;
        return tilemap;
    }

    private static Tilemap FindTilemap(Scene scene, string objectName)
    {
        Tilemap tilemap = scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Tilemap>(true))
            .FirstOrDefault(candidate => candidate.name == objectName);

        if (tilemap == null)
        {
            throw new InvalidOperationException(
                $"Source island tilemap was not found: {objectName}"
            );
        }

        return tilemap;
    }

    private static Dictionary<Vector3Int, TileBase> CaptureCells(
        Tilemap tilemap
    )
    {
        Dictionary<Vector3Int, TileBase> cells =
            new Dictionary<Vector3Int, TileBase>();

        foreach (Vector3Int position in tilemap.cellBounds.allPositionsWithin)
        {
            TileBase tile = tilemap.GetTile(position);
            if (tile != null)
            {
                cells[position] = tile;
            }
        }

        return cells;
    }

    private static TileBase MostCommonTile(IEnumerable<TileBase> tiles)
    {
        return tiles
            .Where(tile => tile != null)
            .GroupBy(tile => tile)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault();
    }

    private static int GetNeighborMask(
        HashSet<Vector3Int> occupied,
        Vector3Int cell
    )
    {
        int mask = 0;

        for (int index = 0; index < NeighborOffsets.Length; index++)
        {
            if (occupied.Contains(cell + NeighborOffsets[index]))
            {
                mask |= 1 << index;
            }
        }

        return mask;
    }

    private static ulong GetLandSignature(
        HashSet<Vector3Int> landCells,
        Vector3Int center
    )
    {
        ulong signature = 0UL;
        int bit = 0;

        for (int y = -ShoreReferenceRadius; y <= ShoreReferenceRadius; y++)
        {
            for (
                int x = -ShoreReferenceRadius;
                x <= ShoreReferenceRadius;
                x++
            )
            {
                if (landCells.Contains(center + new Vector3Int(x, y, 0)))
                {
                    signature |= 1UL << bit;
                }

                bit++;
            }
        }

        return signature;
    }

    private static int CountBits(int value)
    {
        int count = 0;
        while (value != 0)
        {
            count += value & 1;
            value >>= 1;
        }

        return count;
    }

    private static int CountSignatureDifferences(
        ulong first,
        ulong second,
        int minimumRadius,
        int maximumRadius
    )
    {
        ulong differences = first ^ second;
        int count = 0;
        int bit = 0;

        for (int y = -ShoreReferenceRadius; y <= ShoreReferenceRadius; y++)
        {
            for (
                int x = -ShoreReferenceRadius;
                x <= ShoreReferenceRadius;
                x++
            )
            {
                int radius = Mathf.Max(Mathf.Abs(x), Mathf.Abs(y));
                if (
                    radius >= minimumRadius &&
                    radius <= maximumRadius &&
                    (differences & (1UL << bit)) != 0UL
                )
                {
                    count++;
                }

                bit++;
            }
        }

        return count;
    }

    private static void ValidateTerrainMasks(
        HashSet<Vector3Int> landCells,
        SourceIslandArt art
    )
    {
        HashSet<int> supportedGroundMasks = art.GroundShoreSamples
            .Select(sample => sample.ImmediateLandMask)
            .ToHashSet();
        string[] unsupported = landCells
            .Select(cell => new
            {
                Cell = cell,
                Mask = GetNeighborMask(landCells, cell),
            })
            .Where(item =>
                item.Mask != 255 &&
                !supportedGroundMasks.Contains(item.Mask))
            .Select(item => $"{item.Cell}:{item.Mask}")
            .ToArray();

        if (unsupported.Length > 0)
        {
            throw new InvalidOperationException(
                "The island outline contains unsupported shore masks: " +
                string.Join(", ", unsupported)
            );
        }


        HashSet<int> supportedWaterMasks = art.WaterShoreSamples
            .Select(sample => sample.ImmediateLandMask)
            .ToHashSet();
        List<string> unsupportedWater = new List<string>();

        for (int x = WaterMinX; x <= WaterMaxX; x++)
        {
            for (int y = WaterMinY; y <= WaterMaxY; y++)
            {
                Vector3Int cell = new Vector3Int(x, y, 0);
                if (landCells.Contains(cell))
                {
                    continue;
                }

                int mask = GetNeighborMask(landCells, cell);
                if (mask != 0 && !supportedWaterMasks.Contains(mask))
                {
                    unsupportedWater.Add($"{cell}:{mask}");
                }
            }
        }

        if (unsupportedWater.Count > 0)
        {
            throw new InvalidOperationException(
                "The island outline contains water-edge masks that do not " +
                "exist in the lobby reference: " +
                string.Join(", ", unsupportedWater)
            );
        }
    }

    private static TileBase SelectInteriorGroundTile(
        SourceIslandArt art,
        Vector3Int cell
    )
    {
        if (art.InteriorGroundTiles.Count == 0)
        {
            return art.InteriorGroundTile;
        }

        int hash = GetCellHash(cell, 0x51A7);
        return art.InteriorGroundTiles[hash % art.InteriorGroundTiles.Count];
    }

    private static TileBase SelectOpenWaterTile(
        SourceIslandArt art,
        Vector3Int cell
    )
    {
        if (art.OpenWaterTiles.Count == 0)
        {
            return art.WaterTile;
        }

        int hash = GetCellHash(cell, 0x6A31);
        return art.OpenWaterTiles[hash % art.OpenWaterTiles.Count];
    }

    private static ShoreReferenceSample SelectShoreReference(
        IReadOnlyList<ShoreReferenceSample> samples,
        HashSet<Vector3Int> landCells,
        Vector3Int cell,
        int immediateMask,
        string layerName,
        ISet<TileBase> deprioritizedTiles = null
    )
    {
        ulong signature = GetLandSignature(landCells, cell);
        ShoreReferenceSample[] matching = samples
            .Where(sample => sample.ImmediateLandMask == immediateMask)
            .ToArray();

        if (matching.Length == 0)
        {
            throw new InvalidOperationException(
                $"No lobby {layerName} shoreline reference exists for " +
                $"neighbor mask {immediateMask} at {cell}."
            );
        }

        if (deprioritizedTiles != null)
        {
            ShoreReferenceSample[] preferred = matching
                .Where(sample =>
                    !deprioritizedTiles.Contains(sample.Tile))
                .ToArray();

            if (preferred.Length > 0)
            {
                matching = preferred;
            }
        }

        // Immediate topology is an exact match. Radius two chooses the closest
        // authored curve; the outer radius-three ring resolves the few cases
        // where the lobby intentionally uses a different turn for the same
        // local 5x5 shape. Source position makes ties deterministic.
        return matching
            .OrderBy(sample => CountSignatureDifferences(
                signature,
                sample.LandSignature,
                0,
                2
            ))
            .ThenBy(sample => CountSignatureDifferences(
                signature,
                sample.LandSignature,
                3,
                3
            ))
            .ThenBy(sample => sample.SourcePosition.y)
            .ThenBy(sample => sample.SourcePosition.x)
            .First();
    }

    private static Matrix4x4 GetInteriorSandTransform(Vector3Int cell)
    {
        int hash = GetCellHash(cell, 0x7B31);
        int sample = hash % 20;
        float rotation = sample < 7 ? -90f : sample < 10 ? 180f : 0f;

        return Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, rotation));
    }

    private static int GetCellHash(Vector3Int cell, int salt)
    {
        unchecked
        {
            uint hash = (uint)salt;
            hash ^= (uint)cell.x * 0x8DA6B343u;
            hash ^= (uint)cell.y * 0xD8163841u;
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            hash ^= hash >> 16;
            return (int)(hash & int.MaxValue);
        }
    }

    private static void BuildNamedPropStamps(
        Dictionary<Vector3Int, TileBase> propCells,
        Dictionary<Vector3Int, TileBase> overheadCells,
        Dictionary<Vector3Int, TileBase> collisionCells,
        Dictionary<string, PropStamp> destination
    )
    {
        AddNamedPropStamp(
            "Pier", 7, 0, 8, 3,
            propCells, overheadCells, collisionCells, destination
        );
        AddNamedPropStamp(
            "SmallPalm", -4, 8, 3, 4,
            propCells, overheadCells, collisionCells, destination
        );
        AddNamedPropStamp(
            "CrateCamp", -6, -2, 4, 5,
            propCells, overheadCells, collisionCells, destination
        );
        AddNamedPropStamp(
            "LargePalm", 0, 6, 6, 5,
            propCells, overheadCells, collisionCells, destination
        );
        AddNamedPropStamp(
            "HammockGrove", -1, -1, 5, 4,
            propCells, overheadCells, collisionCells, destination
        );
        AddNamedPropStamp(
            "GrassWest", -5, 3, 4, 4,
            propCells, overheadCells, collisionCells, destination
        );
        AddNamedPropStamp(
            "GrassEast", -1, 3, 3, 4,
            propCells, overheadCells, collisionCells, destination
        );

        Vector2Int[] detailSources =
        {
            new Vector2Int(-9, 0),
            new Vector2Int(-8, -2),
            new Vector2Int(-8, 4),
            new Vector2Int(-8, 5),
            new Vector2Int(5, -2),
            new Vector2Int(8, 4),
        };

        for (int index = 0; index < detailSources.Length; index++)
        {
            Vector2Int source = detailSources[index];
            AddNamedPropStamp(
                $"Detail_{index:D2}",
                source.x,
                source.y,
                1,
                1,
                propCells,
                overheadCells,
                collisionCells,
                destination
            );
        }
    }

    private static void AddNamedPropStamp(
        string name,
        int x,
        int y,
        int width,
        int height,
        IReadOnlyDictionary<Vector3Int, TileBase> propCells,
        IReadOnlyDictionary<Vector3Int, TileBase> overheadCells,
        IReadOnlyDictionary<Vector3Int, TileBase> collisionCells,
        IDictionary<string, PropStamp> destination
    )
    {
        Vector3Int origin = new Vector3Int(x, y, 0);
        PropStamp stamp = new PropStamp
        {
            Name = name,
            SourceOrigin = origin,
        };

        for (int offsetY = 0; offsetY < height; offsetY++)
        {
            for (int offsetX = 0; offsetX < width; offsetX++)
            {
                Vector3Int position = origin +
                    new Vector3Int(offsetX, offsetY, 0);
                propCells.TryGetValue(position, out TileBase prop);
                overheadCells.TryGetValue(position, out TileBase overhead);
                collisionCells.TryGetValue(position, out TileBase collision);

                if (prop == null && overhead == null && collision == null)
                {
                    continue;
                }

                stamp.Cells.Add(
                    new StampCell(
                        position - origin,
                        prop,
                        overhead,
                        collision
                    )
                );
            }
        }

        if (stamp.Cells.Count == 0)
        {
            throw new InvalidOperationException(
                $"Lobby art stamp '{name}' captured no cells at {origin}."
            );
        }

        destination.Add(name, stamp);
    }

    private static void AddTiles(
        ISet<TileBase> destination,
        IEnumerable<TileBase> tiles
    )
    {
        foreach (TileBase tile in tiles)
        {
            if (tile != null)
            {
                destination.Add(tile);
            }
        }
    }

    private static void ConfigureBudget(
        SerializedProperty budget,
        SeededContentCategory category,
        int minimum,
        int maximum
    )
    {
        budget.FindPropertyRelative("category").enumValueIndex =
            (int)category;
        budget.FindPropertyRelative("minimumCount").intValue = minimum;
        budget.FindPropertyRelative("maximumCount").intValue = maximum;
    }

    private static void AddRowboatVisual(GameObject target, int sortingOrder)
    {
        Sprite rowboatSprite = AssetDatabase
            .LoadAllAssetsAtPath(RowboatSpritePath)
            .OfType<Sprite>()
            .FirstOrDefault();

        if (rowboatSprite == null)
        {
            return;
        }

        SpriteRenderer renderer = target.GetComponent<SpriteRenderer>();
        if (renderer == null)
        {
            renderer = target.AddComponent<SpriteRenderer>();
        }
        renderer.sprite = rowboatSprite;
        renderer.sortingOrder = sortingOrder;
    }

    private static Tile LoadPropTile(int index)
    {
        return AssetDatabase.LoadAssetAtPath<Tile>(
            $"{PropTileFolder}/tf_beach_tileB_{index}.asset"
        );
    }

    private static Tile LoadTerrainTile(int index)
    {
        return AssetDatabase.LoadAssetAtPath<Tile>(
            $"{TerrainTileFolder}/tf_beach_tileA1_{index}.asset"
        );
    }

    private static void EnsureRegistered(
        NetworkPrefabsList list,
        GameObject prefab
    )
    {
        if (list == null || prefab == null || list.Contains(prefab))
        {
            return;
        }

        list.Add(new NetworkPrefab
        {
            Override = NetworkPrefabOverride.None,
            Prefab = prefab,
        });
        EditorUtility.SetDirty(list);
    }

    private static T EnsureComponent<T>(GameObject gameObject)
        where T : Component
    {
        T component = gameObject.GetComponent<T>();
        return component != null ? component : gameObject.AddComponent<T>();
    }

    private static GameObject LoadOrCreatePrefabContents(
        string path,
        string objectName
    )
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            return new GameObject(objectName);
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        LoadedPrefabContentInstanceIds.Add(contents.GetInstanceID());
        return contents;
    }

    private static void UnloadOrDestroyPrefabContents(
        GameObject root,
        string path
    )
    {
        if (LoadedPrefabContentInstanceIds.Remove(root.GetInstanceID()))
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        string parent = folderPath.Substring(
            0,
            folderPath.LastIndexOf('/')
        );
        string folderName = folderPath.Substring(
            folderPath.LastIndexOf('/') + 1
        );

        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folderName);
    }

    private static void SetSerializedBool(
        UnityEngine.Object target,
        string propertyName,
        bool value
    )
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null)
        {
            property.boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void SetSerializedFloat(
        UnityEngine.Object target,
        string propertyName,
        float value
    )
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null)
        {
            property.floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void SetSerializedString(
        UnityEngine.Object target,
        string propertyName,
        string value
    )
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null)
        {
            property.stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void SetSerializedVector2(
        UnityEngine.Object target,
        string propertyName,
        Vector2 value
    )
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null)
        {
            property.vector2Value = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void SetSerializedObject(
        UnityEngine.Object target,
        string propertyName,
        UnityEngine.Object value
    )
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null)
        {
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
