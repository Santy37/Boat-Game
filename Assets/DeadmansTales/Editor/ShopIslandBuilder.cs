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
    /// The traders are drawn from the very sheet the player renders from
    /// (motw_10), at the same pixels-per-unit, so they need no correction
    /// at all — scale 1 makes an NPC exactly as tall as a player, which is
    /// what a person standing next to another person should be.
    ///
    /// This was 0.61 while the player's size was being read from the
    /// placeholder sword icon on its prefab rather than the motw sprite
    /// its animator actually shows, which left every trader half-height.
    /// </summary>
    private const float NpcScale = 1f;

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

    // The market runs east-west along the top of the plaza. Each trader
    // stands just in front of their own counter, so the stall behind them
    // reads as their business and they are never hidden by their awning.
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
            new Vector2(-5f, 3.4f)
        ),
        new VendorSpec(
            "Vendor_Quartermaster",
            "Quartermaster Vex",
            ShopStock.Upgrade,
            10,
            30,
            20,
            0,
            new Vector2(-1f, 3.4f)
        ),
        new VendorSpec(
            "Vendor_Cook",
            "Ol' Sally",
            ShopStock.FullHeal,
            55,
            15,
            5,
            0,
            new Vector2(3f, 3.4f)
        ),
    };

    /// <summary>Stall sprite drawn behind each trader, in the same order.</summary>
    private static readonly string[] VendorStallProps =
    {
        "stall_red",
        "stall_blue",
        "stall_green",
    };

    // Idle townsfolk: no stall, purely to make the port feel inhabited.
    private static readonly (string Name, int Motw, Vector2 Position)[]
        Townsfolk =
        {
            ("Townsfolk_Dockhand", 1, new Vector2(-7f, 0.6f)),
            ("Townsfolk_Deckhand", 49, new Vector2(4.4f, 1.2f)),
            ("Townsfolk_Traveller", 52, new Vector2(-2.6f, -0.4f)),
            // Row 1 of a character block is its side-facing walk, so this
            // villager reads as browsing the stalls rather than staring
            // out of the screen like the rest.
            ("Townsfolk_Browser", 13, new Vector2(1f, 1.6f)),
        };

    /// <summary>
    /// Scenery that sells the market: a working forge behind the smith, a
    /// meat rack behind the cook, and crates and barrels stacked as if
    /// goods were just unloaded. Names refer to MarketArtBuilder props.
    /// </summary>
    /// <summary>
    /// Sorting orders count UP as props sit further south, so anything
    /// nearer the camera overlaps what is behind it and the market gains
    /// depth instead of looking flat.
    /// </summary>
    private static readonly (string Prop, Vector2 Position, int Order)[]
        Dressing =
        {
            // Trade tools behind the stalls they belong to.
            ("forge_stone", new Vector2(-6.5f, 4.6f), 6),
            ("meatrack", new Vector2(4.5f, 4.9f), 6),

            // Goods stacked against the back of the market row.
            ("crate", new Vector2(-3f, 4.8f), 7),
            ("barrel", new Vector2(-2.2f, 4.8f), 7),
            ("pot", new Vector2(1f, 4.9f), 7),
            ("vase", new Vector2(5.2f, 3.4f), 8),
            ("barrel", new Vector2(-7.4f, 2.6f), 9),
            ("crate", new Vector2(-7.4f, 1.4f), 11),

            // A facing row of counters turns the square into a street.
            ("stall_counter", new Vector2(-4.5f, 0.2f), 14),
            ("stall_counter", new Vector2(1.5f, 0.2f), 14),
            ("crate", new Vector2(-1.6f, 0.4f), 14),
            ("barrel", new Vector2(4.2f, 0.2f), 14),
            ("pot", new Vector2(-6.4f, -0.6f), 16),

            // Signposts either side of the southern entrance.
            ("market_sign", new Vector2(-2.5f, -1.4f), 18),
            ("market_sign", new Vector2(0.5f, -1.4f), 18),
        };

    // Coins the player can collect on a first visit so the stalls are not
    // dead content before they have plundered anywhere. Kept off the stall
    // line so they never sit under an awning.
    private static readonly Vector2[] CoinPositions =
    {
        new Vector2(-6f, 1.6f),
        new Vector2(-4f, 2.4f),
        new Vector2(-2f, 1.4f),
        new Vector2(0f, 2.6f),
        new Vector2(2f, 1.6f),
        new Vector2(4f, 2.6f),
        new Vector2(-5f, -0.6f),
        new Vector2(2.6f, -0.6f),
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
        CreateCobbleTiles();

        Transform districtRoot = new GameObject(DistrictRootName).transform;

        PaintPlaza(scene);
        BuildDressing(districtRoot);
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

        // The island this scene is built from is the multiplayer lobby,
        // which spawns a runtime wave of enemies. A market is the one
        // place in the run where players can stand still and shop.
        int removedSpawners = 0;

        foreach (NetworkSceneEnemySpawner2D spawner in scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<NetworkSceneEnemySpawner2D>(true))
            .ToArray())
        {
            UnityEngine.Object.DestroyImmediate(spawner);
            removedSpawners++;
        }

        if (removedSpawners > 0)
        {
            Debug.Log(
                $"[Shop Island] Removed {removedSpawners} runtime enemy " +
                "spawner(s) inherited from the lobby island."
            );
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

    // Plaza footprint in cells, sized for this island rather than the big
    // one: the market should sit ON the island with beach around it, not
    // cover it wall to wall.
    private const int PlazaMinX = -8;
    private const int PlazaMaxX = 5;
    private const int PlazaMinY = -1;
    private const int PlazaMaxY = 7;

    /// <summary>
    /// Lays a cobbled market square over the beach and clears the
    /// wilderness dressing the scene inherited.
    ///
    /// The paving is what makes this read as a port town rather than three
    /// stalls abandoned on a beach: it gives the market a floor, an edge,
    /// and somewhere the eye understands as "indoors". Edges are ragged by
    /// a cheap deterministic hash so the square does not look stamped.
    /// </summary>
    private static void PaintPlaza(Scene scene)
    {
        Tilemap props = FindTilemap(scene, "Tilemap_Props");
        Tilemap overhead = FindTilemap(scene, "Tilemap_Overhead");
        Tilemap ground = FindTilemap(scene, "Tilemap_Ground");
        Tilemap obstacle = FindTilemap(scene, "Tilemap_ObstacleCollision");

        // Clearing runs a generous margin WIDER than the paving. Palm
        // trees are several cells tall with their canopy on the overhead
        // layer, so clearing exactly the plaza rectangle slices trees in
        // half and leaves floating crowns and headless trunks around the
        // border — which is what made the first market look pasted on.
        const int clearMargin = 4;

        for (int x = PlazaMinX - clearMargin; x <= PlazaMaxX + clearMargin; x++)
        {
            for (int y = PlazaMinY - clearMargin; y <= PlazaMaxY + clearMargin; y++)
            {
                Vector3Int cell = new Vector3Int(x, y, 0);
                props.SetTile(cell, null);
                overhead.SetTile(cell, null);
                obstacle.SetTile(cell, null);
            }
        }

        Tile cobble = LoadShopTile("cobble_a");
        Tile border = LoadShopTile("cobble_edge");

        int paved = 0;

        for (int x = PlazaMinX; x <= PlazaMaxX; x++)
        {
            for (int y = PlazaMinY; y <= PlazaMaxY; y++)
            {
                if (IsRaggedEdge(x, y))
                {
                    continue;
                }

                // A ring of worn sandstone eases the cobble into the beach
                // instead of ending it against the sand like a cut-out.
                bool onRing =
                    x <= PlazaMinX + 1 ||
                    x >= PlazaMaxX - 1 ||
                    y <= PlazaMinY ||
                    y >= PlazaMaxY;

                // Painted onto the ground layer itself: the plaza replaces
                // the sand rather than sitting on a decal above it, so it
                // reads as the island's floor.
                ground.SetTile(
                    new Vector3Int(x, y, 0),
                    onRing ? border : cobble
                );
                paved++;
            }
        }

        Debug.Log($"[Shop Island] Paved {paved} plaza cells.");
    }

    /// <summary>
    /// Nibbles the outermost ring of the plaza so the paving meets the
    /// sand in a worn, irregular line instead of a perfect rectangle.
    /// </summary>
    private static bool IsRaggedEdge(int x, int y)
    {
        bool onEdge =
            x == PlazaMinX ||
            x == PlazaMaxX ||
            y == PlazaMinY ||
            y == PlazaMaxY;

        if (!onEdge)
        {
            return false;
        }

        return (Mathf.Abs(Hash(x, y)) % 10) < 4;
    }

    private static int Hash(int x, int y)
    {
        unchecked
        {
            int hash = x * 73856093 ^ y * 19349663 ^ ScatterSalt;
            hash ^= hash >> 13;
            return hash * 1274126177;
        }
    }

    private const int ScatterSalt = 20260721;

    private static Tile LoadShopTile(string name)
    {
        Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(
            $"{TileFolder}/orpg_{name}.asset"
        );

        if (tile == null)
        {
            throw new InvalidOperationException(
                $"Shop tile orpg_{name} is missing."
            );
        }

        return tile;
    }

    /// <summary>
    /// Creates the cobblestone tile assets the plaza is paved with, cut
    /// from the openRPG exterior sheet that the island polish pass already
    /// slices.
    /// </summary>
    private static void CreateCobbleTiles()
    {
        // Only fully-enclosed fill cells. The neighbouring cells in this
        // sheet are autotile EDGE pieces with wall bars baked into them,
        // which scatter stray dark bars across a plaza if used as fill.
        (string Name, int Column, int Row)[] cobbles =
        {
            ("cobble_a", 10, 10),
            ("cobble_edge", 4, 14),
        };

        foreach ((string name, int column, int row) in cobbles)
        {
            string path = $"{TileFolder}/orpg_{name}.asset";
            Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(path);

            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, path);
            }

            int index = row * 30 + column;

            Sprite sprite = AssetDatabase
                .LoadAllAssetRepresentationsAtPath(PropsSheetPath)
                .OfType<Sprite>()
                .FirstOrDefault(candidate =>
                    candidate.name == $"openrpg_exterior_{index}");

            if (sprite == null)
            {
                throw new InvalidOperationException(
                    "The openRPG exterior sheet is not sliced; run the " +
                    "island polish builder first."
                );
            }

            tile.sprite = sprite;
            tile.colliderType = Tile.ColliderType.None;
            EditorUtility.SetDirty(tile);
        }
    }

    /// <summary>
    /// Places the market's scenery as sprites rather than tiles: a stall is
    /// a 3x5 cell object that must sit at half-cell offsets and overlap its
    /// neighbours' sort order, which a 1-unit tile grid cannot express.
    /// </summary>
    private static void BuildDressing(Transform districtRoot)
    {
        GameObject dressingRoot = new GameObject("Dressing");
        dressingRoot.transform.SetParent(districtRoot, false);

        foreach ((string prop, Vector2 position, int order) in Dressing)
        {
            CreatePropObject(
                dressingRoot.transform,
                prop,
                position,
                order
            );
        }
    }

    private static GameObject CreatePropObject(
        Transform parent,
        string propName,
        Vector2 position,
        int sortingOrder
    )
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
            MarketArtBuilder.PropPath(propName)
        );

        if (sprite == null)
        {
            throw new InvalidOperationException(
                $"Market prop '{propName}' is missing; run " +
                "'Deadman's Tales/Build Market Art' first."
            );
        }

        GameObject prop = new GameObject(propName);
        prop.transform.SetParent(parent, false);
        prop.transform.position = position;

        SpriteRenderer renderer = prop.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = sortingOrder;

        return prop;
    }

    // ------------------------------------------------------------------
    // Traders and townsfolk
    // ------------------------------------------------------------------

    private static void BuildStalls(Transform districtRoot)
    {
        for (int index = 0; index < Vendors.Length; index++)
        {
            VendorSpec spec = Vendors[index];

            // The stall stands behind the trader, who is drawn in front of
            // their own counter so an awning never hides the person you
            // are meant to walk up to.
            CreatePropObject(
                districtRoot,
                VendorStallProps[index % VendorStallProps.Length],
                spec.Position + new Vector2(0f, 0.9f),
                8
            );
        }

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

        // Above the stalls (order 9) so a trader is never swallowed by
        // their own awning, and above the dressing so nothing occludes the
        // person you are meant to walk up to and talk to.
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
            // This island inherits the lobby's rowboat, which already
            // sails to the boat scene through LobbyRowboatInteraction, so
            // there is nothing to repoint. Confirm it exists rather than
            // silently leaving players stranded in the market.
            bool hasRowboatExit = scene
                .GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<LobbyRowboatInteraction>(true))
                .Any();

            if (!hasRowboatExit)
            {
                Debug.LogWarning(
                    "[Shop Island] No stage portal and no rowboat exit; " +
                    "players would be unable to leave."
                );
            }
            else
            {
                Debug.Log(
                    "[Shop Island] Exit is the inherited rowboat " +
                    "(press E to sail back to the ship)."
                );
            }

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
