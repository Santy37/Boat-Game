using System.Collections;
using System.Collections.Generic;
using DeadmansTales.Ship;
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
        "How fast each spawned obstacle drifts toward the ship (units/sec). " +
        "Overrides the Drift Speed on the obstacle prefab."
    )]
    [SerializeField]
    [Min(0f)]
    private float obstacleSpeed = 1.5f;

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
    private PlayerShipMarker playerShip;

    // One warning per reason per run: an obstacle event fires repeatedly and
    // this would otherwise flood the Console.
    private readonly HashSet<string> warnedMessages = new HashSet<string>();

    private void WarnOnce(string message)
    {
        if (warnedMessages.Add(message))
        {
            Debug.LogWarning(message, this);
        }
    }

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
    /// True once this wave is down to its last obstacle, i.e. the event is
    /// about to resolve and the progress bar is about to resume.
    ///
    /// Obstacles read this before playing their hull-impact clip. A rock that
    /// reaches the ship at the very end lands its "heavy object impact" at the
    /// same instant the bar starts moving again, which reads as the progress
    /// bar itself making a destruction noise as it passes the rock icon. The
    /// hit still happens and still damages the ship -- it just stops
    /// announcing itself at the one moment it is guaranteed to be misread.
    /// </summary>
    public bool WaveClearing
    {
        get
        {
            activeObstacles.RemoveAll(obstacle => obstacle == null);
            return !spawning && activeObstacles.Count <= 1;
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
        // Interactive NetworkObjects are only spawned by the server. Clients
        // call Trigger too (every peer's progress bar does), and for them
        // doing nothing is correct and silent.
        if (BoatRunDirector.Instance != null &&
            !BoatRunDirector.Instance.IsServer)
        {
            return false;
        }

        // The two gates below used to return silently, so an obstacle event
        // that spawned nothing looked identical to one that never fired --
        // "the rocks aren't working" with an empty Console. Warned once, on
        // the server only, so it says which gate stopped it.
        if (BoatRunDirector.Instance == null)
        {
            WarnOnce(
                "[Obstacle Generator] No BoatRunDirector in the scene, so " +
                "obstacles cannot spawn.");
            return false;
        }

        if (!BoatRunDirector.Instance.IsRunReady)
        {
            WarnOnce(
                "[Obstacle Generator] The run is not ready (needs a seed and " +
                "a loaded config), so this obstacle event spawned nothing. " +
                "If the run was started from the multiplayer lobby, check " +
                "that it seeded the run.");
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

        // Use only the spawn point's POSITION, never its rotation. The spawn
        // points are parented under a container tilted 75 deg on X (to lay the
        // markers out along the water's perspective), and inheriting that world
        // rotation foreshortened the flat 2D sprite so it spawned squashed.
        // Identity rotation matches how the prefab looks when dropped into the
        // scene by hand.
        GameObject spawned = Instantiate(
            prefab,
            point.position,
            Quaternion.identity
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

        // destroyWithScene: true, same reason as the chest's food spill --
        // NGO's default of false would leave voyage obstacles alive in
        // whatever scene the crew lands in next.
        networkObject.Spawn(true);

        DestructibleObstacle obstacle =
            spawned.GetComponent<DestructibleObstacle>();
        if (obstacle != null)
        {
            // Let the generator dictate how fast obstacles move.
            obstacle.SetSpeedServer(obstacleSpeed);

            // So the obstacle can ask whether the wave is already clearing
            // before it plays its hull-impact clip.
            obstacle.SetOwningGeneratorServer(this);

            // Lock the obstacle onto a straight line from its spawn point to
            // where the ship is right now. It never re-aims, so it does not
            // follow the ship and the line stays exactly where it was drawn.
            PlayerShipMarker ship = ResolvePlayerShip();
            if (ship != null)
            {
                obstacle.SetCourseServer(
                    ship.AimPoint - (Vector2)point.position);
            }
        }

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

    // The player ship, cached. Its position is read fresh each spawn (so each
    // obstacle aims at where the ship is at that moment), but the reference
    // itself only needs resolving once.
    private PlayerShipMarker ResolvePlayerShip()
    {
        if (playerShip == null)
        {
            playerShip = FindFirstObjectByType<PlayerShipMarker>();
        }

        return playerShip;
    }

}
