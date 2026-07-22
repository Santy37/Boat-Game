using System;
using System.Collections.Generic;

public enum RunNodeKind
{
    Island,
    Treasure,
    Elite,
    Boss
}

[Serializable]
public class RunMapNode
{
    /// <summary>Vertical level. 0 is the lobby island at the bottom.</summary>
    public int level;

    /// <summary>Horizontal position within the level, left to right.</summary>
    public int slot;

    public int id;
    public RunNodeKind kind;
    public List<int> next = new List<int>();
}

/// <summary>
/// The run's map, built bottom-up:
///
///   level 0            one lobby island (where the run starts)
///   levels 1..n-2      2-3 islands each
///   level n-1          one boss island
///
/// Connections only ever go UP one level, never sideways, and each island links
/// to 1-2 islands directly above it. Targets are chosen proportionally to the
/// island's own position, so <b>edges can never cross</b>.
///
/// Generated purely from the run seed, so it is identical everywhere and
/// reproducible for testing.
/// </summary>
public class RunMapModel
{
    public List<RunMapNode> Nodes { get; } = new List<RunMapNode>();

    public int StartNodeId { get; private set; }
    public int BossNodeId { get; private set; }
    public int Levels { get; private set; }

    /// <summary>Minimum levels: lobby, at least one island row, boss.</summary>
    public const int MinLevels = 3;

    public static RunMapModel Generate(
        int seed,
        int levels,
        int minPerLevel = 2,
        int maxPerLevel = 3,
        int secondBranchPercent = 55)
    {
        levels = Math.Max(MinLevels, levels);
        minPerLevel = Math.Max(1, minPerLevel);
        maxPerLevel = Math.Max(minPerLevel, maxPerLevel);

        RunMapModel map = new RunMapModel { Levels = levels };
        System.Random rng = new System.Random(seed);

        List<List<RunMapNode>> grid = new List<List<RunMapNode>>();
        int nextId = 0;

        for (int level = 0; level < levels; level++)
        {
            bool isLobby = level == 0;
            bool isBoss = level == levels - 1;

            int count = (isLobby || isBoss)
                ? 1
                : rng.Next(minPerLevel, maxPerLevel + 1);

            List<RunMapNode> row = new List<RunMapNode>();

            for (int slot = 0; slot < count; slot++)
            {
                RunMapNode node = new RunMapNode
                {
                    id = nextId++,
                    level = level,
                    slot = slot,
                    kind = isBoss
                        ? RunNodeKind.Boss
                        : isLobby
                            ? RunNodeKind.Island
                            : PickKind(rng)
                };

                row.Add(node);
                map.Nodes.Add(node);
            }

            grid.Add(row);
        }

        ConnectLevels(grid, rng, secondBranchPercent);
        PruneUnreachable(map, grid);

        map.StartNodeId = grid[0][0].id;
        map.BossNodeId = grid[grid.Count - 1][0].id;

        return map;
    }

    /// <summary>
    /// Links each level to the one above. A node at position i of n links to
    /// the proportionally matching node t of m, plus optionally t+1. Because t
    /// never decreases as i increases, and the extra edge only ever goes to
    /// t+1, the edges are monotonic - which is what guarantees no crossings.
    /// </summary>
    private static void ConnectLevels(
        List<List<RunMapNode>> grid,
        System.Random rng,
        int secondBranchPercent)
    {
        for (int level = 0; level < grid.Count - 1; level++)
        {
            List<RunMapNode> source = grid[level];
            List<RunMapNode> target = grid[level + 1];

            int n = source.Count;
            int m = target.Count;

            for (int i = 0; i < n; i++)
            {
                int t = n == 1
                    ? (int)Math.Round((m - 1) * 0.5)
                    : (int)Math.Round(i * (m - 1) / (double)(n - 1));

                t = Math.Clamp(t, 0, m - 1);

                AddEdge(source[i], target[t]);

                // Only ever branch upward-right, never left: keeps it monotonic.
                if (t + 1 <= m - 1 && rng.Next(100) < secondBranchPercent)
                {
                    AddEdge(source[i], target[t + 1]);
                }
            }
        }
    }

    private static void AddEdge(RunMapNode from, RunMapNode to)
    {
        if (!from.next.Contains(to.id))
        {
            from.next.Add(to.id);
        }
    }

    /// <summary>
    /// Drops islands nothing can reach, so the map never shows a dangling node.
    /// Walked bottom-up, so removing a node also cleans up anything above that
    /// only depended on it. The boss always survives (every path ends there).
    /// </summary>
    private static void PruneUnreachable(
        RunMapModel map,
        List<List<RunMapNode>> grid)
    {
        for (int level = 1; level < grid.Count; level++)
        {
            HashSet<int> reachable = new HashSet<int>();

            foreach (RunMapNode below in grid[level - 1])
            {
                foreach (int id in below.next)
                {
                    reachable.Add(id);
                }
            }

            List<RunMapNode> row = grid[level];

            for (int i = row.Count - 1; i >= 0; i--)
            {
                if (!reachable.Contains(row[i].id))
                {
                    map.Nodes.Remove(row[i]);
                    row.RemoveAt(i);
                }
            }

            // Renumber slots so the survivors sit evenly, order preserved.
            for (int i = 0; i < row.Count; i++)
            {
                row[i].slot = i;
            }
        }
    }

    private static RunNodeKind PickKind(System.Random rng)
    {
        int roll = rng.Next(100);

        if (roll < 60) return RunNodeKind.Island;
        if (roll < 85) return RunNodeKind.Treasure;
        return RunNodeKind.Elite;
    }

    public RunMapNode Get(int id)
    {
        return Nodes.Find(node => node.id == id);
    }

    public int CountAtLevel(int level)
    {
        int count = 0;

        foreach (RunMapNode node in Nodes)
        {
            if (node.level == level)
            {
                count++;
            }
        }

        return count;
    }

    public List<RunMapNode> NextFrom(int id)
    {
        List<RunMapNode> results = new List<RunMapNode>();

        RunMapNode node = Get(id);
        if (node == null)
        {
            return results;
        }

        foreach (int nextId in node.next)
        {
            RunMapNode target = Get(nextId);
            if (target != null)
            {
                results.Add(target);
            }
        }

        return results;
    }
}
