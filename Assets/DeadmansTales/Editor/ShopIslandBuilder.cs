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

    // Wide enough to carry the three stalls (which span x -5..5) and no
    // wider. At 6 x 3.6 the paving covered a quarter of a 275-cell island,
    // and once it was laid in a single stone that quarter read as one grey
    // slab -- the flat-rectangle problem the ragged edge was meant to
    // solve, back at a larger size. Sand between the square and the shore
    // is what makes it a square rather than a floor.
    private const float PlazaRadiusX = 5f;
    private const float PlazaRadiusY = 3.2f;
    private const float PlazaCorner = 3f;

    /// <summary>
    /// The row the pier is built along, inside the bay bitten out of the
    /// island's south-east corner.
    /// </summary>
    private const int HarbourRowY = 0;

    /// <summary>Where the harbour road turns north for the market square.</summary>
    private const int RoadJunctionX = 4;

    /// <summary>
    /// How many cells of the pier are built over dry land, so it joins the
    /// beach instead of starting at the waterline.
    /// </summary>
    private const int DockShoreOverlap = 2;

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

    /// <summary>Height of a stall's counter, in cells.</summary>
    private const float CounterHeight = 1f;

    /// <summary>
    /// Where a trader stands: just behind their counter.
    ///
    /// A single stall sprite could never allow this. It can only be wholly
    /// in front of a character or wholly behind one, and neither reads as
    /// working a stall — in front hid the trader, behind hid them under
    /// their own canopy. The stall is now two sprites and the trader is
    /// sandwiched: counter in front of them, awning and posts behind.
    ///
    /// 0.3 of a cell back puts their feet inside the counter's footprint so
    /// it covers their legs, while their head reaches 0.3 + 2.12 = 2.42 —
    /// below the 2.56 where the lengthened posts hand over to the canopy.
    /// </summary>
    private const float VendorRowY = StallRowY + 0.3f;

    private static readonly VendorSpec[] Vendors =
    {
        // Rusty was a suit of polished plate armour, which reads as a knight
        // standing at a stall rather than as the person who made the blade
        // you are buying. This one is in a work shirt and leather vest.
        new VendorSpec(
            "Vendor_Blacksmith",
            "Rusty the Smith",
            ShopStock.WeaponTier,
            49,
            25,
            15,
            0,
            -4,
            "stall_red"
        ),
        // Vex was motw_10 — the sprite the PLAYER renders. Two of the same
        // character, one shopping from the other. A quartermaster is a
        // ship's officer, so this one wears an officer's colours.
        new VendorSpec(
            "Vendor_Quartermaster",
            "Quartermaster Vex",
            ShopStock.Upgrade,
            4,
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

    // Idle townsfolk were cut deliberately. They read as interactable — a
    // player walks up, presses E, and nothing happens — so four of them
    // standing around a small square cost more in confusion and clutter
    // than they bought in atmosphere. Everyone left on this island now
    // does something.

    /// <summary>
    /// Scenery that sells the market: a working forge in the smith's yard,
    /// a drying rack by the cook, and goods stacked as if just unloaded.
    /// Positions are the cell each prop stands in; props are centred on
    /// that cell with their base on its lower edge.
    /// </summary>
    /// <summary>
    /// Market scenery. SolidWidth is how many cells of the base row block
    /// movement; 0 leaves the prop walkable.
    /// </summary>
    private static readonly
        (string Prop, float CellX, float CellY, int SolidWidth)[] Dressing =
        {
            // Trade tools, in the yards either end of the stall row.
            ("forge_stone", -7.5f, 6f, 2),
            ("meatrack", 6.5f, 5f, 3),

            // Goods stacked in the two gaps between the three stalls,
            // which fall on cells -2 and 2.
            ("crate", -2.2f, 6.2f, 1),
            ("barrel", -1.8f, 5.4f, 1),
            ("pot", 2.2f, 6.2f, 1),
            ("barrel", -6.4f, 5.2f, 1),

            // The square used to have a facing row of counters across its
            // south side, meant to read as a street. Nobody stood behind
            // them and nothing could be bought at them, so they read as
            // two more shops that were broken. An empty square is better
            // than furniture that promises an interaction it does not have.

            // One signpost where the road leaves for the pier. Two read as
            // a pair of odd poles rather than as wayfinding.
            ("market_sign", 5.5f, 3.2f, 1),
        };

    /// <summary>
    /// Tile props stamped onto the prop layer. The well is the square's
    /// centrepiece; torches mark its corners after dark.
    /// </summary>
    /// <summary>
    /// Props stamped onto the prop tilemap, grouped into the yards they
    /// belong to.
    ///
    /// These used to be listed as a flat scatter, and the scatter stacked
    /// eight objects into the two cells east of the square — a woodpile, a
    /// jug, a barrel pair, a planter, a torch, a signpost, the drying rack
    /// and a crate, all within a couple of cells of each other. Read as a
    /// junk heap rather than as a trader's yard. Each group below now sits
    /// beside the thing it serves, with open paving between them.
    ///
    /// Kept off the stall row and the counter line: the prop tilemap draws
    /// at order 2, below every market sprite, so anything under a stall is
    /// simply painted over. Also kept off row 3, where the players land.
    /// </summary>
    private static readonly (string Tile, int CellX, int CellY)[] TownTiles =
    {
        // 2x2 well, west of the spawn point so nobody lands inside it.
        ("bigwell_bl", -5, 2),
        ("bigwell_br", -4, 2),
        ("bigwell_tl", -5, 3),
        ("bigwell_tr", -4, 3),

        // A lamp at each end of the square's open side. The pair that stood
        // at (-4, 7) and (4, 7) were directly behind the stall counters,
        // where the stalls hid them completely.
        ("torch", -6, 4),
        ("torch", 6, 4),

        // The smith's yard: fuel stacked in front of his forge.
        ("logpile", -9, 5),
        ("barrel_open", -8, 5),

        // The cook's yard: water and stores beside her drying rack.
        ("barrels_double", 7, 3),
        ("jug", 7, 4),

        // Two planters, so the square is not stone from wall to wall.
        ("pot_flower", -6, 2),
        ("pot_flower", 6, 2),
    };

    /// <summary>
    /// A fisherman's camp for the western beach: a tent, a fire, a logpile
    /// and a barrel.
    ///
    /// The west half of the island was open sand you only crossed to get
    /// somewhere else — dead space, in mapping terms. A second inhabited
    /// spot gives that half a reason to exist and an anchor for the eye,
    /// and a dirt trail from it to the market turns crossing the island
    /// into following a road rather than wandering.
    /// </summary>
    private static readonly (string Tile, int DX, int DY)[] FisherCamp =
    {
        ("tentw_bl", 0, 0),
        ("tentw_br", 1, 0),
        ("tentw_ml", 0, 1),
        ("tentw_mr", 1, 1),
        ("tentw_tl", 0, 2),
        ("tentw_tr", 1, 2),
        ("campfire", 3, 0),
        ("logpile", 4, 1),
        ("barrel_open", 3, 2),
    };

    /// <summary>Where to try putting the camp, best spot first.</summary>
    private static readonly Vector2Int[] CampAnchors =
    {
        new Vector2Int(-11, 2),
        new Vector2Int(-11, 4),
        new Vector2Int(-10, 1),
        new Vector2Int(-12, 3),
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
    /// not dead content before players have plundered anywhere. Kept small:
    /// these are a starter float, and a square littered with floating gold
    /// looks like debris rather than treasure. The real income is meant to
    /// come from the hostile island.
    /// </summary>
    private const int CoinCount = 5;

    /// <summary>
    /// Cells the four players drop into. Coins auto-collect on touch, so
    /// anything left here would be vacuumed up before anyone had moved.
    /// </summary>
    private static readonly RectInt SpawnClearance =
        new RectInt(-3, 2, 6, 3);

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

        // Salt Harbour is generated, not copied. The coastline vocabulary is
        // read back out of the team's two hand-authored islands so a new
        // silhouette is drawn with the pieces the artist already used.
        IslandTerrainBuilder.TerrainVocabulary vocabulary =
            IslandTerrainBuilder.Learn();
        HashSet<Vector2Int> land = IslandTerrainBuilder.BuildShape(vocabulary);
        IslandTerrainBuilder.Paint(scene, land, vocabulary);

        StripHostileContent(scene);
        ClearPreviousDistrict(scene);
        CreateTownTiles();

        Tilemap ground = FindTilemap(scene, "Tilemap_Ground");
        Tilemap props = FindTilemap(scene, "Tilemap_Props");
        Tilemap overhead = FindTilemap(scene, "Tilemap_Overhead");
        Tilemap obstacle = FindTilemap(scene, "Tilemap_ObstacleCollision");

        HashSet<Vector2Int> footprint = BuildFootprint(ground);

        // The pier goes wherever the bay's western shore actually ended up
        // after erosion, not at a hardcoded cell.
        Vector2Int dockAnchor = FindHarbourAnchor(land);
        Vector2Int dockEnd = IslandTerrainBuilder.PlaceDock(
            scene,
            land,
            vocabulary,
            dockAnchor
        );
        MoorRowboat(scene, ground, dockEnd);
        PaveHarbourRoad(ground, land, footprint, dockAnchor);

        // After the road, not before: the road joins the square along its
        // south-east rim, and the cell where the two meet is only walled in
        // once both exist.
        FillPavingPits(ground, footprint);

        PavePlaza(ground, footprint);

        // Deliberate landmarks are placed before filler. Palm groves check
        // that their footprint is clear before planting, so putting them
        // last means they flow around the camp instead of dropping a canopy
        // across its tent — which is exactly what happened when the groves
        // went in first.
        PlantTownTiles(
            ground,
            props,
            obstacle,
            vocabulary.ObstacleCollision
        );

        BuildOutpost(
            props,
            overhead,
            obstacle,
            vocabulary.ObstacleCollision,
            land,
            footprint
        );

        int groves = IslandTerrainBuilder.PlantGroves(
            scene,
            land,
            vocabulary,
            GroveAnchors(land, footprint),
            GroveLimit
        );
        Debug.Log($"[Shop Island] Planted {groves} palm groves.");

        ScatterUndergrowth(ground, props, overhead, footprint);

        Transform districtRoot = new GameObject(DistrictRootName).transform;

        BuildDressing(
            districtRoot,
            ground,
            obstacle,
            vocabulary.ObstacleCollision
        );
        BuildStalls(
            districtRoot,
            ground,
            obstacle,
            vocabulary.ObstacleCollision
        );
        PlaceCoins(districtRoot, ground, footprint, coinPrefab);
        EnsureShopHud(scene);
        PointExitAtBoat(scene);
        RebuildCollisionGeometry(scene);

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
            $"[Shop Island] Built {Vendors.Length} staffed stalls; " +
            "hostile spawns removed."
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
    /// Finds where to build the pier: the eastmost land on the harbour row,
    /// which is the western shore of the bay. Read from the finished shape
    /// rather than hardcoded, because erosion decides the exact coastline.
    /// </summary>
    private static Vector2Int FindHarbourAnchor(HashSet<Vector2Int> land)
    {
        int shoreX = int.MinValue;

        foreach (Vector2Int cell in land)
        {
            if (cell.y == HarbourRowY && cell.x > shoreX)
            {
                shoreX = cell.x;
            }
        }

        if (shoreX == int.MinValue)
        {
            throw new InvalidOperationException(
                "[Shop Island] No land on the harbour row; the island shape " +
                "does not reach the bay."
            );
        }

        // The pier is pulled back onto the beach rather than started at the
        // first water cell. Beginning it exactly where the sand stops leaves
        // a visible seam — planks that meet the shoreline edge-on and look
        // like they are floating alongside the island instead of being
        // built onto it. Real piers are anchored on land.
        return new Vector2Int(
            shoreX + 1 - DockShoreOverlap,
            HarbourRowY - 1
        );
    }

    /// <summary>
    /// Moves the inherited rowboat to the seaward end of the new pier. It
    /// is the island's only exit, so leaving it at the lobby's coordinates
    /// would strand it in open water far from the dock.
    /// </summary>
    private static void MoorRowboat(
        Scene scene,
        Tilemap ground,
        Vector2Int dockEnd
    )
    {
        LobbyRowboatInteraction rowboat = scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<LobbyRowboatInteraction>(true))
            .FirstOrDefault();

        if (rowboat == null)
        {
            Debug.LogWarning(
                "[Shop Island] No rowboat to moor; players could not leave."
            );
            return;
        }

        // The interaction sits on a child of the rowboat root, so the root
        // is what has to move.
        Transform root = rowboat.transform;

        while (root.parent != null)
        {
            root = root.parent;
        }

        root.position = CellPoint(ground, dockEnd.x + 0.3f, dockEnd.y);

        Debug.Log(
            $"[Shop Island] Moored the rowboat at the pier end, cell " +
            $"{dockEnd}."
        );
    }

    /// <summary>
    /// Cobbles a road from the market square down to the pier, so the
    /// harbour reads as connected to the town rather than as a pier that
    /// happens to be nearby. Only ever paints on land.
    /// </summary>
    private static void PaveHarbourRoad(
        Tilemap ground,
        HashSet<Vector2Int> land,
        HashSet<Vector2Int> footprint,
        Vector2Int dockAnchor
    )
    {
        Tile cobble = LoadShopTile("cobble_a");
        int roadY = dockAnchor.y + 1;

        // Along the shore to the pier. Inclusive of the pier's own landward
        // cell, or the cobble stops short and the road reads as a stub that
        // gives up before reaching the water.
        for (int x = RoadJunctionX; x <= dockAnchor.x; x++)
        {
            AddRoadCell(ground, land, footprint, cobble, x, roadY);
            AddRoadCell(ground, land, footprint, cobble, x, roadY + 1);
        }

        // Up from the shore into the square.
        for (int y = roadY; y <= PlazaCentreY; y++)
        {
            AddRoadCell(ground, land, footprint, cobble, RoadJunctionX, y);
            AddRoadCell(ground, land, footprint, cobble, RoadJunctionX + 1, y);
        }
    }

    private static void AddRoadCell(
        Tilemap ground,
        HashSet<Vector2Int> land,
        HashSet<Vector2Int> footprint,
        Tile cobble,
        int x,
        int y
    )
    {
        Vector2Int cell = new Vector2Int(x, y);

        if (!land.Contains(cell) || footprint.Contains(cell))
        {
            return;
        }

        ground.SetTile((Vector3Int)cell, cobble);
        footprint.Add(cell);
    }

    /// <summary>
    /// Where to try planting palm groves: spread around the island, well
    /// clear of the town. Anchors that do not fit are skipped by the
    /// planter, so this is a wish list rather than a guarantee.
    /// </summary>
    private static IEnumerable<Vector2Int> GroveAnchors(
        HashSet<Vector2Int> land,
        HashSet<Vector2Int> footprint
    )
    {
        List<Vector2Int> candidates = new List<Vector2Int>();

        // Every cell outside the town is a candidate. A palm needs a 3x4
        // clear footprint on dry land, so most are rejected by the planter;
        // a coarse lattice offered so few that the island came out bare.
        for (int x = -16; x <= 12; x++)
        {
            for (int y = -5; y <= 12; y++)
            {
                Vector2Int anchor = new Vector2Int(x, y);

                if (!land.Contains(anchor))
                {
                    continue;
                }

                bool nearTown = footprint.Any(cell =>
                    Mathf.Abs(cell.x - anchor.x) <= 2 &&
                    Mathf.Abs(cell.y - anchor.y) <= 2);

                if (nearTown)
                {
                    continue;
                }

                candidates.Add(anchor);
            }
        }

        // Offering them in reading order packs the groves into whichever
        // corner comes first, because the planter takes the first that fit,
        // so the list is shuffled deterministically: the same island shape
        // gives the same planting every run.
        //
        // Thinning this list to anchors a grove's width apart was tried and
        // is wrong — it reserves ground for candidates that then fail to
        // fit, and cost six of the seven groves. Spacing is the planter's
        // job, because only the planter knows which anchors were taken.
        return candidates
            .OrderBy(cell => Mathf.Abs(Hash(cell.x, cell.y)))
            .ToList();
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

        // The well is the square's centrepiece, so the ragged rim must not
        // nibble the paving out from under it. It used to stand with two
        // feet on cobble and two on sand, straddling the boundary as though
        // it had been dropped there.
        foreach (Vector2Int cell in SolidTownCells)
        {
            if (ground.HasTile((Vector3Int)cell))
            {
                footprint.Add(cell);
            }
        }

        return footprint;
    }

    /// <summary>
    /// Paves over single cells the ragged edge left unpaved inside the
    /// square.
    ///
    /// <see cref="IsRaggedEdge"/> only ever nibbles cells on the ideal
    /// outline, which sounds safe — but the outline of a rounded rectangle
    /// is a staircase, so a cell can sit on it and still end up with paving
    /// on three sides. Those came out as isolated pits of sand in the middle
    /// of the cobble, one of them right under the red stall's counter. They
    /// read as damage rather than as wear. A cell walled in by paving is
    /// part of the square whatever the outline says.
    /// </summary>
    private static void FillPavingPits(
        Tilemap ground,
        HashSet<Vector2Int> footprint
    )
    {
        Vector2Int[] sides =
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1),
        };

        List<Vector2Int> pits = new List<Vector2Int>();

        foreach (Vector2Int cell in footprint)
        {
            foreach (Vector2Int side in sides)
            {
                Vector2Int gap = cell + side;

                if (footprint.Contains(gap) ||
                    !ground.HasTile((Vector3Int)gap))
                {
                    continue;
                }

                int walled = sides.Count(step =>
                    footprint.Contains(gap + step));

                if (walled >= 3)
                {
                    pits.Add(gap);
                }
            }
        }

        footprint.UnionWith(pits);
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

    // ------------------------------------------------------------------
    // Paving and planting
    // ------------------------------------------------------------------

    /// <summary>
    /// Lays the market square. The paving goes onto the ground layer itself
    /// so it REPLACES the sand rather than sitting on a decal above it.
    ///
    /// One stone only, and that now includes the edge.
    ///
    /// Mixing the sheet's warm cobble with its blue-grey brick was the first
    /// attempt at breaking up a flat expanse; the two are drawn in different
    /// styles and palettes, so scattered through each other they read as
    /// holes punched in the floor. The second attempt was a rim of worn
    /// sandstone to ease the cobble into the beach — but the rim is computed
    /// from the RAGGED footprint, so every cell the ragged edge nibbled
    /// turned its neighbours into rim too. Half the square ended up worn:
    /// a flat yellow band across the whole south side and stray patches
    /// inside the paving. An irregular outline in one material reads as a
    /// worn square; two materials read as a mistake.
    /// </summary>
    private static void PavePlaza(
        Tilemap ground,
        HashSet<Vector2Int> footprint
    )
    {
        Tile cobble = LoadShopTile("cobble_a");

        foreach (Vector2Int cell in footprint)
        {
            ground.SetTile((Vector3Int)cell, cobble);
        }

        Debug.Log($"[Shop Island] Paved {footprint.Count} plaza cells.");
    }

    /// <summary>
    /// Pitches the fisherman's camp at the first anchor where every cell is
    /// dry land and nothing already stands. Returns the anchor used, or null
    /// if the coast this build produced has no room for it.
    /// </summary>
    private static Vector2Int? BuildOutpost(
        Tilemap props,
        Tilemap overhead,
        Tilemap obstacle,
        TileBase solidTile,
        HashSet<Vector2Int> land,
        HashSet<Vector2Int> footprint
    )
    {
        foreach (Vector2Int anchor in CampAnchors)
        {
            bool fits = FisherCamp.All(part =>
            {
                Vector2Int cell =
                    new Vector2Int(anchor.x + part.DX, anchor.y + part.DY);

                // The overhead layer matters as much as the prop layer: it
                // carries tree canopies, which draw over everything.
                return land.Contains(cell) &&
                    !footprint.Contains(cell) &&
                    !props.HasTile((Vector3Int)cell) &&
                    !overhead.HasTile((Vector3Int)cell);
            });

            if (!fits)
            {
                continue;
            }

            foreach ((string tile, int dx, int dy) in FisherCamp)
            {
                Vector3Int cell =
                    new Vector3Int(anchor.x + dx, anchor.y + dy, 0);

                props.SetTile(cell, LoadShopTile(tile));

                // Every piece of the camp is something you walk around: a
                // tent, a fire, a woodpile, a barrel.
                if (solidTile != null)
                {
                    obstacle.SetTile(cell, solidTile);
                }
            }

            Debug.Log($"[Shop Island] Pitched the fisher camp at {anchor}.");
            return anchor;
        }

        Debug.LogWarning(
            "[Shop Island] No room for the fisher camp on this coastline."
        );
        return null;
    }

    // There used to be a dirt trail beaten from the fisher camp to the
    // market square. It never worked and could not have: the camp sits at
    // x -11 and the square's paving already starts at x -6, so the only
    // ground the trail could cover was the three columns between them —
    // and it laid two cells per column. That is not a path, it is a patch,
    // and in solid mid-brown against pale beach sand it read as mud spilled
    // beside the tent. Two places five paces apart do not need a road.

    private static void PlantTownTiles(
        Tilemap ground,
        Tilemap props,
        Tilemap obstacle,
        TileBase solidTile
    )
    {
        int placed = 0;

        foreach ((string name, int x, int y) in TownTiles)
        {
            Vector3Int cell = new Vector3Int(x, y, 0);

            // A wish list, not a command. The coastline is generated, so a
            // yard prop authored a couple of cells outside the square is
            // only PROBABLY on land; stamping it blindly would float it on
            // the sea, and stamping it over an existing prop would also
            // push the fisher camp off its anchor, since the camp needs its
            // whole footprint clear.
            if (!ground.HasTile(cell) || props.HasTile(cell))
            {
                continue;
            }

            props.SetTile(cell, LoadShopTile(name));
            placed++;
        }

        Debug.Log(
            $"[Shop Island] Placed {placed} of {TownTiles.Length} town props."
        );

        if (solidTile == null)
        {
            return;
        }

        foreach (Vector2Int cell in SolidTownCells)
        {
            if (props.HasTile((Vector3Int)cell))
            {
                obstacle.SetTile((Vector3Int)cell, solidTile);
            }
        }
    }

    /// <summary>
    /// Sprinkles greenery over the beach outside the town. Only cells
    /// surrounded by land are eligible, so nothing sprouts on the shoreline
    /// where it would hang over the water.
    ///
    /// Density is deliberately low. At 16% the beach filled with lone
    /// bushes, stumps and flower tufts spaced one cell apart, which read as
    /// static rather than as planting — the island looked busier but not
    /// fuller. Sparse clumps let the palm groves and the market be the
    /// things the eye lands on.
    /// </summary>
    private const int UndergrowthPercent = 11;

    /// <summary>
    /// How many palm groves to plant. Enough to frame the island without
    /// walling the market off from the shore.
    /// </summary>
    private const int GroveLimit = 7;

    private static void ScatterUndergrowth(
        Tilemap ground,
        Tilemap props,
        Tilemap overhead,
        HashSet<Vector2Int> footprint
    )
    {
        // Stumps read as felled trees and drew the eye as though they
        // mattered; the scatter is greenery only.
        string[] undergrowth = { "bush_a", "bush_b", "flowers" };
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

            if (roll % 100 >= UndergrowthPercent)
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
    /// Stamps blocking cells under a prop, so players walk up to the market
    /// furniture instead of straight through it.
    ///
    /// Only the base row is marked. These are drawn in a 2.5D projection: a
    /// stall's awning hangs over ground you should still be able to walk
    /// past, and blocking the whole sprite would wall off the square.
    /// </summary>
    private static void MarkSolid(
        Tilemap obstacle,
        TileBase tile,
        float centreCellX,
        float baseCellY,
        int widthCells
    )
    {
        if (tile == null || widthCells <= 0)
        {
            return;
        }

        int centre = Mathf.RoundToInt(centreCellX);
        int row = Mathf.RoundToInt(baseCellY);
        int start = centre - (widthCells - 1) / 2;

        for (int offset = 0; offset < widthCells; offset++)
        {
            obstacle.SetTile(
                new Vector3Int(start + offset, row, 0),
                tile
            );
        }
    }

    /// <summary>
    /// Rebuilds the collision outlines for the two blocking tilemaps.
    ///
    /// A CompositeCollider2D SERIALIZES its geometry into the scene, and this
    /// scene was born as a copy of the lobby island — so it shipped with the
    /// lobby's outlines baked in while carrying Salt Harbour's tiles. The
    /// saved shapes described land that is not here: a block at cells
    /// (-3, 8..9) with no tile under it, nothing at all around the well.
    /// Whatever the editor reconstructs on load, a scene whose stored
    /// collision does not match its own tiles is a scene nobody can reason
    /// about, and it is the only explanation left for solid props you can
    /// walk through. Generating it here makes what is saved what is meant.
    /// </summary>
    private static void RebuildCollisionGeometry(Scene scene)
    {
        string[] blockingMaps =
        {
            "Tilemap_ObstacleCollision",
            "Tilemap_WaterCollision",
        };

        foreach (string name in blockingMaps)
        {
            Tilemap map = FindTilemap(scene, name);

            TilemapCollider2D tileCollider =
                map.GetComponent<TilemapCollider2D>();

            if (tileCollider == null)
            {
                Debug.LogWarning(
                    $"[Shop Island] {name} has no TilemapCollider2D; " +
                    "nothing on it will block movement."
                );
                continue;
            }

            CompositeCollider2D composite =
                map.GetComponent<CompositeCollider2D>();

            if (composite == null)
            {
                continue;
            }

            // A TilemapCollider2D batches tile edits and rebuilds its shapes
            // on the next physics step, which a batch-mode build never takes.
            // Asking the composite to regenerate before that happens merges
            // whatever it already had -- which is how the stale outlines
            // survived being "regenerated" the first time. Flush the pending
            // tile changes first, then merge.
            tileCollider.ProcessTilemapChanges();
            Physics2D.SyncTransforms();
            composite.GenerateGeometry();

            Debug.Log(
                $"[Shop Island] {name}: {map.GetUsedTilesCount()} blocking " +
                $"tiles merged into {composite.pathCount} outline(s)."
            );
        }
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

    private static void BuildDressing(
        Transform districtRoot,
        Tilemap ground,
        Tilemap obstacle,
        TileBase solidTile
    )
    {
        GameObject dressingRoot = new GameObject("Dressing");
        dressingRoot.transform.SetParent(districtRoot, false);

        foreach ((string prop, float cellX, float cellY, int solidWidth)
            in Dressing)
        {
            CreatePropObject(
                dressingRoot.transform,
                ground,
                prop,
                cellX,
                cellY
            );

            MarkSolid(obstacle, solidTile, cellX, cellY, solidWidth);
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

    private static void BuildStalls(
        Transform districtRoot,
        Tilemap ground,
        Tilemap obstacle,
        TileBase solidTile
    )
    {
        foreach (VendorSpec spec in Vendors)
        {
            // The awning and posts stand a counter's height further back,
            // BEHIND the trader.
            GameObject back = CreatePropObject(
                districtRoot,
                ground,
                spec.StallProp + "_back",
                spec.CellX,
                StallRowY + CounterHeight
            );

            int stallOrder = back
                .GetComponent<SpriteRenderer>()
                .sortingOrder;

            // The counter draws IN FRONT of the trader and hides their legs,
            // which is what actually sells "standing behind the stall".
            GameObject front = CreatePropObject(
                districtRoot,
                ground,
                MarketArtBuilder.StallFrontProp,
                spec.CellX,
                StallRowY
            );

            front.name = spec.StallProp + "_counter";

            front.GetComponent<SpriteRenderer>().sortingOrder =
                Mathf.Min(stallOrder + 2, 19);

            // A counter is solid: you walk up to it, not through it.
            MarkSolid(obstacle, solidTile, spec.CellX, StallRowY, 3);

            GameObject vendorObject = new GameObject(spec.ObjectName);
            vendorObject.transform.SetParent(districtRoot, false);

            Vector3 position =
                CellPoint(ground, spec.CellX, VendorRowY);
            vendorObject.transform.position = position;

            vendorObject.AddComponent<NetworkObject>();

            BoxCollider2D trigger =
                vendorObject.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            // Only the strip of pavement directly in front of the counter.
            //
            // This used to be 3 x 3.4 centred on the trader, which reached
            // most of the way across the square: the shop panel opened while
            // you were still walking towards the stall, and with three
            // stalls four cells apart their zones very nearly touched. The
            // box now sits south of the trader and covers roughly one
            // player's depth of standing room, which is the only place a
            // shopper can actually be — the counter blocks the row behind.
            trigger.offset = new Vector2(0f, -0.85f);
            trigger.size = new Vector2(2.2f, 1.2f);

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

            // No slack on top of the player's own two-unit reach. The extra
            // 1.5 units here was the other half of the too-eager prompt:
            // the range test measures to the TRADER, who stands a counter's
            // depth back, so every unit of slack showed the panel a unit
            // earlier than it looks like it should.
            SetSerializedFloat(vendor, "additionalServerRange", 0f);

            // One order above their own stall. Depth-sorting alone would
            // put the trader behind it — they stand further north — and the
            // canopy is solid, so they would vanish.
            AddNpcVisual(
                vendorObject.transform,
                spec.MotwIndex,
                stallOrder + 1
            );
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

    /// <summary>
    /// Adds the purse readout and the stall counter window. Both are plain
    /// OnGUI components with no wiring, which is what lets this builder
    /// author the whole shop without a canvas prefab.
    /// </summary>
    private static void EnsureShopHud(Scene scene)
    {
        bool hasPurse = scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<CoinPurseHUD>(true))
            .Any();

        if (!hasPurse)
        {
            new GameObject("CoinPurseHUD").AddComponent<CoinPurseHUD>();
        }

        bool hasShopScreen = scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<ShopScreenHUD>(true))
            .Any();

        if (!hasShopScreen)
        {
            new GameObject("ShopScreenHUD").AddComponent<ShopScreenHUD>();
        }
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
