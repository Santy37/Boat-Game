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
/// Turns Island_Shop_2D into Salt Harbour: the crew's safe port, where the
/// coins plundered on the hostile islands buy blade tiers, ship upgrades,
/// and hot meals from NPC traders around a cobbled market square.
///
/// COORDINATES. Everything here is authored in TILE CELLS and converted to
/// world space through the scene's own grid. That is not a stylistic
/// preference: the tilemaps live under Pixel_Art_Visuals at roughly
/// (2.17, 8.52), so a prop placed at "world (0, 4)" lands nine units south
/// of the paving painted at "cell (0, 4)". Mixing the two spaces is what
/// previously stranded the entire market on the shoreline below its own
/// plaza. Author in cells; let <see cref="CellPoint"/> do the conversion.
///
/// TERRAIN. Every build re-copies the ground, prop, overhead and collision
/// layers from the lobby island before touching anything, so the builder is
/// idempotent and can never accumulate damage across runs. An earlier pass
/// cleared a rectangle four cells wider than the plaza, which deleted 56 of
/// the island's 71 props, all 42 tree canopies, and half the dock — that is
/// what left the island a bare sandbox with a pier ending in mid-air.
///
/// Idempotent: everything it creates lives under a single "ShopDistrict"
/// root plus tiles stamped onto layers that are restored from scratch.
/// </summary>
public static class ShopIslandBuilder
{
    private const string MenuPath = "Deadman's Tales/Build Shop Island";

    private const string ShopScenePath =
        "Assets/DeadmansTales/Scenes/Island_Shop_2D.unity";

    /// <summary>
    /// The terrain donor. The shop island is a re-dressing of the lobby's
    /// landmass, so the lobby scene is the authoritative source for the
    /// coastline, the dock, and the palm groves.
    /// </summary>
    private const string LobbyScenePath =
        "Assets/DeadmansTales/Scenes/Lobby_Island_2D.unity";

    private const string HostileIslandScenePath =
        "Assets/DeadmansTales/Scenes/Island_After_Ocean_01_2D.unity";

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
    /// The dock and its rowboat are the island's only exit. Clusters
    /// reaching this far east are never cleared, whatever else overlaps
    /// them — a market is not worth trapping the crew in.
    /// </summary>
    private const int DockGuardX = 7;

    /// <summary>
    /// motw.png is a 12x8 grid of 52x72 cells in RPG-Maker layout: each
    /// character occupies a 3-wide by 4-tall block (3 walk frames, 4
    /// facings). Column 1 of a block, top row, is the forward-facing idle —
    /// the pose a stallholder should hold.
    /// </summary>
    private const int MotwColumns = 12;

    /// <summary>
    /// The traders are drawn from the very sheet the player renders from
    /// (motw_10, measured at 2.12 world units tall), at the same
    /// pixels-per-unit, so they need no correction: scale 1 makes an NPC
    /// exactly as tall as a player, which is what a person standing next to
    /// another person should be.
    /// </summary>
    private const float NpcScale = 1f;

    // ------------------------------------------------------------------
    // Town plan, in cells
    // ------------------------------------------------------------------

    /// <summary>
    /// The market square, as a rounded rectangle rather than an ellipse:
    /// the stall row needs a straight, full-width northern edge to stand
    /// on, which an ellipse pinches away.
    ///
    /// A plain rectangle is what made the first attempt read as a grey slab
    /// dropped on the beach, so the exponent below rounds the corners and
    /// <see cref="IsRaggedEdge"/> then nibbles the rim.
    /// </summary>
    private const float PlazaCentreX = 0f;
    private const float PlazaCentreY = 4f;
    private const float PlazaRadiusX = 6f;
    private const float PlazaRadiusY = 3.6f;
    private const float PlazaCorner = 3f;

    /// <summary>Cobbled street linking the square to the pier.</summary>
    private const int StreetMinX = 5;
    private const int StreetMaxX = 8;
    private const int StreetMinY = 1;
    private const int StreetMaxY = 2;

    private const int ScatterSalt = 20260721;

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
            int cellX,
            string stallProp
        )
        {
            ObjectName = objectName;
            VendorName = vendorName;
            Stock = stock;
            MotwIndex = motwIndex;
            BasePrice = basePrice;
            PriceStep = priceStep;
            Limit = limit;
            CellX = cellX;
            StallProp = stallProp;
        }

        public string ObjectName { get; }
        public string VendorName { get; }
        public ShopStock Stock { get; }
        public int MotwIndex { get; }
        public int BasePrice { get; }
        public int PriceStep { get; }
        public int Limit { get; }

        /// <summary>Cell the stall is centred on; each stall is 3 cells wide.</summary>
        public int CellX { get; }

        public string StallProp { get; }
    }

    /// <summary>Cell row the stall counters stand on.</summary>
    private const int StallRowY = 6;

    /// <summary>
    /// Traders stand half a cell in front of their own counter, so the
    /// awning never hides the person you are meant to walk up to, and the
    /// depth sort still draws them over their stall.
    /// </summary>
    private const float VendorRowY = 5.5f;

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
            -4,
            "stall_red"
        ),
        new VendorSpec(
            "Vendor_Quartermaster",
            "Quartermaster Vex",
            ShopStock.Upgrade,
            10,
            30,
            20,
            0,
            0,
            "stall_blue"
        ),
        new VendorSpec(
            "Vendor_Cook",
            "Ol' Sally",
            ShopStock.FullHeal,
            55,
            15,
            5,
            0,
            4,
            "stall_green"
        ),
    };

    // Idle townsfolk: no stall, purely to make the port feel inhabited.
    // Kept clear of cells (-2..1, 3) where the four players spawn.
    private static readonly (string Name, int Motw, float CellX, float CellY)[]
        Townsfolk =
        {
            ("Townsfolk_Dockhand", 1, 6.5f, 2.4f),
            ("Townsfolk_Deckhand", 49, -6.5f, 3.4f),
            ("Townsfolk_Traveller", 52, -3.5f, 1.2f),
            // Row 1 of a character block is its side-facing walk, so this
            // villager reads as browsing the stalls rather than staring
            // out of the screen like the rest.
            ("Townsfolk_Browser", 13, 2.5f, 7.2f),
        };

    /// <summary>
    /// Scenery that sells the market: a working forge in the smith's yard,
    /// a drying rack by the cook, and goods stacked as if just unloaded.
    /// Positions are the cell each prop stands in; props are centred on
    /// that cell with their base on its lower edge.
    /// </summary>
    private static readonly (string Prop, float CellX, float CellY)[]
        Dressing =
        {
            // Trade tools, in the yards either end of the stall row.
            ("forge_stone", -7.5f, 6f),
            ("meatrack", 6.5f, 5f),

            // Goods stacked in the two gaps between the three stalls,
            // which fall on cells -2 and 2.
            ("crate", -2.2f, 6.2f),
            ("barrel", -1.8f, 5.4f),
            ("pot", 2.2f, 6.2f),
            ("vase", 1.8f, 5.4f),
            ("barrel", -6.4f, 5.2f),
            ("crate", 6.4f, 6.4f),

            // A facing row of counters turns the square into a street.
            ("stall_counter", -2.5f, 1.2f),
            ("stall_counter", 2.5f, 1.2f),

            // Signposts either side of the road down to the pier.
            ("market_sign", 5.5f, 3.2f),
            ("market_sign", 5.5f, 0.2f),
        };

    /// <summary>
    /// Tile props stamped onto the prop layer. The well is the square's
    /// centrepiece; torches mark its corners after dark.
    /// </summary>
    private static readonly (string Tile, int CellX, int CellY)[] TownTiles =
    {
        // 2x2 well, west of the spawn point so nobody lands inside it.
        ("bigwell_bl", -5, 2),
        ("bigwell_br", -4, 2),
        ("bigwell_tl", -5, 3),
        ("bigwell_tr", -4, 3),

        ("torch", -6, 4),
        ("torch", 6, 4),
        ("torch", -4, 7),
        ("torch", 4, 7),

        ("campfire", 4, 2),
        ("logpile", 5, 2),

        // Kept off the stall row and the counter line: the prop tilemap
        // draws at order 2, below every market sprite, so anything placed
        // under a stall would simply be painted over.
        ("pot_flower", -2, 3),
        ("pot_flower", 2, 3),
        ("jug", 5, 3),
        ("sack", -6, 2),
        ("barrel_open", -5, 1),
        ("barrels_double", 5, 4),
    };

    /// <summary>Cells the well occupies, which players must not walk through.</summary>
    private static readonly Vector2Int[] SolidTownCells =
    {
        new Vector2Int(-5, 2),
        new Vector2Int(-4, 2),
        new Vector2Int(-5, 3),
        new Vector2Int(-4, 3),
    };

    /// <summary>
    /// How many coins are scattered around the square, so the stalls are
    /// not dead content before players have plundered anywhere.
    /// </summary>
    private const int CoinCount = 8;

    /// <summary>
    /// Cells the four players drop into. Coins auto-collect on touch, so
    /// anything left here would be vacuumed up before anyone had moved.
    /// </summary>
    private static readonly RectInt SpawnClearance =
        new RectInt(-3, 2, 6, 3);

    /// <summary>
    /// Anchors for extra palm groves, stamped from a grove the island
    /// already has so the replanting matches the existing art exactly
    /// instead of importing temperate trees onto a tropical beach.
    /// </summary>
    private static readonly Vector2Int[] PalmStampAnchors =
    {
        new Vector2Int(-9, 2),
        new Vector2Int(6, 5),
        new Vector2Int(-7, -4),
        new Vector2Int(0, -5),
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

        RestoreTerrainFromLobby(scene);
        StripHostileContent(scene);
        ClearPreviousDistrict(scene);
        CreateTownTiles();

        Tilemap ground = FindTilemap(scene, "Tilemap_Ground");
        Tilemap props = FindTilemap(scene, "Tilemap_Props");
        Tilemap overhead = FindTilemap(scene, "Tilemap_Overhead");
        Tilemap obstacle = FindTilemap(scene, "Tilemap_ObstacleCollision");

        HashSet<Vector2Int> footprint = BuildFootprint(ground);
        TileBase solidTile = FindSolidTile(obstacle);

        ClearTownFootprint(props, overhead, obstacle, footprint);
        PavePlaza(ground, footprint);

        PlantPalmGroves(props, overhead, ground);
        PlantTownTiles(props, obstacle, solidTile);
        ScatterUndergrowth(ground, props, overhead, footprint);

        Transform districtRoot = new GameObject(DistrictRootName).transform;

        BuildDressing(districtRoot, ground);
        BuildStalls(districtRoot, ground);
        BuildTownsfolk(districtRoot, ground);
        PlaceCoins(districtRoot, ground, footprint, coinPrefab);
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
            $"[Shop Island] Built {Vendors.Length} stalls and " +
            $"{Townsfolk.Length} townsfolk; hostile spawns removed."
        );
    }

    public static void BuildAllFromCommandLine()
    {
        BuildAll();
    }

    // ------------------------------------------------------------------
    // Cell space -> world space
    // ------------------------------------------------------------------

    /// <summary>
    /// Converts a position expressed in tile cells into world space, using
    /// the scene's own grid rather than assuming the tilemaps sit at the
    /// origin. They do not: they hang off Pixel_Art_Visuals, several units
    /// from it.
    ///
    /// The returned point is the BOTTOM of the cell, horizontally centred,
    /// matching the bottom-centre pivot every prop and character sprite in
    /// this game uses — so "place it at cell (4, 6)" means "it stands on
    /// the ground in cell (4, 6)".
    /// </summary>
    private static Vector3 CellPoint(Tilemap map, float cellX, float cellY)
    {
        Vector3 origin = map.CellToWorld(Vector3Int.zero);
        return new Vector3(origin.x + cellX + 0.5f, origin.y + cellY, 0f);
    }

    /// <summary>
    /// Depth sorting for a 2.5D top-down view: whatever stands further
    /// south is nearer the camera and must overlap what is behind it.
    ///
    /// The band 3..19 is deliberate — it sits between the prop tilemap
    /// (order 2, ground clutter) and the overhead tilemap (order 20, tree
    /// canopies that pass over the player's head).
    /// </summary>
    private static int DepthOrder(float worldY)
    {
        return Mathf.Clamp(Mathf.RoundToInt(21f - worldY), 3, 19);
    }

    // ------------------------------------------------------------------
    // Terrain
    // ------------------------------------------------------------------

    /// <summary>
    /// Re-copies the terrain layers from the lobby island, so each build
    /// starts from known-good ground instead of whatever the last build
    /// left behind. Without this the builder is destructive rather than
    /// idempotent: cobble painted over sand, or a grove cleared to make
    /// room, could never be undone by running it again.
    ///
    /// The water layers are deliberately excluded. The shoreline autotiling
    /// is ambiguous enough that reshaping it is a coin-flip on the most
    /// visible thing in the scene, so the coastline is left exactly as the
    /// lobby authored it and the island is differentiated by what is built
    /// on top of it.
    /// </summary>
    private static void RestoreTerrainFromLobby(Scene shop)
    {
        string[] layers =
        {
            "Tilemap_Ground",
            "Tilemap_Props",
            "Tilemap_Overhead",
            "Tilemap_ObstacleCollision",
        };

        Scene lobby = EditorSceneManager.OpenScene(
            LobbyScenePath,
            OpenSceneMode.Additive
        );

        try
        {
            foreach (string layer in layers)
            {
                Tilemap source = FindTilemap(lobby, layer);
                Tilemap destination = FindTilemap(shop, layer);

                destination.ClearAllTiles();

                BoundsInt bounds = source.cellBounds;
                destination.SetTilesBlock(
                    bounds,
                    source.GetTilesBlock(bounds)
                );
            }
        }
        finally
        {
            // Close without saving: the lobby island is read-only here.
            EditorSceneManager.CloseScene(lobby, true);
        }

        Debug.Log(
            "[Shop Island] Restored terrain layers from the lobby island."
        );
    }

    /// <summary>
    /// The cells the town occupies: the market square plus the road to the
    /// pier. Cells without ground are dropped so nothing is ever built out
    /// over the sea.
    /// </summary>
    private static HashSet<Vector2Int> BuildFootprint(Tilemap ground)
    {
        HashSet<Vector2Int> footprint = new HashSet<Vector2Int>();

        int minX = Mathf.FloorToInt(PlazaCentreX - PlazaRadiusX);
        int maxX = Mathf.CeilToInt(PlazaCentreX + PlazaRadiusX);
        int minY = Mathf.FloorToInt(PlazaCentreY - PlazaRadiusY);
        int maxY = Mathf.CeilToInt(PlazaCentreY + PlazaRadiusY);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                if (!IsInsidePlaza(x, y) || IsRaggedEdge(x, y))
                {
                    continue;
                }

                Vector2Int cell = new Vector2Int(x, y);

                if (ground.HasTile((Vector3Int)cell))
                {
                    footprint.Add(cell);
                }
            }
        }

        for (int x = StreetMinX; x <= StreetMaxX; x++)
        {
            for (int y = StreetMinY; y <= StreetMaxY; y++)
            {
                Vector2Int cell = new Vector2Int(x, y);

                if (ground.HasTile((Vector3Int)cell))
                {
                    footprint.Add(cell);
                }
            }
        }

        return footprint;
    }

    /// <summary>
    /// Rounded-rectangle test. The exponent controls corner shape: 2 gives
    /// an ellipse (which pinches the stall row), higher values approach a
    /// rectangle (which reads as a stamped slab). Three splits the
    /// difference — full-width edges with soft corners.
    /// </summary>
    private static bool IsInsidePlaza(int x, int y)
    {
        float dx = Mathf.Abs((x - PlazaCentreX) / PlazaRadiusX);
        float dy = Mathf.Abs((y - PlazaCentreY) / PlazaRadiusY);

        return Mathf.Pow(dx, PlazaCorner) + Mathf.Pow(dy, PlazaCorner) <= 1f;
    }

    /// <summary>
    /// Nibbles the outermost ring of the plaza so the paving meets the
    /// sand in a worn, irregular line instead of a machined curve.
    /// </summary>
    private static bool IsRaggedEdge(int x, int y)
    {
        bool onEdge =
            !IsInsidePlaza(x + 1, y) ||
            !IsInsidePlaza(x - 1, y) ||
            !IsInsidePlaza(x, y + 1) ||
            !IsInsidePlaza(x, y - 1);

        if (!onEdge)
        {
            return false;
        }

        return Mathf.Abs(Hash(x, y)) % 10 < 3;
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

    // ------------------------------------------------------------------
    // Clearing, without cutting anything in half
    // ------------------------------------------------------------------

    /// <summary>
    /// Removes the wilderness standing where the town goes — and removes it
    /// a whole object at a time.
    ///
    /// A palm is several cells: a trunk on the prop layer and a canopy two
    /// or three cells higher on the overhead layer. Clearing a rectangle
    /// therefore slices trees, leaving headless trunks inside the border
    /// and canopies floating over nothing outside it, which is exactly what
    /// made the first market look pasted onto the island. So the two layers
    /// are flood-filled together into connected clusters, and a cluster is
    /// either kept entirely or removed entirely.
    /// </summary>
    private static void ClearTownFootprint(
        Tilemap props,
        Tilemap overhead,
        Tilemap obstacle,
        HashSet<Vector2Int> footprint
    )
    {
        int removed = 0;

        foreach (HashSet<Vector2Int> cluster in FindClusters(props, overhead))
        {
            // The pier is the only way off the island. Never clear it, no
            // matter what the town plan overlaps.
            if (cluster.Any(cell => cell.x >= DockGuardX))
            {
                continue;
            }

            if (!cluster.Any(footprint.Contains))
            {
                continue;
            }

            foreach (Vector2Int cell in cluster)
            {
                Vector3Int position = (Vector3Int)cell;
                props.SetTile(position, null);
                overhead.SetTile(position, null);
                obstacle.SetTile(position, null);
            }

            removed++;
        }

        Debug.Log(
            $"[Shop Island] Cleared {removed} whole prop clusters for the " +
            "town; everything else was left standing."
        );
    }

    /// <summary>
    /// Flood-fills the prop and overhead layers together into connected
    /// clusters, using 8-connectivity so a canopy still counts as attached
    /// to a trunk that sits diagonally beneath it.
    /// </summary>
    private static List<HashSet<Vector2Int>> FindClusters(
        Tilemap props,
        Tilemap overhead
    )
    {
        HashSet<Vector2Int> occupied = new HashSet<Vector2Int>();

        foreach (Tilemap map in new[] { props, overhead })
        {
            foreach (Vector3Int cell in map.cellBounds.allPositionsWithin)
            {
                if (map.HasTile(cell))
                {
                    occupied.Add(new Vector2Int(cell.x, cell.y));
                }
            }
        }

        List<HashSet<Vector2Int>> clusters =
            new List<HashSet<Vector2Int>>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();

        foreach (Vector2Int start in occupied)
        {
            if (!visited.Add(start))
            {
                continue;
            }

            HashSet<Vector2Int> cluster =
                new HashSet<Vector2Int> { start };
            Queue<Vector2Int> pending = new Queue<Vector2Int>();
            pending.Enqueue(start);

            while (pending.Count > 0)
            {
                Vector2Int current = pending.Dequeue();

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0)
                        {
                            continue;
                        }

                        Vector2Int neighbour =
                            new Vector2Int(current.x + dx, current.y + dy);

                        if (!occupied.Contains(neighbour) ||
                            !visited.Add(neighbour))
                        {
                            continue;
                        }

                        cluster.Add(neighbour);
                        pending.Enqueue(neighbour);
                    }
                }
            }

            clusters.Add(cluster);
        }

        return clusters;
    }

    // ------------------------------------------------------------------
    // Paving and planting
    // ------------------------------------------------------------------

    /// <summary>
    /// Lays the market square. The paving goes onto the ground layer itself
    /// so it REPLACES the sand rather than sitting on a decal above it.
    ///
    /// One stone only. Mixing the sheet's warm cobble with its blue-grey
    /// brick looked like a way to break up a flat expanse, but the two are
    /// drawn in different styles and different palettes: scattered through
    /// each other they read as holes punched in the floor rather than as
    /// texture. Variety belongs in the props standing on the square, not in
    /// its surface.
    /// </summary>
    private static void PavePlaza(
        Tilemap ground,
        HashSet<Vector2Int> footprint
    )
    {
        Tile cobble = LoadShopTile("cobble_a");
        Tile worn = LoadShopTile("cobble_edge");

        foreach (Vector2Int cell in footprint)
        {
            // A rim of worn sandstone eases the cobble into the beach
            // instead of ending it against the sand like a cut-out.
            bool onRim =
                !footprint.Contains(new Vector2Int(cell.x + 1, cell.y)) ||
                !footprint.Contains(new Vector2Int(cell.x - 1, cell.y)) ||
                !footprint.Contains(new Vector2Int(cell.x, cell.y + 1)) ||
                !footprint.Contains(new Vector2Int(cell.x, cell.y - 1));

            ground.SetTile((Vector3Int)cell, onRim ? worn : cobble);
        }

        Debug.Log($"[Shop Island] Paved {footprint.Count} plaza cells.");
    }

    /// <summary>
    /// Copies one of the island's own palm groves to a few new anchors.
    ///
    /// Clearing the town site necessarily takes out the groves that stood
    /// there, and importing the openRPG temperate trees to replace them
    /// would put oaks on a tropical beach. Stamping a grove the artist
    /// already drew keeps the replanting indistinguishable from the
    /// original planting.
    /// </summary>
    private static void PlantPalmGroves(
        Tilemap props,
        Tilemap overhead,
        Tilemap ground
    )
    {
        // The northern grove: trunk cells on the prop layer, canopy on the
        // overhead layer, spanning cells (-4..-2, 8..11).
        BoundsInt source = new BoundsInt(-4, 8, 0, 3, 4, 1);

        List<(Vector2Int Offset, TileBase Prop, TileBase Overhead)> pattern =
            new List<(Vector2Int, TileBase, TileBase)>();

        foreach (Vector3Int cell in source.allPositionsWithin)
        {
            TileBase propTile = props.GetTile(cell);
            TileBase overheadTile = overhead.GetTile(cell);

            if (propTile == null && overheadTile == null)
            {
                continue;
            }

            pattern.Add((
                new Vector2Int(cell.x - source.xMin, cell.y - source.yMin),
                propTile,
                overheadTile
            ));
        }

        if (pattern.Count == 0)
        {
            Debug.LogWarning(
                "[Shop Island] The donor palm grove is missing; skipping " +
                "replanting."
            );
            return;
        }

        int planted = 0;

        foreach (Vector2Int anchor in PalmStampAnchors)
        {
            // Only plant where every trunk cell is dry land and the whole
            // footprint is empty, so a grove never straddles the surf or
            // grows through something already standing.
            bool clear = pattern.All(part =>
            {
                Vector3Int target = new Vector3Int(
                    anchor.x + part.Offset.x,
                    anchor.y + part.Offset.y,
                    0
                );

                if (props.HasTile(target) || overhead.HasTile(target))
                {
                    return false;
                }

                return part.Prop == null || ground.HasTile(target);
            });

            if (!clear)
            {
                continue;
            }

            foreach ((Vector2Int offset, TileBase prop, TileBase canopy)
                in pattern)
            {
                Vector3Int target = new Vector3Int(
                    anchor.x + offset.x,
                    anchor.y + offset.y,
                    0
                );

                if (prop != null)
                {
                    props.SetTile(target, prop);
                }

                if (canopy != null)
                {
                    overhead.SetTile(target, canopy);
                }
            }

            planted++;
        }

        Debug.Log($"[Shop Island] Replanted {planted} palm groves.");
    }

    private static void PlantTownTiles(
        Tilemap props,
        Tilemap obstacle,
        TileBase solidTile
    )
    {
        foreach ((string name, int x, int y) in TownTiles)
        {
            props.SetTile(new Vector3Int(x, y, 0), LoadShopTile(name));
        }

        if (solidTile == null)
        {
            return;
        }

        foreach (Vector2Int cell in SolidTownCells)
        {
            obstacle.SetTile((Vector3Int)cell, solidTile);
        }
    }

    /// <summary>
    /// Sprinkles bushes and flowers over the beach outside the town. Only
    /// cells surrounded by land are eligible, so nothing sprouts on the
    /// shoreline where it would hang over the water.
    /// </summary>
    private static void ScatterUndergrowth(
        Tilemap ground,
        Tilemap props,
        Tilemap overhead,
        HashSet<Vector2Int> footprint
    )
    {
        string[] undergrowth = { "bush_a", "bush_b", "flowers", "stump" };
        int planted = 0;

        foreach (Vector3Int cell in ground.cellBounds.allPositionsWithin)
        {
            Vector2Int flat = new Vector2Int(cell.x, cell.y);

            if (!ground.HasTile(cell) ||
                footprint.Contains(flat) ||
                props.HasTile(cell) ||
                overhead.HasTile(cell))
            {
                continue;
            }

            bool inland =
                ground.HasTile(new Vector3Int(cell.x + 1, cell.y, 0)) &&
                ground.HasTile(new Vector3Int(cell.x - 1, cell.y, 0)) &&
                ground.HasTile(new Vector3Int(cell.x, cell.y + 1, 0)) &&
                ground.HasTile(new Vector3Int(cell.x, cell.y - 1, 0));

            if (!inland)
            {
                continue;
            }

            int roll = Mathf.Abs(Hash(cell.x * 7, cell.y * 13));

            if (roll % 100 >= 16)
            {
                continue;
            }

            string pick = undergrowth[(roll / 100) % undergrowth.Length];
            props.SetTile(cell, LoadShopTile(pick));
            planted++;
        }

        Debug.Log($"[Shop Island] Scattered {planted} pieces of undergrowth.");
    }

    /// <summary>
    /// Borrows whatever invisible tile the island already uses for its
    /// collision layer, rather than inventing one that might render.
    /// </summary>
    private static TileBase FindSolidTile(Tilemap obstacle)
    {
        foreach (Vector3Int cell in obstacle.cellBounds.allPositionsWithin)
        {
            TileBase tile = obstacle.GetTile(cell);

            if (tile != null)
            {
                return tile;
            }
        }

        return null;
    }

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
    /// Creates the paving tiles the plaza needs, cut from the openRPG
    /// exterior sheet that the island polish pass already slices.
    /// </summary>
    private static void CreateTownTiles()
    {
        // Only fully-enclosed fill cells. Their neighbours in this sheet
        // are autotile EDGE pieces with wall bars baked in, which scatter
        // stray dark bars across a plaza when used as fill.
        (string Name, int Column, int Row)[] paving =
        {
            ("cobble_a", 10, 10),
            ("cobble_edge", 4, 14),
        };

        foreach ((string name, int column, int row) in paving)
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

    // ------------------------------------------------------------------
    // Converting the island into a safe harbour
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
    // Market objects
    // ------------------------------------------------------------------

    private static void BuildDressing(Transform districtRoot, Tilemap ground)
    {
        GameObject dressingRoot = new GameObject("Dressing");
        dressingRoot.transform.SetParent(districtRoot, false);

        foreach ((string prop, float cellX, float cellY) in Dressing)
        {
            CreatePropObject(
                dressingRoot.transform,
                ground,
                prop,
                cellX,
                cellY
            );
        }
    }

    private static GameObject CreatePropObject(
        Transform parent,
        Tilemap ground,
        string propName,
        float cellX,
        float cellY
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

        Vector3 position = CellPoint(ground, cellX, cellY);
        prop.transform.position = position;

        SpriteRenderer renderer = prop.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = DepthOrder(position.y);

        return prop;
    }

    private static void BuildStalls(Transform districtRoot, Tilemap ground)
    {
        foreach (VendorSpec spec in Vendors)
        {
            CreatePropObject(
                districtRoot,
                ground,
                spec.StallProp,
                spec.CellX,
                StallRowY
            );
        }

        foreach (VendorSpec spec in Vendors)
        {
            GameObject vendorObject = new GameObject(spec.ObjectName);
            vendorObject.transform.SetParent(districtRoot, false);

            Vector3 position =
                CellPoint(ground, spec.CellX, VendorRowY);
            vendorObject.transform.position = position;

            vendorObject.AddComponent<NetworkObject>();

            BoxCollider2D trigger =
                vendorObject.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            // Offset upward: the transform sits at the trader's feet, so a
            // centred box would hang below the ground.
            trigger.offset = new Vector2(0f, 1f);
            trigger.size = new Vector2(2f, 2.2f);

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

            AddNpcVisual(
                vendorObject.transform,
                spec.MotwIndex,
                DepthOrder(position.y)
            );
        }
    }

    private static void BuildTownsfolk(Transform districtRoot, Tilemap ground)
    {
        foreach ((string name, int motw, float cellX, float cellY)
            in Townsfolk)
        {
            GameObject person = new GameObject(name);
            person.transform.SetParent(districtRoot, false);

            Vector3 position = CellPoint(ground, cellX, cellY);
            person.transform.position = position;

            AddNpcVisual(person.transform, motw, DepthOrder(position.y));
        }
    }

    private static void AddNpcVisual(
        Transform parent,
        int motwIndex,
        int sortingOrder
    )
    {
        GameObject visual = new GameObject("Visual");
        visual.transform.SetParent(parent, false);
        visual.transform.localScale =
            new Vector3(NpcScale, NpcScale, 1f);

        SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
        renderer.sprite = LoadMotwSprite(motwIndex);
        renderer.sortingOrder = sortingOrder;
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
    /// unit: loose change beside a 2.1 unit player.
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

    /// <summary>
    /// Chooses where the loose change lies. Picking cells from the paved
    /// footprint rather than hand-listing them means coins cannot drift
    /// under a stall or into the sea when the square is reshaped, and the
    /// spacing rule stops them clumping into one lucky handful.
    /// </summary>
    private static List<Vector2Int> ChooseCoinCells(
        HashSet<Vector2Int> footprint
    )
    {
        HashSet<Vector2Int> blocked = new HashSet<Vector2Int>();

        foreach ((string _, int x, int y) in TownTiles)
        {
            blocked.Add(new Vector2Int(x, y));
        }

        foreach (Vector2Int cell in SolidTownCells)
        {
            blocked.Add(cell);
        }

        List<Vector2Int> chosen = new List<Vector2Int>();

        // Below the stall row, so no coin ends up behind an awning.
        IEnumerable<Vector2Int> candidates = footprint
            .Where(cell => cell.y < StallRowY - 1)
            .Where(cell => !blocked.Contains(cell))
            .Where(cell => !SpawnClearance.Contains(cell))
            .OrderBy(cell => Mathf.Abs(Hash(cell.x, cell.y)));

        foreach (Vector2Int cell in candidates)
        {
            if (chosen.Count >= CoinCount)
            {
                break;
            }

            bool crowded = chosen.Any(taken =>
                Mathf.Abs(taken.x - cell.x) < 2 &&
                Mathf.Abs(taken.y - cell.y) < 2);

            if (!crowded)
            {
                chosen.Add(cell);
            }
        }

        return chosen;
    }

    private static void PlaceCoins(
        Transform districtRoot,
        Tilemap ground,
        HashSet<Vector2Int> footprint,
        GameObject coinPrefab
    )
    {
        GameObject coinRoot = new GameObject("Coins");
        coinRoot.transform.SetParent(districtRoot, false);

        List<Vector2Int> cells = ChooseCoinCells(footprint);

        for (int index = 0; index < cells.Count; index++)
        {
            GameObject coin = (GameObject)PrefabUtility.InstantiatePrefab(
                coinPrefab,
                coinRoot.transform
            );

            coin.name = $"Coin_{index:D2}";

            // Coins float in the middle of their cell rather than resting
            // on its lower edge like a prop.
            coin.transform.position = CellPoint(
                ground,
                cells[index].x,
                cells[index].y + 0.5f
            );
        }

        Debug.Log($"[Shop Island] Scattered {cells.Count} coins.");
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
                $"{name} was not found in {scene.name}."
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
