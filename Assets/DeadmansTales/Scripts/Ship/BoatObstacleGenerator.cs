using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative obstacle spawner, driven by the boat progress bar.
///
/// This no longer spawns on its own. The progress bar (BoatLegProgress) calls
/// <see cref="Trigger"/> when the ship icon reaches an obstacle event, and this
/// spawner then places <see cref="amount"/> obstacles, waiting
/// <see cref="interval"/> seconds between each, at a random or a chosen spawn
/// point. Spawning is server-only, so it is safe for every client's progress
/// bar to call Trigger() -- clients simply do nothing.
/// </summary>
public class BoatObstacleGenerator : MonoBehaviour
{
    private const string RandomStreamName = "ShipObstacles";

    [Header("Spawn Points")]
    [Tooltip("Empty GameObjects marking where obstacles can appear.")]
    [SerializeField]
    private Transform[] spawnPoints;

    [Header("Obstacle Prefabs")]
    [Tooltip(
        "Every prefab in this array should have a NetworkObject " +
        "and should be registered as a Network Prefab."
    )]
    [SerializeField]
    private GameObject[] obstaclePrefabs;

    [Header("Spawning")]
    [Tooltip("How many obstacles to spawn each time the progress bar triggers.")]
    [SerializeField]
    [Min(0)]
    private int amount = 1;

    [Tooltip("Seconds to wait between each obstacle in a single trigger.")]
    [SerializeField]
    [Min(0f)]
    private float interval = 0.5f;

    [Tooltip(
        "ON: pick a random spawn point for each obstacle. " +
        "OFF: always use Chosen Spawn Point."
    )]
    [SerializeField]
    private bool randomSpawnPoint = true;

    [Tooltip("Spawn-point index used when Random Spawn Point is OFF.")]
    [SerializeField]
    [Min(0)]
    private int chosenSpawnPoint;

    [Header("Debug")]
    [SerializeField]
    private bool logGeneratedObstacles = true;

    private System.Random rng;
    private bool initialized;

    // The obstacles spawned by the current trigger. Pruned of destroyed entries
    // by IsResolving; the progress bar waits on this to empty out.
    private readonly List<GameObject> activeObstacles = new List<GameObject>();
    private bool spawning;

    /// <summary>
    /// True from the moment a trigger starts spawning until every obstacle it
    /// spawned has been destroyed. The boat progress bar waits on this before
    /// it resumes and clears the event icon.
    /// </summary>
    public bool IsResolving
    {
        get
        {
            activeObstacles.RemoveAll(obstacle => obstacle == null);
            return spawning || activeObstacles.Count > 0;
        }
    }

    /// <summary>
    /// Called by the boat progress bar when the ship reaches an obstacle event.
    /// Server-only; clients ignore it.
    /// </summary>
    public void Trigger()
    {
        if (!TryPrepareServer())
        {
            return;
        }

        // Mark busy up front so IsResolving is true the instant we return,
        // before the coroutine has added any obstacles to the list.
        spawning = true;
        StartCoroutine(SpawnRoutine());
    }

    private bool TryPrepareServer()
    {
        if (BoatRunDirector.Instance == null ||
            !BoatRunDirector.Instance.IsRunReady)
        {
            return false;
        }

        // Interactive NetworkObjects are only spawned by the server.
        if (!BoatRunDirector.Instance.IsServer)
        {
            return false;
        }

        if (obstaclePrefabs == null || obstaclePrefabs.Length == 0)
        {
            Debug.LogWarning(
                "[Obstacle Generator] No obstacle prefabs assigned. " +
                "Trigger will do nothing.",
                this
            );
            return false;
        }

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning(
                "[Obstacle Generator] No spawn points assigned. " +
                "Trigger will do nothing.",
                this
            );
            return false;
        }

        if (!initialized)
        {
            rng = BoatRunDirector.Instance.CreateRandom(RandomStreamName);
            initialized = true;
        }

        return true;
    }

    private IEnumerator SpawnRoutine()
    {
        // Fresh trigger: forget the previous batch (any survivors would already
        // have been pruned once destroyed).
        activeObstacles.Clear();

        // Work out where each obstacle in this trigger goes up front, so they
        // land on DIFFERENT spawn points and never stack on top of each other.
        List<Transform> points = BuildSpawnOrder();
        if (points.Count == 0)
        {
            spawning = false;
            yield break;
        }

        for (int i = 0; i < amount; i++)
        {
            GameObject obstacle = SpawnOneObstacle(points[i % points.Count]);
            if (obstacle != null)
            {
                activeObstacles.Add(obstacle);
            }

            if (interval > 0f && i < amount - 1)
            {
                yield return new WaitForSeconds(interval);
            }
        }

        // Done placing obstacles; from here IsResolving depends only on whether
        // any spawned obstacle is still alive.
        spawning = false;
    }

    // The ordered list of points to spawn at. When random, it is a shuffle of
    // every valid point (so repeats only happen once all points are used).
    // When not random, it is just the single chosen point.
    private List<Transform> BuildSpawnOrder()
    {
        List<Transform> valid = new List<Transform>();
        foreach (Transform point in spawnPoints)
        {
            if (point != null)
            {
                valid.Add(point);
            }
        }

        if (valid.Count == 0)
        {
            Debug.LogWarning(
                "[Obstacle Generator] All spawn points are empty.",
                this
            );
            return valid;
        }

        if (!randomSpawnPoint)
        {
            Transform chosen = ChosenPoint();
            valid.Clear();
            if (chosen != null)
            {
                valid.Add(chosen);
            }
            return valid;
        }

        // Fisher-Yates shuffle, driven by the seeded RNG.
        for (int i = valid.Count - 1; i > 0; i--)
        {
            int swap = rng.Next(0, i + 1);
            (valid[i], valid[swap]) = (valid[swap], valid[i]);
        }

        return valid;
    }

    // The point selected by the Chosen Spawn Point dropdown (an index into the
    // spawnPoints array). Returns null if that slot is empty.
    private Transform ChosenPoint()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            return null;
        }

        int index = Mathf.Clamp(chosenSpawnPoint, 0, spawnPoints.Length - 1);
        Transform point = spawnPoints[index];

        if (point == null)
        {
            Debug.LogWarning(
                $"[Obstacle Generator] Chosen spawn point (index {index}) " +
                "is empty.",
                this
            );
        }

        return point;
    }

    // Returns the spawned obstacle GameObject (or null if it could not be
    // spawned), so the trigger can track it for IsResolving.
    private GameObject SpawnOneObstacle(Transform point)
    {
        if (point == null)
        {
            return null;
        }

        GameObject prefab = obstaclePrefabs[rng.Next(0, obstaclePrefabs.Length)];
        if (prefab == null)
        {
            Debug.LogWarning(
                "[Obstacle Generator] Selected an empty prefab slot. Skipping.",
                this
            );
            return null;
        }

        GameObject spawned = Instantiate(
            prefab,
            point.position,
            point.rotation
        );

        NetworkObject networkObject = spawned.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            Debug.LogError(
                $"[Obstacle Generator] Prefab '{prefab.name}' has no " +
                "NetworkObject component. Destroying the object.",
                spawned
            );
            Destroy(spawned);
            return null;
        }

        networkObject.Spawn();

        if (logGeneratedObstacles)
        {
            Debug.Log(
                $"[Obstacle Generator] Spawned '{prefab.name}' at " +
                $"{(Vector2)point.position}.",
                spawned
            );
        }

        return spawned;
    }

}
