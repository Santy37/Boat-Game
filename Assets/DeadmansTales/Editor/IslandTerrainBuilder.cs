using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Paints an island of an arbitrary shape using the shore vocabulary the
/// team's two hand-authored islands already contain.
///
/// WHY LEARN RATHER THAN AUTOTILE FROM THE SHEET. The beach sheets are
/// RPG-Maker autotile templates, and composing 32px tiles from 16px quarters
/// is the textbook way to do this — but the two existing islands were not
/// built that way. They were laid out from whole sliced tiles, and every
/// shoreline the team has ever approved is expressed in those tiles. Reading
/// the vocabulary back out of them means a new island is made of exactly the
/// pieces the artist already used, in exactly the arrangements they already
/// used, so it cannot end up subtly off-style from the rest of the game.
///
/// THE REPAIR STEP IS WHAT MAKES IT SAFE. A learned vocabulary is not
/// complete: measured against a freshly invented shape it was missing 12
/// ground and 8 water configurations, and a missing configuration is a hole
/// in the coastline. So the shape is not treated as fixed. It is smoothed and
/// then eroded until every land cell AND every water cell touching land has a
/// configuration the vocabulary can express. The shape bends to the art
/// instead of the art being faked to fit the shape.
/// </summary>
public static class IslandTerrainBuilder
{
    private const string LobbyScenePath =
        "Assets/DeadmansTales/Scenes/Lobby_Island_2D.unity";
    private const string OceanScenePath =
        "Assets/DeadmansTales/Scenes/Island_After_Ocean_01_2D.unity";

    /// <summary>
    /// How far the painted ocean reaches. Matches what the islands already
    /// ship so the camera never reaches the edge of the water.
    /// </summary>
    private static readonly RectInt OceanBounds =
        new RectInt(-31, -20, 58, 42);

    /// <summary>
    /// Everything learned from the existing islands: which tile belongs in
    /// which neighbour configuration, plus the multi-cell set pieces worth
    /// reusing.
    /// </summary>
    public sealed class TerrainVocabulary
    {
        public Dictionary<int, TileBase> GroundBySignature =
            new Dictionary<int, TileBase>();

        /// <summary>
        /// Fill tiles for cells with land on all sides, weighted by how
        /// often the artist used each. The interior is decorated with 26
        /// different sand variants; picking uniformly would look flatter
        /// than the originals.
        /// </summary>
        public List<TileBase> GroundInterior = new List<TileBase>();

        public Dictionary<int, TileBase> WaterBySignature =
            new Dictionary<int, TileBase>();

        public List<TileBase> OpenOcean = new List<TileBase>();

        public TileBase WaterCollision;

        /// <summary>
        /// The invisible tile the islands use to block movement through
        /// solid props. Borrowed rather than invented, because a tile with
        /// a sprite on it would render over the whole scene.
        /// </summary>
        public TileBase ObstacleCollision;

        /// <summary>A palm grove: trunks on props, canopy on overhead.</summary>
        public List<(Vector2Int Offset, TileBase Prop, TileBase Overhead)>
            PalmGrove =
                new List<(Vector2Int, TileBase, TileBase)>();

        /// <summary>The pier, as prop tiles relative to its landward corner.</summary>
        public List<(Vector2Int Offset, TileBase Prop)> Dock =
            new List<(Vector2Int, TileBase)>();

        public Vector2Int DockSize;
    }

    private static readonly Vector2Int[] Neighbours =
    {
        new Vector2Int(-1, 1),
        new Vector2Int(0, 1),
        new Vector2Int(1, 1),
        new Vector2Int(-1, 0),
        new Vector2Int(1, 0),
        new Vector2Int(-1, -1),
        new Vector2Int(0, -1),
        new Vector2Int(1, -1),
    };

    /// <summary>
    /// The 8-neighbour land mask for a cell, in a fixed bit order. This is
    /// the key the vocabulary is indexed by.
    /// </summary>
    private static int Signature(HashSet<Vector2Int> land, Vector2Int cell)
    {
        int mask = 0;

        for (int index = 0; index < Neighbours.Length; index++)
        {
            if (land.Contains(cell + Neighbours[index]))
            {
                mask |= 1 << index;
            }
        }

        return mask;
    }

    // ------------------------------------------------------------------
    // Learning
    // ------------------------------------------------------------------

    public static TerrainVocabulary Learn()
    {
        TerrainVocabulary vocabulary = new TerrainVocabulary();

        Dictionary<int, Dictionary<TileBase, int>> groundVotes =
            new Dictionary<int, Dictionary<TileBase, int>>();
        Dictionary<int, Dictionary<TileBase, int>> waterVotes =
            new Dictionary<int, Dictionary<TileBase, int>>();

        foreach (string path in new[] { LobbyScenePath, OceanScenePath })
        {
            Scene donor = EditorSceneManager.OpenScene(
                path,
                OpenSceneMode.Additive
            );

            try
            {
                LearnFromScene(
                    donor,
                    vocabulary,
                    groundVotes,
                    waterVotes,
                    path == LobbyScenePath
                );
            }
            finally
            {
                EditorSceneManager.CloseScene(donor, true);
            }
        }

        vocabulary.GroundBySignature = WinnersOf(groundVotes);
        vocabulary.WaterBySignature = WinnersOf(waterVotes);

        Debug.Log(
            $"[Island Terrain] Learned {vocabulary.GroundBySignature.Count} " +
            $"ground and {vocabulary.WaterBySignature.Count} water shore " +
            $"configurations, {vocabulary.GroundInterior.Count} interior " +
            $"fills, {vocabulary.OpenOcean.Count} ocean fills, a " +
            $"{vocabulary.PalmGrove.Count}-tile palm grove and a " +
            $"{vocabulary.Dock.Count}-tile pier."
        );

        return vocabulary;
    }

    private static Dictionary<int, TileBase> WinnersOf(
        Dictionary<int, Dictionary<TileBase, int>> votes
    )
    {
        Dictionary<int, TileBase> winners = new Dictionary<int, TileBase>();

        foreach (KeyValuePair<int, Dictionary<TileBase, int>> entry in votes)
        {
            winners[entry.Key] = entry.Value
                .OrderByDescending(pair => pair.Value)
                .First()
                .Key;
        }

        return winners;
    }

    private static void LearnFromScene(
        Scene donor,
        TerrainVocabulary vocabulary,
        Dictionary<int, Dictionary<TileBase, int>> groundVotes,
        Dictionary<int, Dictionary<TileBase, int>> waterVotes,
        bool captureSetPieces
    )
    {
        Tilemap ground = FindTilemap(donor, "Tilemap_Ground");
        Tilemap water = FindTilemap(donor, "Tilemap_Water");
        Tilemap collision = FindTilemap(donor, "Tilemap_WaterCollision");

        HashSet<Vector2Int> land = CellsOf(ground);

        foreach (Vector2Int cell in land)
        {
            int signature = Signature(land, cell);
            TileBase tile = ground.GetTile((Vector3Int)cell);

            if (tile == null)
            {
                continue;
            }

            if (signature == 255)
            {
                vocabulary.GroundInterior.Add(tile);
                continue;
            }

            Vote(groundVotes, signature, tile);
        }

        foreach (Vector2Int cell in CellsOf(water))
        {
            if (land.Contains(cell))
            {
                continue;
            }

            TileBase tile = water.GetTile((Vector3Int)cell);

            if (tile == null)
            {
                continue;
            }

            int signature = Signature(land, cell);

            if (signature == 0)
            {
                vocabulary.OpenOcean.Add(tile);
                continue;
            }

            Vote(waterVotes, signature, tile);
        }

        if (vocabulary.WaterCollision == null)
        {
            vocabulary.WaterCollision = FirstTile(collision);
        }

        if (!captureSetPieces)
        {
            return;
        }

        CaptureSetPieces(donor, vocabulary);
    }

    /// <summary>
    /// Lifts the palm grove and the pier out of the lobby island so a new
    /// island can be planted and given a harbour without inventing art.
    /// </summary>
    private static void CaptureSetPieces(
        Scene donor,
        TerrainVocabulary vocabulary
    )
    {
        Tilemap props = FindTilemap(donor, "Tilemap_Props");
        Tilemap overhead = FindTilemap(donor, "Tilemap_Overhead");

        vocabulary.ObstacleCollision =
            FirstTile(FindTilemap(donor, "Tilemap_ObstacleCollision"));

        // The northern grove: trunks on the prop layer at (-3..-2, 8..9),
        // canopy on the overhead layer above it.
        BoundsInt grove = new BoundsInt(-4, 8, 0, 3, 4, 1);

        foreach (Vector3Int cell in grove.allPositionsWithin)
        {
            TileBase prop = props.GetTile(cell);
            TileBase canopy = overhead.GetTile(cell);

            if (prop == null && canopy == null)
            {
                continue;
            }

            vocabulary.PalmGrove.Add((
                new Vector2Int(cell.x - grove.xMin, cell.y - grove.yMin),
                prop,
                canopy
            ));
        }

        // The pier, which is the only way off an island.
        BoundsInt dock = new BoundsInt(7, 0, 0, 8, 3, 1);
        vocabulary.DockSize = new Vector2Int(dock.size.x, dock.size.y);

        foreach (Vector3Int cell in dock.allPositionsWithin)
        {
            TileBase prop = props.GetTile(cell);

            if (prop == null)
            {
                continue;
            }

            vocabulary.Dock.Add((
                new Vector2Int(cell.x - dock.xMin, cell.y - dock.yMin),
                prop
            ));
        }
    }

    private static void Vote(
        Dictionary<int, Dictionary<TileBase, int>> votes,
        int signature,
        TileBase tile
    )
    {
        if (!votes.TryGetValue(signature, out Dictionary<TileBase, int> bucket))
        {
            bucket = new Dictionary<TileBase, int>();
            votes[signature] = bucket;
        }

        bucket[tile] = bucket.TryGetValue(tile, out int count) ? count + 1 : 1;
    }

    // ------------------------------------------------------------------
    // Shape
    // ------------------------------------------------------------------

    /// <summary>
    /// Salt Harbour: a long east-west island with a sheltered bay bitten out
    /// of its south-east corner, so the pier sits inside a natural harbour
    /// rather than sticking off a round beach like the lobby island's.
    /// </summary>
    private static bool IsInsideBaseShape(int x, int y)
    {
        // Wobble keeps the coast from reading as a drawn ellipse. Every term
        // is periodic in cell coordinates, so the island is identical on
        // every rebuild without needing a stored seed.
        // Amplitude is capped on purpose. The vocabulary was learned from two
        // convex islands, so it has no tiles for tight concave coast; a
        // noisier field produces notches nothing can draw and erosion then
        // eats the island trying to fix them.
        float wobble =
            0.10f * Mathf.Sin(y * 0.75f) +
            0.08f * Mathf.Cos(x * 0.55f) +
            0.05f * Mathf.Sin((x + y) * 0.4f);

        // Main landmass: long east-west, unlike the lobby island's disc.
        // Centred on the town rather than on the origin, so the market sits
        // in the middle of the island instead of crowding one shore.
        float nx = x / 13f;
        float ny = (y - 3f) / 6.8f;
        bool inside = nx * nx + ny * ny <= 1f + wobble;

        // A headland off the north-west, so one end of the island has a
        // shoulder instead of tapering away symmetrically. It overlaps the
        // main body generously — a headland joined by a thin neck is the
        // concave case the vocabulary cannot draw.
        float hx = (x + 8f) / 4.5f;
        float hy = (y - 7.5f) / 3f;
        inside |= hx * hx + hy * hy <= 1f + wobble * 0.5f;

        if (!inside)
        {
            return false;
        }

        // The harbour: a cove bitten deep into the south-east so the pier
        // sits in sheltered water rather than off an open beach.
        //
        // Its centre is deliberately outside the landmass so the cove opens
        // to the sea. Placing it inside carves a lagoon and strands the land
        // beyond it as an islet, which then erodes and takes the coastline
        // with it.
        float bx = (x - 13.5f) / 6f;
        float by = (y + 1f) / 4f;

        return bx * bx + by * by > 1f;
    }

    /// <summary>
    /// Builds the land set, then makes it printable: smoothing removes the
    /// one-cell spits and diagonal-only joins that a noisy field produces,
    /// and erosion then removes anything the learned vocabulary still cannot
    /// draw. Both only ever take land away, so the loop terminates.
    /// </summary>
    public static HashSet<Vector2Int> BuildShape(TerrainVocabulary vocabulary)
    {
        HashSet<Vector2Int> land = new HashSet<Vector2Int>();

        for (int x = OceanBounds.xMin + 2; x < OceanBounds.xMax - 2; x++)
        {
            for (int y = OceanBounds.yMin + 2; y < OceanBounds.yMax - 2; y++)
            {
                if (IsInsideBaseShape(x, y))
                {
                    land.Add(new Vector2Int(x, y));
                }
            }
        }

        int generated = land.Count;

        for (int pass = 0; pass < 3; pass++)
        {
            land = Smooth(land);
        }

        land = LargestLandmass(land);

        int smoothed = land.Count;
        int eroded = Erode(land, vocabulary);
        land.IntersectWith(LargestLandmass(land));

        Debug.Log(
            $"[Island Terrain] Shape: {generated} cells generated, " +
            $"{smoothed} after smoothing, {land.Count} after {eroded} " +
            "erosion passes."
        );

        return land;
    }

    /// <summary>
    /// Keeps only the biggest connected landmass.
    ///
    /// Carving a cove can leave a scrap of land stranded beyond it. A
    /// stranded scrap is nearly all coastline, so erosion tends to eat it,
    /// and each pass exposes more pathological configurations on whatever
    /// it was attached to — a cascade that can consume the whole island.
    /// Dropping the strays up front stops that before it starts, and also
    /// guarantees players can walk everywhere they can see.
    /// </summary>
    private static HashSet<Vector2Int> LargestLandmass(
        HashSet<Vector2Int> land
    )
    {
        HashSet<Vector2Int> unvisited = new HashSet<Vector2Int>(land);
        HashSet<Vector2Int> best = new HashSet<Vector2Int>();

        while (unvisited.Count > 0)
        {
            Vector2Int seed = unvisited.First();
            HashSet<Vector2Int> component = new HashSet<Vector2Int> { seed };
            Queue<Vector2Int> pending = new Queue<Vector2Int>();

            unvisited.Remove(seed);
            pending.Enqueue(seed);

            while (pending.Count > 0)
            {
                Vector2Int current = pending.Dequeue();

                foreach (Vector2Int offset in Neighbours)
                {
                    Vector2Int neighbour = current + offset;

                    if (!unvisited.Remove(neighbour))
                    {
                        continue;
                    }

                    component.Add(neighbour);
                    pending.Enqueue(neighbour);
                }
            }

            if (component.Count > best.Count)
            {
                best = component;
            }
        }

        return best;
    }

    /// <summary>
    /// Majority smoothing. A cell survives when most of its 3x3
    /// neighbourhood is land, which rounds off spits and fills nicks.
    /// </summary>
    private static HashSet<Vector2Int> Smooth(HashSet<Vector2Int> land)
    {
        HashSet<Vector2Int> result = new HashSet<Vector2Int>();

        int minX = land.Min(cell => cell.x) - 1;
        int maxX = land.Max(cell => cell.x) + 1;
        int minY = land.Min(cell => cell.y) - 1;
        int maxY = land.Max(cell => cell.y) + 1;

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                int count = land.Contains(cell) ? 1 : 0;

                foreach (Vector2Int offset in Neighbours)
                {
                    if (land.Contains(cell + offset))
                    {
                        count++;
                    }
                }

                if (count >= 5)
                {
                    result.Add(cell);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Removes land until every land cell, and every water cell touching
    /// land, has a configuration the vocabulary can draw. Returns the number
    /// of passes taken.
    /// </summary>
    private static int Erode(
        HashSet<Vector2Int> land,
        TerrainVocabulary vocabulary
    )
    {
        const int maximumPasses = 40;

        for (int pass = 1; pass <= maximumPasses; pass++)
        {
            HashSet<Vector2Int> doomed = new HashSet<Vector2Int>();

            foreach (Vector2Int cell in land)
            {
                int signature = Signature(land, cell);

                if (
                    signature != 255 &&
                    !vocabulary.GroundBySignature.ContainsKey(signature)
                )
                {
                    doomed.Add(cell);
                }
            }

            // A water cell the vocabulary cannot draw is fixed by removing
            // the land that creates that configuration.
            foreach (Vector2Int cell in ShoreWaterCells(land))
            {
                int signature = Signature(land, cell);

                if (vocabulary.WaterBySignature.ContainsKey(signature))
                {
                    continue;
                }

                foreach (Vector2Int offset in Neighbours)
                {
                    if (land.Contains(cell + offset))
                    {
                        doomed.Add(cell + offset);
                    }
                }
            }

            if (doomed.Count == 0)
            {
                return pass - 1;
            }

            if (pass <= 3)
            {
                Debug.Log(
                    $"[Island Terrain] Erosion pass {pass}: {land.Count} " +
                    $"cells, removing {doomed.Count}."
                );
            }

            land.ExceptWith(doomed);

            if (land.Count < 150)
            {
                throw new InvalidOperationException(
                    "[Island Terrain] Erosion consumed the island. The " +
                    "learned vocabulary comes from two convex islands, so " +
                    "it has no tiles for tight concave coast; soften the " +
                    "wobble or the cove in IsInsideBaseShape."
                );
            }
        }

        throw new InvalidOperationException(
            "[Island Terrain] Shape did not settle into a drawable coastline."
        );
    }

    private static IEnumerable<Vector2Int> ShoreWaterCells(
        HashSet<Vector2Int> land
    )
    {
        HashSet<Vector2Int> shore = new HashSet<Vector2Int>();

        foreach (Vector2Int cell in land)
        {
            foreach (Vector2Int offset in Neighbours)
            {
                Vector2Int neighbour = cell + offset;

                if (!land.Contains(neighbour))
                {
                    shore.Add(neighbour);
                }
            }
        }

        return shore;
    }

    // ------------------------------------------------------------------
    // Painting
    // ------------------------------------------------------------------

    public static void Paint(
        Scene scene,
        HashSet<Vector2Int> land,
        TerrainVocabulary vocabulary
    )
    {
        Tilemap ground = FindTilemap(scene, "Tilemap_Ground");
        Tilemap water = FindTilemap(scene, "Tilemap_Water");
        Tilemap collision = FindTilemap(scene, "Tilemap_WaterCollision");
        Tilemap props = FindTilemap(scene, "Tilemap_Props");
        Tilemap overhead = FindTilemap(scene, "Tilemap_Overhead");
        Tilemap obstacle = FindTilemap(scene, "Tilemap_ObstacleCollision");

        foreach (Tilemap map in
            new[] { ground, water, collision, props, overhead, obstacle })
        {
            map.ClearAllTiles();
        }

        int missing = 0;

        for (int x = OceanBounds.xMin; x < OceanBounds.xMax; x++)
        {
            for (int y = OceanBounds.yMin; y < OceanBounds.yMax; y++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                Vector3Int position = new Vector3Int(x, y, 0);

                if (land.Contains(cell))
                {
                    int signature = Signature(land, cell);

                    TileBase tile = signature == 255
                        ? Pick(vocabulary.GroundInterior, cell)
                        : vocabulary.GroundBySignature.TryGetValue(
                            signature,
                            out TileBase shore
                        ) ? shore : Pick(vocabulary.GroundInterior, cell);

                    ground.SetTile(position, tile);
                    continue;
                }

                int waterSignature = Signature(land, cell);

                TileBase waterTile;

                if (waterSignature == 0)
                {
                    waterTile = Pick(vocabulary.OpenOcean, cell);
                }
                else if (!vocabulary.WaterBySignature.TryGetValue(
                    waterSignature,
                    out waterTile
                ))
                {
                    waterTile = Pick(vocabulary.OpenOcean, cell);
                    missing++;
                }

                water.SetTile(position, waterTile);

                if (vocabulary.WaterCollision != null)
                {
                    collision.SetTile(position, vocabulary.WaterCollision);
                }
            }
        }

        if (missing > 0)
        {
            Debug.LogWarning(
                $"[Island Terrain] {missing} shore cells fell back to open " +
                "ocean; the erosion pass should have prevented this."
            );
        }

        Debug.Log(
            $"[Island Terrain] Painted {land.Count} land cells and " +
            $"{OceanBounds.width * OceanBounds.height - land.Count} water cells."
        );
    }

    /// <summary>
    /// Deterministic weighted pick: the list holds one entry per use in the
    /// donor islands, so common sand reads as common here too.
    /// </summary>
    private static TileBase Pick(List<TileBase> options, Vector2Int cell)
    {
        if (options == null || options.Count == 0)
        {
            return null;
        }

        unchecked
        {
            int hash = cell.x * 73856093 ^ cell.y * 19349663;
            hash ^= hash >> 13;
            return options[Mathf.Abs(hash) % options.Count];
        }
    }

    // ------------------------------------------------------------------
    // Set pieces
    // ------------------------------------------------------------------

    /// <summary>
    /// Lays the pier out over the water from a landward anchor, and returns
    /// the world-space cell its seaward end sits on so the rowboat can be
    /// moored there.
    /// </summary>
    public static Vector2Int PlaceDock(
        Scene scene,
        HashSet<Vector2Int> land,
        TerrainVocabulary vocabulary,
        Vector2Int anchor
    )
    {
        Tilemap props = FindTilemap(scene, "Tilemap_Props");
        Tilemap collision = FindTilemap(scene, "Tilemap_WaterCollision");

        foreach ((Vector2Int offset, TileBase prop) in vocabulary.Dock)
        {
            Vector3Int target = new Vector3Int(
                anchor.x + offset.x,
                anchor.y + offset.y,
                0
            );

            props.SetTile(target, prop);

            // The pier is walkable, so it must not keep the water's collider.
            collision.SetTile(target, null);
        }

        return new Vector2Int(
            anchor.x + vocabulary.DockSize.x - 1,
            anchor.y + 1
        );
    }

    /// <summary>
    /// Plants palm groves wherever they fit: every trunk cell must be dry
    /// land and the whole footprint clear, so a grove never straddles the
    /// surf or grows through something already standing.
    /// </summary>
    public static int PlantGroves(
        Scene scene,
        HashSet<Vector2Int> land,
        TerrainVocabulary vocabulary,
        IEnumerable<Vector2Int> anchors
    )
    {
        Tilemap props = FindTilemap(scene, "Tilemap_Props");
        Tilemap overhead = FindTilemap(scene, "Tilemap_Overhead");
        int planted = 0;

        foreach (Vector2Int anchor in anchors)
        {
            bool fits = vocabulary.PalmGrove.All(part =>
            {
                Vector2Int cell = anchor + part.Offset;
                Vector3Int position = (Vector3Int)cell;

                if (props.HasTile(position) || overhead.HasTile(position))
                {
                    return false;
                }

                return part.Prop == null || land.Contains(cell);
            });

            if (!fits)
            {
                continue;
            }

            foreach ((Vector2Int offset, TileBase prop, TileBase canopy)
                in vocabulary.PalmGrove)
            {
                Vector3Int position = (Vector3Int)(anchor + offset);

                if (prop != null)
                {
                    props.SetTile(position, prop);
                }

                if (canopy != null)
                {
                    overhead.SetTile(position, canopy);
                }
            }

            planted++;
        }

        return planted;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static HashSet<Vector2Int> CellsOf(Tilemap map)
    {
        HashSet<Vector2Int> cells = new HashSet<Vector2Int>();

        foreach (Vector3Int cell in map.cellBounds.allPositionsWithin)
        {
            if (map.HasTile(cell))
            {
                cells.Add(new Vector2Int(cell.x, cell.y));
            }
        }

        return cells;
    }

    private static TileBase FirstTile(Tilemap map)
    {
        foreach (Vector3Int cell in map.cellBounds.allPositionsWithin)
        {
            TileBase tile = map.GetTile(cell);

            if (tile != null)
            {
                return tile;
            }
        }

        return null;
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
                $"{name} was not found in {scene.name}."
            );
        }

        return map;
    }
}
