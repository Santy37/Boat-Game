using System;
using System.Collections.Generic;
using System.Linq;
using DeadmansTales.Networking;
using DeadmansTales.UI;
using DeadmansTales.WorldGeneration;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Turns Island_Shop_2D into the crew's safe harbour: a market plaza of
/// stalls run by NPC traders who sell blade tiers, ship upgrades, and hot
/// meals for the coins players plunder on the hostile islands.
///
/// The scene starts as a copy of the post-ocean island so the terrain,
/// shoreline autotiling, camera framing, and spawn rig are already correct
/// and consistent with the rest of the game. This builder then converts it:
/// every hostile spawn marker is removed (a shop you have to fight your way
/// through is not a shop), the wilderness dressing is replaced by a market
/// plaza, and coins are seeded so the stalls are usable on a first visit.
///
/// Idempotent: everything it creates lives under a single "ShopDistrict"
/// root plus "orpg_"-prefixed tiles, both of which are cleared first.
/// </summary>
public static class ShopIslandBuilder
{
    private const string MenuPath = "Deadman's Tales/Build Shop Island";

    private const string ShopScenePath =
        "Assets/DeadmansTales/Scenes/Island_Shop_2D.unity";

    private const string GameplayPrefabFolder =
        "Assets/DeadmansTales/Prefabs/Gameplay";

    private const string CoinPrefabPath =
        GameplayPrefabFolder + "/NetworkCoinPickup.prefab";

    private const string MotwSheetPath =
        "Assets/DeadmansTales/Art_Pixel/Characters/motw.png";

    private const string PropsSheetPath =
        "Assets/DeadmansTales/Art_Pixel/Tiles/OpenRPG/openrpg_exterior.png";

    private const string TileFolder =
        "Assets/DeadmansTales/Art_Pixel/Tiles/OpenRPGTiles";

    private const string ObstacleCollisionTilePath =
        "Assets/DeadmansTales/Palettes/Island_ObstacleCollision_Grid.asset";

    private const string GeneratedNetworkPrefabsPath =
        "Assets/DefaultNetworkPrefabs.asset";
    private const string BootstrapSettingsPath =
        "Assets/DeadmansTales/Resources/Networking/" +
        "DeadmansNetworkBootstrapSettings.asset";

    private const string DistrictRootName = "ShopDistrict";

    /// <summary>
    /// motw.png is a 12x8 grid of 52x72 cells in RPG-Maker layout: each
    /// character occupies a 3-wide by 4-tall block (3 walk frames, 4
    /// facings). Column 1 of a block, top row, is the forward-facing idle —
    /// the pose a stallholder should hold.
    /// </summary>
    private const int MotwColumns = 12;

    /// <summary>
    /// The sheet is shared with the player and basicenemy prefabs, so its
    /// import settings are untouchable — changing pixels-per-unit here
    /// would resize the player. The traders are scaled on their transform
    /// instead: 72px cells at 32 ppu draw a ~1.9 unit character, and the
    /// player is ~1.0, so this brings them to a natural ~1.15.
    /// </summary>
    private const float NpcScale = 0.61f;

    private sealed class VendorSpec
    {
        public VendorSpec(
            string objectName,
            string vendorName,
            ShopStock stock,
            int motwIndex,
            int basePrice,
            int priceStep,
            int limit,
            Vector2 position
        )
        {
            ObjectName = objectName;
            VendorName = vendorName;
            Stock = stock;
            MotwIndex = motwIndex;
            BasePrice = basePrice;
            PriceStep = priceStep;
            Limit = limit;
            Position = position;
        }

        public string ObjectName { get; }
        public string VendorName { get; }
        public ShopStock Stock { get; }
        public int MotwIndex { get; }
        public int BasePrice { get; }
        public int PriceStep { get; }
        public int Limit { get; }
        public Vector2 Position { get; }
    }

    // Laid out along the plaza's north side so players walking up the trail
    // from the beach meet the stalls face-on. motw indices are the
    // forward-facing idle of each character block.
    private static readonly VendorSpec[] Vendors =
    {
        new VendorSpec(
            "Vendor_Blacksmith",
            "Rusty the Smith",
            ShopStock.WeaponTier,
            7,
            25,
            15,
            0,
            new Vector2(-12.5f, 6.2f)
        ),
        new VendorSpec(
            "Vendor_Quartermaster",
            "Quartermaster Vex",
            ShopStock.Upgrade,
            10,
            30,
            20,
            0,
            new Vector2(-8.5f, 6.2f)
        ),
        new VendorSpec(
            "Vendor_Cook",
            "Ol' Sally",
            ShopStock.FullHeal,
            55,
            15,
            5,
            0,
            new Vector2(-4.5f, 6.2f)
        ),
    };

    // Idle townsfolk: no stall, purely to make the port feel inhabited.
    private static readonly (string Name, int Motw, Vector2 Position)[]
        Townsfolk =
        {
            ("Townsfolk_Dockhand", 1, new Vector2(-14.5f, 2.5f)),
            ("Townsfolk_Deckhand", 49, new Vector2(-2.5f, 2.2f)),
            ("Townsfolk_Traveller", 52, new Vector2(-6.5f, 0.5f)),
        };

    // Coins the player can collect on a first visit so the stalls are not
    // dead content before they have plundered anywhere.
    private static readonly Vector2[] CoinPositions =
    {
        new Vector2(-16.5f, 4.5f),
        new Vector2(-15.5f, 0.5f),
        new Vector2(-10.5f, 3.5f),
        new Vector2(-6.5f, 4.5f),
        new Vector2(-3.5f, 3.5f),
        new Vector2(-1.5f, 6.5f),
        new Vector2(1.5f, 2.5f),
        new Vector2(4.5f, 4.5f),
    };

    [MenuItem(MenuPath)]
    public static void BuildAll()
    {
        GameObject coinPrefab = CreateCoinPrefab();
        RegisterNetworkPrefabs(new[] { coinPrefab });
        SeedCoinsIntoHostileIsland(coinPrefab);

        Scene scene = EditorSceneManager.OpenScene(
            ShopScenePath,
            OpenSceneMode.Single
        );

        StripHostileContent(scene);
        ClearPreviousDistrict(scene);

        Transform districtRoot = new GameObject(DistrictRootName).transform;

        PaintPlaza(scene);
        BuildStalls(districtRoot);
        BuildTownsfolk(districtRoot);
        PlaceCoins(districtRoot, coinPrefab);
        EnsureCoinHud(scene);
        PointExitAtBoat(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        EnsureBuildSettings();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // The in-scene NetworkObjects created above still need unique
        // GlobalObjectIdHash values, but the identity repair deliberately
        // refuses to run in the same pass as scene edits so it can never
        // overwrite unsaved work. It is run as a separate step:
        //   -executeMethod NetworkSceneIdentityRepair.RepairFromCommandLine

        Debug.Log(
            $"[Shop Island] Built {Vendors.Length} stalls, " +
            $"{Townsfolk.Length} townsfolk, and {CoinPositions.Length} " +
            "coin pickups; hostile spawns removed."
        );
    }

    public static void BuildAllFromCommandLine()
    {
        BuildAll();
    }

    private const string HostileIslandScenePath =
        "Assets/DeadmansTales/Scenes/Island_After_Ocean_01_2D.unity";

    /// <summary>
    /// Gives the shop an economy. The coins scattered around the market are
    /// a starter float; the real income has to come from the dangerous
    /// island, or the stalls are a one-time vending machine.
    ///
    /// Coins join the post-ocean island's existing Loot marker pool rather
    /// than being hand-placed, so how much plunder a run yields is decided
    /// by the run seed like every other piece of island content — the same
    /// seed always funds the same shopping trip.
    /// </summary>
    private static void SeedCoinsIntoHostileIsland(GameObject coinPrefab)
    {
        if (coinPrefab == null)
        {
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(
            HostileIslandScenePath,
            OpenSceneMode.Single
        );

        int updated = 0;

        foreach (SeededSpawnMarker2D marker in scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<SeededSpawnMarker2D>(true))
            .Where(marker =>
                marker.Category == SeededContentCategory.Loot))
        {
            SerializedObject serialized = new SerializedObject(marker);
            SerializedProperty prefabs =
                serialized.FindProperty("networkPrefabs");

            List<UnityEngine.Object> current = Enumerable
                .Range(0, prefabs.arraySize)
                .Select(index => prefabs
                    .GetArrayElementAtIndex(index).objectReferenceValue)
                .Where(value => value != null)
                .ToList();

            if (current.Contains(coinPrefab))
            {
                continue;
            }

            current.Add(coinPrefab);

            prefabs.arraySize = current.Count;
            for (int index = 0; index < current.Count; index++)
            {
                prefabs.GetArrayElementAtIndex(index).objectReferenceValue =
                    current[index];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            updated++;
        }

        if (updated > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        Debug.Log(
            $"[Shop Island] Coins added to {updated} loot markers on the " +
            "post-ocean island."
        );
    }

    // ------------------------------------------------------------------
    // Converting the hostile island into a safe harbour
    // ------------------------------------------------------------------

    /// <summary>
    /// Removes every seeded spawn marker and any pre-placed enemy. The shop
    /// island is the one place in the run where players can stand still, so
    /// nothing here may spawn something that attacks them.
    /// </summary>
    private static void StripHostileContent(Scene scene)
    {
        int removedMarkers = 0;

        foreach (SeededSpawnMarker2D marker in scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<SeededSpawnMarker2D>(true))
            .ToArray())
        {
            UnityEngine.Object.DestroyImmediate(marker.gameObject);
            removedMarkers++;
        }

        int removedGenerators = 0;

        foreach (SeededIslandContentGenerator generator in scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<
                    SeededIslandContentGenerator>(true))
            .ToArray())
        {
            UnityEngine.Object.DestroyImmediate(generator);
            removedGenerators++;
        }

        foreach (Enemy enemy in scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Enemy>(true))
            .ToArray())
        {
            UnityEngine.Object.DestroyImmediate(enemy.gameObject);
        }

        Debug.Log(
            $"[Shop Island] Removed {removedMarkers} spawn markers and " +
            $"{removedGenerators} content generators."
        );
    }

    private static void ClearPreviousDistrict(Scene scene)
    {
        GameObject existing = scene
            .GetRootGameObjects()
            .FirstOrDefault(root => root.name == DistrictRootName);

        if (existing != null)
        {
            UnityEngine.Object.DestroyImmediate(existing);
        }
    }

    // ------------------------------------------------------------------
    // Plaza dressing
    // ------------------------------------------------------------------

    /// <summary>
    /// Replaces the wilderness camp fence with an open market plaza: a
    /// paved-feeling run of stalls, awnings, barrels and crates, with the
    /// approach left clear so players can walk straight in.
    /// </summary>
    private static void PaintPlaza(Scene scene)
    {
        Tilemap props = FindTilemap(scene, "Tilemap_Props");
        Tilemap overhead = FindTilemap(scene, "Tilemap_Overhead");
        Tilemap detail = FindTilemap(scene, "Tilemap_GroundDetail");
        Tilemap obstacle = FindTilemap(scene, "Tilemap_ObstacleCollision");

        Tile obstacleTile =
            AssetDatabase.LoadAssetAtPath<Tile>(ObstacleCollisionTilePath);

        // Wipe the inherited camp/graveyard dressing across the plaza so the
        // market is not built on top of a fenced wilderness camp.
        for (int x = -18; x <= 6; x++)
        {
            for (int y = -1; y <= 10; y++)
            {
                Vector3Int cell = new Vector3Int(x, y, 0);
                props.SetTile(cell, null);
                overhead.SetTile(cell, null);
                detail.SetTile(cell, null);
                obstacle.SetTile(cell, null);
            }
        }

        // Stall counters behind each trader, plus awnings above them.
        foreach (VendorSpec vendor in Vendors)
        {
            int x = Mathf.FloorToInt(vendor.Position.x);
            int y = Mathf.FloorToInt(vendor.Position.y);

            PlaceTile(props, obstacle, obstacleTile, x, y + 1, "vendor_stand_a");
            PlaceTile(props, obstacle, obstacleTile, x + 1, y + 1, "vendor_stand_b");
            PlaceTile(overhead, null, null, x, y + 2, "tentw_ml");
            PlaceTile(overhead, null, null, x + 1, y + 2, "tentw_mr");
        }

        // Market clutter, all clear of the walking lanes between stalls.
        (int X, int Y, string Tile, bool Blocking)[] clutter =
        {
            (-16, 6, "barrels_double", true),
            (-16, 5, "barrel_open", true),
            (-15, 8, "logpile", true),
            (-11, 8, "barrel_basket", true),
            (-7, 8, "jug", false),
            (-3, 8, "pot_flower", false),
            (-2, 6, "barrel_open", true),
            (1, 6, "barrels_double", true),
            (1, 4, "well_or_torch", false),
            (-17, 2, "bush_a", false),
            (2, 2, "bush_b", false),
            (-13, 0, "flowers", false),
            (-5, -1, "flowers", false),
        };

        foreach ((int cx, int cy, string tile, bool blocking) in clutter)
        {
            string resolved = tile == "well_or_torch" ? "torch" : tile;
            PlaceTile(
                props,
                blocking ? obstacle : null,
                blocking ? obstacleTile : null,
                cx,
                cy,
                resolved
            );
        }

        // Lantern posts marking the plaza entrance from the beach trail.
        PlaceTile(props, null, null, -9, -1, "torch");
        PlaceTile(props, null, null, -6, -1, "torch");
    }

    private static void PlaceTile(
        Tilemap target,
        Tilemap obstacleMap,
        Tile obstacleTile,
        int x,
        int y,
        string tileName
    )
    {
        Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(
            $"{TileFolder}/orpg_{tileName}.asset"
        );

        if (tile == null)
        {
            Debug.LogWarning($"[Shop Island] Missing tile orpg_{tileName}.");
            return;
        }

        Vector3Int cell = new Vector3Int(x, y, 0);
        target.SetTile(cell, tile);

        if (obstacleMap != null && obstacleTile != null)
        {
            obstacleMap.SetTile(cell, obstacleTile);
        }
    }

    // ------------------------------------------------------------------
    // Traders and townsfolk
    // ------------------------------------------------------------------

    private static void BuildStalls(Transform districtRoot)
    {
        foreach (VendorSpec spec in Vendors)
        {
            GameObject vendorObject = new GameObject(spec.ObjectName);
            vendorObject.transform.SetParent(districtRoot, false);
            vendorObject.transform.position = spec.Position;

            vendorObject.AddComponent<NetworkObject>();

            BoxCollider2D trigger =
                vendorObject.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.size = new Vector2(1.8f, 2f);

            NetworkShopVendor vendor =
                vendorObject.AddComponent<NetworkShopVendor>();

            SetSerializedString(vendor, "vendorName", spec.VendorName);
            SetSerializedEnum(vendor, "stock", (int)spec.Stock);
            SetSerializedInt(vendor, "basePrice", spec.BasePrice);
            SetSerializedInt(
                vendor,
                "priceIncreasePerPurchase",
                spec.PriceStep
            );
            SetSerializedInt(
                vendor,
                "purchaseLimitPerPlayer",
                spec.Limit
            );

            // Stalls restock, so the interaction must not be one-shot.
            SetSerializedBool(vendor, "allowRepeatedInteraction", true);
            SetSerializedFloat(vendor, "additionalServerRange", 0.75f);

            AddNpcVisual(vendorObject.transform, spec.MotwIndex);
        }
    }

    private static void BuildTownsfolk(Transform districtRoot)
    {
        foreach ((string name, int motw, Vector2 position) in Townsfolk)
        {
            GameObject person = new GameObject(name);
            person.transform.SetParent(districtRoot, false);
            person.transform.position = position;

            AddNpcVisual(person.transform, motw);
        }
    }

    private static void AddNpcVisual(Transform parent, int motwIndex)
    {
        GameObject visual = new GameObject("Visual");
        visual.transform.SetParent(parent, false);
        visual.transform.localScale =
            new Vector3(NpcScale, NpcScale, 1f);

        SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
        renderer.sprite = LoadMotwSprite(motwIndex);

        // Above the props tilemap so a trader is never hidden by their own
        // stall counter.
        renderer.sortingOrder = 20;
    }

    private static Sprite LoadMotwSprite(int index)
    {
        Sprite sprite = AssetDatabase
            .LoadAllAssetRepresentationsAtPath(MotwSheetPath)
            .OfType<Sprite>()
            .FirstOrDefault(candidate => candidate.name == $"motw_{index}");

        if (sprite == null)
        {
            throw new InvalidOperationException(
                $"motw_{index} is not sliced in {MotwSheetPath}."
            );
        }

        return sprite;
    }

    // ------------------------------------------------------------------
    // Coins
    // ------------------------------------------------------------------

    private static GameObject CreateCoinPrefab()
    {
        GameObject root = new GameObject("NetworkCoinPickup");

        try
        {
            root.AddComponent<NetworkObject>();

            CircleCollider2D trigger =
                root.AddComponent<CircleCollider2D>();
            trigger.isTrigger = true;
            trigger.radius = 0.45f;

            NetworkCoinPickup coin = root.AddComponent<NetworkCoinPickup>();
            SetSerializedInt(coin, "coinValue", 5);

            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);

            SpriteRenderer renderer =
                visual.AddComponent<SpriteRenderer>();
            renderer.sprite = LoadCoinSprite();
            renderer.sortingOrder = 18;

            PrefabUtility.SaveAsPrefabAsset(root, CoinPrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(CoinPrefabPath);
    }

    private const string CoinSpritePath =
        "Assets/DeadmansTales/Art_Pixel/Props/coin.png";

    /// <summary>
    /// Neither tileset ships a coin, and pressing some other prop into
    /// service would leave the game's only currency reading as scenery.
    /// This draws a 16px gold coin once — rim, body, and a highlight —
    /// and imports it at 32 pixels-per-unit so it sits at half a world
    /// unit, small enough to read as loose change beside a ~1 unit player.
    /// </summary>
    private static Sprite LoadCoinSprite()
    {
        if (!System.IO.File.Exists(CoinSpritePath))
        {
            GenerateCoinTexture();
        }

        Sprite sprite =
            AssetDatabase.LoadAssetAtPath<Sprite>(CoinSpritePath);

        if (sprite == null)
        {
            GenerateCoinTexture();
            sprite = AssetDatabase.LoadAssetAtPath<Sprite>(CoinSpritePath);
        }

        if (sprite == null)
        {
            throw new InvalidOperationException(
                "The coin sprite failed to import."
            );
        }

        return sprite;
    }

    private static void GenerateCoinTexture()
    {
        const int size = 16;

        Color32 clear = new Color32(0, 0, 0, 0);
        Color32 rim = new Color32(120, 78, 16, 255);
        Color32 body = new Color32(226, 170, 44, 255);
        Color32 shine = new Color32(252, 226, 138, 255);

        Texture2D texture =
            new Texture2D(size, size, TextureFormat.RGBA32, false);

        try
        {
            Color32[] pixels = new Color32[size * size];
            Vector2 centre = new Vector2(size / 2f - 0.5f, size / 2f - 0.5f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(
                        new Vector2(x, y),
                        centre
                    );

                    Color32 colour;

                    if (distance > 6.6f)
                    {
                        colour = clear;
                    }
                    else if (distance > 5.2f)
                    {
                        colour = rim;
                    }
                    else if (
                        distance < 3.4f &&
                        x - y < -1
                    )
                    {
                        colour = shine;
                    }
                    else
                    {
                        colour = body;
                    }

                    pixels[y * size + x] = colour;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            System.IO.File.WriteAllBytes(
                System.IO.Path.Combine(
                    System.IO.Directory.GetCurrentDirectory(),
                    CoinSpritePath.Replace('/', System.IO.Path.DirectorySeparatorChar)
                ),
                texture.EncodeToPNG()
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }

        AssetDatabase.ImportAsset(CoinSpritePath);

        TextureImporter importer =
            (TextureImporter)AssetImporter.GetAtPath(CoinSpritePath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 32;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
    }

    private static void PlaceCoins(
        Transform districtRoot,
        GameObject coinPrefab
    )
    {
        GameObject coinRoot = new GameObject("Coins");
        coinRoot.transform.SetParent(districtRoot, false);

        for (int index = 0; index < CoinPositions.Length; index++)
        {
            GameObject coin = (GameObject)PrefabUtility.InstantiatePrefab(
                coinPrefab,
                coinRoot.transform
            );

            coin.name = $"Coin_{index:D2}";
            coin.transform.position = CoinPositions[index];
        }
    }

    // ------------------------------------------------------------------
    // Scene wiring
    // ------------------------------------------------------------------

    private static void EnsureCoinHud(Scene scene)
    {
        CoinPurseHUD existing = scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<CoinPurseHUD>(true))
            .FirstOrDefault();

        if (existing != null)
        {
            return;
        }

        GameObject hud = new GameObject("CoinPurseHUD");
        hud.AddComponent<CoinPurseHUD>();
    }

    /// <summary>
    /// The inherited exit still points at the boat, which is right — the
    /// shop is a stop on the voyage — but the copied island gated it behind
    /// "defeat all enemies" and there are no enemies here, so that gate is
    /// cleared or players would be trapped in the shop forever.
    /// </summary>
    private static void PointExitAtBoat(Scene scene)
    {
        NetworkStagePortal portal = scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<NetworkStagePortal>(true))
            .FirstOrDefault();

        if (portal == null)
        {
            Debug.LogWarning(
                "[Shop Island] No stage portal found; players would be " +
                "unable to leave."
            );
            return;
        }

        SetSerializedString(
            portal,
            "destinationSceneName",
            "Boat_Gameplay_2D"
        );
        SetSerializedBool(portal, "requireAllEnemiesDefeated", false);
        SetSerializedBool(portal, "requireGenerationComplete", false);
        SetSerializedBool(portal, "advanceStage", true);
    }

    /// <summary>
    /// The stage portals load scenes by name, and a scene missing from
    /// Build Settings simply fails to load at runtime with no warning in
    /// the editor.
    /// </summary>
    private static void EnsureBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes =
            EditorBuildSettings.scenes.ToList();

        if (scenes.Any(scene => scene.path == ShopScenePath))
        {
            return;
        }

        scenes.Add(new EditorBuildSettingsScene(ShopScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();

        Debug.Log("[Shop Island] Added the shop scene to Build Settings.");
    }

    private static void RegisterNetworkPrefabs(GameObject[] prefabs)
    {
        NetworkPrefabsList generatedList =
            AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(
                GeneratedNetworkPrefabsPath
            );

        if (generatedList != null)
        {
            foreach (GameObject prefab in prefabs)
            {
                if (prefab != null && !generatedList.Contains(prefab))
                {
                    generatedList.Add(new NetworkPrefab
                    {
                        Override = NetworkPrefabOverride.None,
                        Prefab = prefab,
                    });
                }
            }

            EditorUtility.SetDirty(generatedList);
        }

        DeadmansNetworkBootstrapSettings settings =
            AssetDatabase.LoadAssetAtPath<DeadmansNetworkBootstrapSettings>(
                BootstrapSettingsPath
            );

        if (settings == null)
        {
            return;
        }

        List<GameObject> additional = settings
            .AdditionalNetworkPrefabs
            .Where(prefab => prefab != null)
            .Distinct()
            .ToList();

        foreach (GameObject prefab in prefabs)
        {
            if (prefab != null && !additional.Contains(prefab))
            {
                additional.Add(prefab);
            }
        }

        SerializedObject settingsObject = new SerializedObject(settings);
        SerializedProperty property =
            settingsObject.FindProperty("additionalNetworkPrefabs");

        property.arraySize = additional.Count;
        for (int index = 0; index < additional.Count; index++)
        {
            property.GetArrayElementAtIndex(index).objectReferenceValue =
                additional[index];
        }

        settingsObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(settings);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Tilemap FindTilemap(Scene scene, string name)
    {
        Tilemap map = scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Tilemap>(true))
            .FirstOrDefault(candidate => candidate.name == name);

        if (map == null)
        {
            throw new InvalidOperationException(
                $"{name} was not found in the shop island scene."
            );
        }

        return map;
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

    private static void SetSerializedInt(
        UnityEngine.Object target,
        string propertyName,
        int value
    )
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null)
        {
            property.intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void SetSerializedEnum(
        UnityEngine.Object target,
        string propertyName,
        int value
    )
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null)
        {
            property.enumValueIndex = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
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
}
