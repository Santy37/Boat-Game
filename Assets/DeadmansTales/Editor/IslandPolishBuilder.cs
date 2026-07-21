using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DeadmansTales.WorldGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Turns the post-ocean island from a bare beach into a dressed game
/// level using the CC0 openRPG tilesets:
///  - A fenced OUTFITTER CAMP around the guaranteed-reward spot: tents,
///    campfire, well, vendor stands, barrels — with always-spawn weapon
///    and upgrade chest markers, so the island is where crews gear up.
///  - A worn dirt trail from the player spawns to the camp and on to the
///    exit pier.
///  - A small pirate graveyard, plus seeded decoration scatter (bushes,
///    trees, stumps, flowers, bones) across the interior.
///
/// Everything this builder paints uses tiles whose names start with
/// "orpg_", so a rerun first erases exactly its own output (including the
/// obstacle-collision cells it added) and repaints — fully idempotent.
/// It never touches the teammate-owned boat scene.
/// </summary>
public static class IslandPolishBuilder
{
    private const string MenuPath = "Deadman's Tales/Island Polish";

    private const string IslandScenePath =
        "Assets/DeadmansTales/Scenes/Island_After_Ocean_01_2D.unity";

    private const string OpenRpgFolder =
        "Assets/DeadmansTales/Art_Pixel/Tiles/OpenRPG";
    private const string ExteriorSheetPath =
        OpenRpgFolder + "/openrpg_exterior.png";
    private const string WorldSheetPath =
        OpenRpgFolder + "/openrpg_world.png";

    private const string TileFolder =
        "Assets/DeadmansTales/Art_Pixel/Tiles/OpenRPGTiles";

    private const string ObstacleCollisionTilePath =
        "Assets/DeadmansTales/Palettes/Island_ObstacleCollision_Grid.asset";

    private const string WeaponChestPrefabPath =
        "Assets/DeadmansTales/Prefabs/Gameplay/NetworkRewardChest_Weapon.prefab";
    private const string UpgradeChestPrefabPath =
        "Assets/DeadmansTales/Prefabs/Gameplay/NetworkRewardChest_Upgrade.prefab";

    private const int SheetColumns = 30;
    private const int CellPixels = 16;
    private const int PixelsPerUnit = 16;

    private const int ScatterSeed = 20260720;

    // Camp footprint in cell coordinates (inclusive). West of the island
    // crossroads: the architecture tests guarantee two walkable
    // corridors — a serpentine north-south route inside x [-2, 2] and an
    // east dock band at y 1..3 for x 0..21 — and the camp (whose fence
    // adds obstacle collision) must never intersect either.
    private const int CampMinX = -16;
    private const int CampMaxX = -6;
    private const int CampMinY = 1;
    private const int CampMaxY = 9;

    private enum Layer
    {
        Detail,
        Props,
        Overhead,
    }

    private sealed class Deco
    {
        public Deco(string sheet, int col, int row)
        {
            Sheet = sheet;
            Col = col;
            Row = row;
        }

        public string Sheet { get; }
        public int Col { get; }
        public int Row { get; }
    }

    // Curated openRPG tiles, verified visually cell by cell.
    // "e" = exterior sheet, "w" = world sheet; coordinates are (col, row)
    // with row 0 at the TOP of the sheet.
    private static readonly Dictionary<string, Deco> Tiles =
        new Dictionary<string, Deco>
        {
            ["trail_dirt"] = new Deco("w", 7, 2),
            ["fence_h"] = new Deco("e", 19, 12),
            ["fence_v"] = new Deco("w", 24, 1),
            ["gate_post"] = new Deco("e", 19, 15),
            ["tentw_tl"] = new Deco("e", 27, 13),
            ["tentw_tr"] = new Deco("e", 28, 13),
            ["tentw_ml"] = new Deco("e", 27, 14),
            ["tentw_mr"] = new Deco("e", 28, 14),
            ["tentw_bl"] = new Deco("e", 27, 15),
            ["tentw_br"] = new Deco("e", 28, 15),
            ["bigwell_tl"] = new Deco("e", 24, 0),
            ["bigwell_tr"] = new Deco("e", 25, 0),
            ["bigwell_bl"] = new Deco("e", 24, 1),
            ["bigwell_br"] = new Deco("e", 25, 1),
            ["campfire"] = new Deco("e", 21, 12),
            ["embers"] = new Deco("e", 21, 13),
            ["sign_big"] = new Deco("e", 20, 14),
            ["torch"] = new Deco("w", 24, 4),
            ["sack"] = new Deco("e", 28, 5),
            ["jug"] = new Deco("e", 22, 11),
            ["pot_flower"] = new Deco("e", 21, 11),
            ["barrel_open"] = new Deco("e", 26, 6),
            ["barrel_basket"] = new Deco("w", 25, 9),
            ["barrels_double"] = new Deco("e", 19, 11),
            ["logpile"] = new Deco("e", 28, 2),
            ["vendor_stand_a"] = new Deco("e", 24, 2),
            ["vendor_stand_b"] = new Deco("w", 24, 7),
            ["skullbones"] = new Deco("e", 23, 12),
            ["leaves"] = new Deco("w", 27, 3),
            ["flowers"] = new Deco("e", 18, 11),
            ["stump"] = new Deco("e", 19, 8),
            ["bush_a"] = new Deco("e", 20, 8),
            ["bush_b"] = new Deco("e", 19, 9),
            ["tree_round"] = new Deco("w", 19, 8),
            ["deadtree"] = new Deco("w", 20, 8),
            ["bigtree_tl"] = new Deco("e", 22, 8),
            ["bigtree_tr"] = new Deco("e", 23, 8),
            ["bigtree_bl"] = new Deco("e", 22, 9),
            ["bigtree_br"] = new Deco("e", 23, 9),
            ["grave_cross"] = new Deco("e", 23, 10),
            ["grave_metal"] = new Deco("e", 23, 13),
        };

    [MenuItem(MenuPath)]
    public static void BuildAll()
    {
        SliceTileSheet(ExteriorSheetPath);
        SliceTileSheet(WorldSheetPath);
        AssetDatabase.Refresh();

        CreateTileAssets();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        PolishIslandScene();
    }

    public static void BuildAllFromCommandLine()
    {
        BuildAll();
    }

    // ------------------------------------------------------------------
    // Sheet slicing + tile assets
    // ------------------------------------------------------------------

    private static void SliceTileSheet(string assetPath)
    {
        TextureImporter importer =
            (TextureImporter)AssetImporter.GetAtPath(assetPath);

        if (importer == null)
        {
            throw new InvalidOperationException(
                $"Missing tile sheet: {assetPath}"
            );
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression =
            TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;

        importer.GetSourceTextureWidthAndHeight(
            out int width,
            out int height
        );

        int columns = width / CellPixels;
        int rows = height / CellPixels;
        string baseName =
            System.IO.Path.GetFileNameWithoutExtension(assetPath);

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        ISpriteEditorDataProvider provider =
            factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();

        List<SpriteRect> spriteRects = new List<SpriteRect>();
        List<SpriteNameFileIdPair> nameFileIdPairs =
            new List<SpriteNameFileIdPair>();

        int index = 0;
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                string spriteName = $"{baseName}_{index}";
                GUID spriteId = DeterministicGuid(assetPath + spriteName);

                spriteRects.Add(
                    new SpriteRect
                    {
                        name = spriteName,
                        spriteID = spriteId,
                        rect = new Rect(
                            column * CellPixels,
                            height - (row + 1) * CellPixels,
                            CellPixels,
                            CellPixels
                        ),
                        alignment = SpriteAlignment.Center,
                        pivot = new Vector2(0.5f, 0.5f),
                    }
                );
                nameFileIdPairs.Add(
                    new SpriteNameFileIdPair(spriteName, spriteId)
                );
                index++;
            }
        }

        provider.SetSpriteRects(spriteRects.ToArray());

        ISpriteNameFileIdDataProvider nameProvider =
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        nameProvider.SetNameFileIdPairs(nameFileIdPairs);

        provider.Apply();
        importer.SaveAndReimport();
    }

    private static Sprite LoadDecoSprite(Deco deco)
    {
        string sheetPath = deco.Sheet == "e"
            ? ExteriorSheetPath
            : WorldSheetPath;
        string baseName =
            System.IO.Path.GetFileNameWithoutExtension(sheetPath);
        int index = deco.Row * SheetColumns + deco.Col;
        string spriteName = $"{baseName}_{index}";

        Sprite sprite = AssetDatabase
            .LoadAllAssetRepresentationsAtPath(sheetPath)
            .OfType<Sprite>()
            .FirstOrDefault(candidate => candidate.name == spriteName);

        if (sprite == null)
        {
            throw new InvalidOperationException(
                $"Sprite {spriteName} did not import from {sheetPath}."
            );
        }

        return sprite;
    }

    private static void CreateTileAssets()
    {
        if (!AssetDatabase.IsValidFolder(TileFolder))
        {
            string parent = System.IO.Path
                .GetDirectoryName(TileFolder)
                ?.Replace('\\', '/');
            AssetDatabase.CreateFolder(
                parent,
                System.IO.Path.GetFileName(TileFolder)
            );
        }

        foreach (KeyValuePair<string, Deco> entry in Tiles)
        {
            string tilePath = $"{TileFolder}/orpg_{entry.Key}.asset";
            Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);

            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, tilePath);
            }

            tile.sprite = LoadDecoSprite(entry.Value);
            tile.colliderType = Tile.ColliderType.None;
            EditorUtility.SetDirty(tile);
        }
    }

    private static Tile LoadOwnedTile(string name)
    {
        Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(
            $"{TileFolder}/orpg_{name}.asset"
        );

        if (tile == null)
        {
            throw new InvalidOperationException(
                $"Polish tile orpg_{name} is missing."
            );
        }

        return tile;
    }

    // ------------------------------------------------------------------
    // Scene painting
    // ------------------------------------------------------------------

    private static Tilemap ground;
    private static Tilemap groundDetail;
    private static Tilemap props;
    private static Tilemap overhead;
    private static Tilemap waterCollision;
    private static Tilemap obstacleCollision;
    private static Tile obstacleTile;
    private static readonly HashSet<Vector3Int> trailCells =
        new HashSet<Vector3Int>();
    private static readonly HashSet<Vector3Int> blockedByUs =
        new HashSet<Vector3Int>();
    private static List<Vector2> keepClearPoints = new List<Vector2>();

    private static void PolishIslandScene()
    {
        Scene scene = EditorSceneManager.OpenScene(
            IslandScenePath,
            OpenSceneMode.Single
        );

        ground = FindTilemap(scene, "Tilemap_Ground");
        groundDetail = FindTilemap(scene, "Tilemap_GroundDetail");
        props = FindTilemap(scene, "Tilemap_Props");
        overhead = FindTilemap(scene, "Tilemap_Overhead");
        waterCollision = FindTilemap(scene, "Tilemap_WaterCollision");
        obstacleCollision = FindTilemap(scene, "Tilemap_ObstacleCollision");

        obstacleTile = AssetDatabase.LoadAssetAtPath<Tile>(
            ObstacleCollisionTilePath
        );

        if (obstacleTile == null)
        {
            throw new InvalidOperationException(
                "Island obstacle collision tile asset is missing."
            );
        }

        trailCells.Clear();
        blockedByUs.Clear();

        RemovePreviousPolish();
        CollectKeepClearPoints(scene);
        RelocateMarkersOutOfCamp(scene);
        ClearCampFootprint();

        PaintTrail();
        PaintCamp();
        PaintGraveyard();
        ScatterDecoration();

        EnsureCampChestMarkers(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log(
            "[Island Polish] Outfitter camp, trail, graveyard, and " +
            $"decoration painted; {trailCells.Count} trail cells, " +
            $"{blockedByUs.Count} obstacle cells added."
        );
    }

    private static Tilemap FindTilemap(Scene scene, string name)
    {
        Tilemap map = scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Tilemap>(true))
            .FirstOrDefault(candidate => candidate.name == name);

        if (map == null)
        {
            throw new InvalidOperationException(
                $"{name} was not found in the island scene."
            );
        }

        return map;
    }

    /// <summary>
    /// Erases everything a previous polish run painted: any cell holding
    /// an "orpg_" tile, plus the obstacle-collision entries that were
    /// added for those cells.
    /// </summary>
    private static void RemovePreviousPolish()
    {
        int cleared = 0;

        foreach (Tilemap map in new[] { groundDetail, props, overhead })
        {
            BoundsInt bounds = map.cellBounds;

            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    Vector3Int cell = new Vector3Int(x, y, 0);
                    TileBase tile = map.GetTile(cell);

                    if (tile == null ||
                        !tile.name.StartsWith(
                            "orpg_",
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    map.SetTile(cell, null);
                    cleared++;

                    if (obstacleCollision.GetTile(cell) != null)
                    {
                        obstacleCollision.SetTile(cell, null);
                    }
                }
            }
        }

        if (cleared > 0)
        {
            Debug.Log(
                $"[Island Polish] Cleared {cleared} cells from the " +
                "previous polish pass."
            );
        }
    }

    private static void CollectKeepClearPoints(Scene scene)
    {
        keepClearPoints = new List<Vector2>();

        foreach (SeededSpawnMarker2D marker in scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<SeededSpawnMarker2D>(true)))
        {
            keepClearPoints.Add(marker.transform.position);
        }

        foreach (Transform candidate in scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<Transform>(true)))
        {
            string name = candidate.name;
            if (name.StartsWith("PlayerSpawn", StringComparison.Ordinal) ||
                name.Contains("Arrival") ||
                name.Contains("Exit") ||
                name.Contains("Portal"))
            {
                keepClearPoints.Add(candidate.position);
            }
        }
    }

    /// <summary>
    /// Enemy or loot markers caught inside the camp footprint would spawn
    /// hostiles inside the fence (or get walled in), so they are shifted
    /// east onto open ground.
    /// </summary>
    private static void RelocateMarkersOutOfCamp(Scene scene)
    {
        foreach (SeededSpawnMarker2D marker in scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<SeededSpawnMarker2D>(true))
            .Where(marker =>
                marker.Category == SeededContentCategory.Enemy ||
                marker.Category == SeededContentCategory.Loot))
        {
            Vector3 position = marker.transform.position;

            bool insideCamp =
                position.x >= CampMinX - 1.5f &&
                position.x <= CampMaxX + 1.5f &&
                position.y >= CampMinY - 1.5f &&
                position.y <= CampMaxY + 1.5f;

            if (!insideCamp)
            {
                continue;
            }

            Vector3 moved = position + new Vector3(13f, -1f, 0f);
            Vector3Int cell = new Vector3Int(
                Mathf.FloorToInt(moved.x),
                Mathf.FloorToInt(moved.y),
                0
            );

            if (ground.GetTile(cell) == null ||
                waterCollision.GetTile(cell) != null)
            {
                moved = position + new Vector3(-12f, -2f, 0f);
            }

            Debug.Log(
                $"[Island Polish] Moved {marker.name} out of the camp " +
                $"({position.x:F1},{position.y:F1}) -> " +
                $"({moved.x:F1},{moved.y:F1})."
            );
            marker.transform.position = moved;

            keepClearPoints.Add(moved);
        }
    }

    /// <summary>
    /// The camp interior becomes curated space: native palms, props, and
    /// detail inside the fence line are cleared before the camp is laid
    /// down.
    /// </summary>
    private static void ClearCampFootprint()
    {
        for (int x = CampMinX; x <= CampMaxX; x++)
        {
            for (int y = CampMinY; y <= CampMaxY; y++)
            {
                Vector3Int cell = new Vector3Int(x, y, 0);
                groundDetail.SetTile(cell, null);
                props.SetTile(cell, null);
                overhead.SetTile(cell, null);
                obstacleCollision.SetTile(cell, null);
            }
        }
    }

    // ------------------------------------------------------------------
    // Painting primitives
    // ------------------------------------------------------------------

    private static bool IsLand(Vector3Int cell)
    {
        return ground.GetTile(cell) != null &&
            waterCollision.GetTile(cell) == null;
    }

    private static void Place(
        Layer layer,
        Vector3Int cell,
        string tileName,
        bool blocking
    )
    {
        if (!IsLand(cell))
        {
            return;
        }

        Tilemap target = layer switch
        {
            Layer.Detail => groundDetail,
            Layer.Overhead => overhead,
            _ => props,
        };

        target.SetTile(cell, LoadOwnedTile(tileName));

        if (blocking)
        {
            obstacleCollision.SetTile(cell, obstacleTile);
            blockedByUs.Add(cell);
        }
    }

    // ------------------------------------------------------------------
    // Trail
    // ------------------------------------------------------------------

    private static readonly Vector2[][] TrailPolylines =
    {
        // Spawn beach west to the camp gate.
        new[]
        {
            new Vector2(1f, -10f),
            new Vector2(-3f, -6f),
            new Vector2(-9f, -2f),
            new Vector2(-11.5f, 0.5f),
            new Vector2(-11.5f, 3f),
        },
        // Fork east toward the exit pier.
        new[]
        {
            new Vector2(1f, -10f),
            new Vector2(3f, -6f),
            new Vector2(7f, -4f),
            new Vector2(12f, -1f),
            new Vector2(17f, 1f),
            new Vector2(21f, 2f),
        },
    };

    private static void PaintTrail()
    {
        Tile trail = LoadOwnedTile("trail_dirt");

        foreach (Vector2[] line in TrailPolylines)
        {
            for (int i = 0; i < line.Length - 1; i++)
            {
                Vector2 from = line[i];
                Vector2 to = line[i + 1];
                float length = Vector2.Distance(from, to);
                int steps = Mathf.CeilToInt(length * 3f);

                for (int step = 0; step <= steps; step++)
                {
                    Vector2 point = Vector2.Lerp(
                        from,
                        to,
                        step / (float)steps
                    );

                    // Single-cell width: a worn footpath, not a road.
                    Vector3Int cell = new Vector3Int(
                        Mathf.FloorToInt(point.x),
                        Mathf.FloorToInt(point.y),
                        0
                    );

                    if (!IsLand(cell) ||
                        props.GetTile(cell) != null ||
                        obstacleCollision.GetTile(cell) != null)
                    {
                        continue;
                    }

                    groundDetail.SetTile(cell, trail);
                    trailCells.Add(cell);
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // Outfitter camp
    // ------------------------------------------------------------------

    private static void PaintCamp()
    {
        // Fence line: gate on the south side where the trail enters.
        for (int x = CampMinX; x <= CampMaxX; x++)
        {
            Place(
                Layer.Props,
                new Vector3Int(x, CampMaxY, 0),
                "fence_h",
                true
            );

            bool isGate = x == -12 || x == -11;
            if (isGate)
            {
                continue;
            }

            string piece = x == -13 || x == -10 ? "gate_post" : "fence_h";
            Place(Layer.Props, new Vector3Int(x, CampMinY, 0), piece, true);
        }

        for (int y = CampMinY + 1; y < CampMaxY; y++)
        {
            Place(Layer.Props, new Vector3Int(CampMinX, y, 0), "fence_v", true);
            Place(Layer.Props, new Vector3Int(CampMaxX, y, 0), "fence_v", true);
        }

        // White canvas tent, west side. Top row renders overhead.
        Place(Layer.Overhead, new Vector3Int(-15, 8, 0), "tentw_tl", false);
        Place(Layer.Overhead, new Vector3Int(-14, 8, 0), "tentw_tr", false);
        Place(Layer.Props, new Vector3Int(-15, 7, 0), "tentw_ml", true);
        Place(Layer.Props, new Vector3Int(-14, 7, 0), "tentw_mr", true);
        Place(Layer.Props, new Vector3Int(-15, 6, 0), "tentw_bl", true);
        Place(Layer.Props, new Vector3Int(-14, 6, 0), "tentw_br", true);

        // Old stone well, east side.
        Place(Layer.Props, new Vector3Int(-8, 7, 0), "bigwell_tl", true);
        Place(Layer.Props, new Vector3Int(-7, 7, 0), "bigwell_tr", true);
        Place(Layer.Props, new Vector3Int(-8, 6, 0), "bigwell_bl", true);
        Place(Layer.Props, new Vector3Int(-7, 6, 0), "bigwell_br", true);

        // Hearth.
        Place(Layer.Props, new Vector3Int(-11, 4, 0), "campfire", true);
        Place(Layer.Detail, new Vector3Int(-10, 4, 0), "embers", false);

        // The outfitters themselves: two stands, chests spawn in front.
        Place(Layer.Props, new Vector3Int(-13, 6, 0), "vendor_stand_a", true);
        Place(Layer.Props, new Vector3Int(-12, 6, 0), "vendor_stand_b", true);
        Place(Layer.Props, new Vector3Int(-11, 6, 0), "torch", false);

        // Supplies scattered with intent.
        Place(Layer.Props, new Vector3Int(-15, 3, 0), "barrels_double", true);
        Place(Layer.Props, new Vector3Int(-15, 2, 0), "barrel_open", true);
        Place(Layer.Props, new Vector3Int(-14, 2, 0), "barrel_basket", true);
        Place(Layer.Props, new Vector3Int(-7, 2, 0), "logpile", true);
        Place(Layer.Props, new Vector3Int(-9, 3, 0), "sack", false);
        Place(Layer.Props, new Vector3Int(-13, 7, 0), "jug", false);
        Place(Layer.Props, new Vector3Int(-8, 4, 0), "pot_flower", false);
        Place(Layer.Detail, new Vector3Int(-8, 5, 0), "skullbones", false);

        // Gate dressing outside the fence.
        Place(Layer.Props, new Vector3Int(-13, 0, 0), "torch", false);
        Place(Layer.Props, new Vector3Int(-10, 0, 0), "torch", false);
        Place(Layer.Props, new Vector3Int(-9, 0, 0), "sign_big", true);
    }

    // ------------------------------------------------------------------
    // Graveyard
    // ------------------------------------------------------------------

    private static void PaintGraveyard()
    {
        Place(Layer.Props, new Vector3Int(9, 7, 0), "grave_cross", true);
        Place(Layer.Props, new Vector3Int(11, 6, 0), "grave_metal", true);
        Place(Layer.Props, new Vector3Int(10, 8, 0), "deadtree", true);
        Place(Layer.Detail, new Vector3Int(10, 6, 0), "skullbones", false);
        Place(Layer.Detail, new Vector3Int(9, 5, 0), "leaves", false);
        Place(Layer.Detail, new Vector3Int(11, 7, 0), "leaves", false);
    }

    // ------------------------------------------------------------------
    // Seeded scatter
    // ------------------------------------------------------------------

    private static void ScatterDecoration()
    {
        System.Random rng = new System.Random(ScatterSeed);

        List<Vector3Int> candidates = CollectScatterCandidates();
        Shuffle(candidates, rng);

        HashSet<Vector3Int> reserved = new HashSet<Vector3Int>();

        int bigTrees = PlaceBigTrees(candidates, reserved, 4);

        int trees = PlaceSingles(
            candidates, reserved, "tree_round", Layer.Props, true, 6, 2
        );
        trees += PlaceSingles(
            candidates, reserved, "deadtree", Layer.Props, true, 2, 2
        );
        int stumps = PlaceSingles(
            candidates, reserved, "stump", Layer.Props, true, 6, 2
        );
        int bushes = PlaceSingles(
            candidates, reserved, "bush_a", Layer.Props, false, 8, 1
        );
        bushes += PlaceSingles(
            candidates, reserved, "bush_b", Layer.Props, false, 8, 1
        );
        int details = PlaceSingles(
            candidates, reserved, "flowers", Layer.Detail, false, 22, 1
        );
        details += PlaceSingles(
            candidates, reserved, "leaves", Layer.Detail, false, 14, 1
        );
        details += PlaceSingles(
            candidates, reserved, "skullbones", Layer.Detail, false, 3, 3
        );

        Debug.Log(
            $"[Island Polish] Scatter: {bigTrees} big trees, {trees} " +
            $"trees, {stumps} stumps, {bushes} bushes, {details} ground " +
            "details."
        );
    }

    /// <summary>
    /// The cells the architecture tests require to stay walkable: the
    /// serpentine main route (x within [-2, 2] for y -11..14) and the
    /// east dock band (y 1..3 for x 0..21). Nothing blocking may ever be
    /// scattered onto them.
    /// </summary>
    private static HashSet<Vector3Int> ProtectedRouteCells()
    {
        HashSet<Vector3Int> cells = new HashSet<Vector3Int>();

        for (int y = -11; y <= 14; y++)
        {
            int pathX = Mathf.RoundToInt(
                Mathf.Sin((y + 10f) * 0.24f) * 1.7f
            );
            cells.Add(new Vector3Int(pathX, y, 0));
        }

        for (int x = 0; x <= 21; x++)
        {
            int pathY = Mathf.RoundToInt(2f + Mathf.Sin(x * 0.24f));
            cells.Add(new Vector3Int(x, pathY, 0));
        }

        return cells;
    }

    private static List<Vector3Int> CollectScatterCandidates()
    {
        HashSet<Vector3Int> protectedCells = ProtectedRouteCells();
        List<Vector3Int> result = new List<Vector3Int>();
        BoundsInt bounds = ground.cellBounds;

        for (int x = bounds.xMin; x < bounds.xMax; x++)
        {
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                Vector3Int cell = new Vector3Int(x, y, 0);

                if (!IsLand(cell))
                {
                    continue;
                }

                // Inland only: no water within a 2-cell ring, so scatter
                // never fights the shoreline art.
                bool nearWater = false;
                for (int dx = -2; dx <= 2 && !nearWater; dx++)
                {
                    for (int dy = -2; dy <= 2 && !nearWater; dy++)
                    {
                        Vector3Int probe =
                            new Vector3Int(x + dx, y + dy, 0);
                        if (ground.GetTile(probe) == null ||
                            waterCollision.GetTile(probe) != null)
                        {
                            nearWater = true;
                        }
                    }
                }

                if (nearWater)
                {
                    continue;
                }

                if (x >= CampMinX - 1 && x <= CampMaxX + 1 &&
                    y >= CampMinY - 1 && y <= CampMaxY + 1)
                {
                    continue;
                }

                if (protectedCells.Contains(cell))
                {
                    continue;
                }

                if (groundDetail.GetTile(cell) != null ||
                    props.GetTile(cell) != null ||
                    overhead.GetTile(cell) != null ||
                    obstacleCollision.GetTile(cell) != null)
                {
                    continue;
                }

                bool nearKeepClear = keepClearPoints.Any(point =>
                    Mathf.Abs(point.x - (x + 0.5f)) < 2.5f &&
                    Mathf.Abs(point.y - (y + 0.5f)) < 2.5f);

                if (nearKeepClear)
                {
                    continue;
                }

                result.Add(cell);
            }
        }

        return result;
    }

    private static void Shuffle(List<Vector3Int> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static bool IsReservedNear(
        HashSet<Vector3Int> reserved,
        Vector3Int cell,
        int radius
    )
    {
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                if (reserved.Contains(
                        new Vector3Int(cell.x + dx, cell.y + dy, 0)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int PlaceBigTrees(
        List<Vector3Int> candidates,
        HashSet<Vector3Int> reserved,
        int quota
    )
    {
        int placed = 0;
        HashSet<Vector3Int> candidateSet =
            new HashSet<Vector3Int>(candidates);

        foreach (Vector3Int cell in candidates)
        {
            if (placed >= quota)
            {
                break;
            }

            Vector3Int right = cell + Vector3Int.right;
            Vector3Int up = cell + Vector3Int.up;
            Vector3Int upRight = cell + new Vector3Int(1, 1, 0);

            if (!candidateSet.Contains(right) ||
                !candidateSet.Contains(up) ||
                !candidateSet.Contains(upRight))
            {
                continue;
            }

            if (IsReservedNear(reserved, cell, 3))
            {
                continue;
            }

            Place(Layer.Props, cell, "bigtree_bl", true);
            Place(Layer.Props, right, "bigtree_br", true);
            Place(Layer.Overhead, up, "bigtree_tl", false);
            Place(Layer.Overhead, upRight, "bigtree_tr", false);

            foreach (Vector3Int used in new[] { cell, right, up, upRight })
            {
                reserved.Add(used);
            }

            placed++;
        }

        return placed;
    }

    private static int PlaceSingles(
        List<Vector3Int> candidates,
        HashSet<Vector3Int> reserved,
        string tileName,
        Layer layer,
        bool blocking,
        int quota,
        int spacing
    )
    {
        int placed = 0;

        foreach (Vector3Int cell in candidates)
        {
            if (placed >= quota)
            {
                break;
            }

            if (IsReservedNear(reserved, cell, spacing))
            {
                continue;
            }

            if (groundDetail.GetTile(cell) != null ||
                props.GetTile(cell) != null ||
                overhead.GetTile(cell) != null)
            {
                continue;
            }

            Place(layer, cell, tileName, blocking);
            reserved.Add(cell);
            placed++;
        }

        return placed;
    }

    // ------------------------------------------------------------------
    // Camp chest markers
    // ------------------------------------------------------------------

    private static void EnsureCampChestMarkers(Scene scene)
    {
        GameObject markerRoot = scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Where(candidate => candidate.name == "SeededContentMarkers")
            .Select(candidate => candidate.gameObject)
            .FirstOrDefault();

        if (markerRoot == null)
        {
            markerRoot = new GameObject("SeededContentMarkers");
        }

        EnsureRewardMarker(
            scene,
            markerRoot.transform,
            "CampWeaponChestMarker",
            new Vector3(-12.5f, 5.3f, 0f),
            WeaponChestPrefabPath
        );
        EnsureRewardMarker(
            scene,
            markerRoot.transform,
            "CampUpgradeChestMarker",
            new Vector3(-10.5f, 5.3f, 0f),
            UpgradeChestPrefabPath
        );
    }

    private static void EnsureRewardMarker(
        Scene scene,
        Transform parent,
        string markerName,
        Vector3 position,
        string prefabPath
    )
    {
        GameObject prefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        if (prefab == null)
        {
            throw new InvalidOperationException(
                $"Chest prefab missing: {prefabPath}"
            );
        }

        GameObject markerObject = scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Where(candidate => candidate.name == markerName)
            .Select(candidate => candidate.gameObject)
            .FirstOrDefault();

        if (markerObject == null)
        {
            markerObject = new GameObject(markerName);
            markerObject.transform.SetParent(parent, false);
            markerObject.AddComponent<SeededSpawnMarker2D>();
        }

        markerObject.transform.position = position;

        SeededSpawnMarker2D marker =
            markerObject.GetComponent<SeededSpawnMarker2D>();
        SerializedObject serialized = new SerializedObject(marker);
        serialized.FindProperty("category").enumValueIndex =
            (int)SeededContentCategory.Reward;

        SerializedProperty prefabs =
            serialized.FindProperty("networkPrefabs");
        prefabs.arraySize = 1;
        prefabs.GetArrayElementAtIndex(0).objectReferenceValue = prefab;

        serialized.FindProperty("alwaysSpawn").boolValue = true;
        serialized.FindProperty("spawnChance").floatValue = 1f;
        serialized.FindProperty("minimumStage").intValue = 2;
        serialized.FindProperty("maximumStage").intValue = 0;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static GUID DeterministicGuid(string seed)
    {
        using MD5 md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(seed));
        StringBuilder builder = new StringBuilder(32);
        foreach (byte value in hash)
        {
            builder.Append(value.ToString("x2"));
        }

        GUID.TryParse(builder.ToString(), out GUID result);
        return result;
    }
}
