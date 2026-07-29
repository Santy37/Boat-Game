using System.Collections;
using System.Collections.Generic;
using DeadmansTales.Ship;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeadmansTales.Networking
{
    /// <summary>
    /// Server-authoritative enemy-ship spawner, driven by the boat progress bar.
    ///
    /// This no longer spawns on its own. The progress bar (BoatLegProgress)
    /// calls <see cref="Trigger"/> when the ship icon reaches an enemy event,
    /// and this spawner then places <see cref="amount"/> ships, waiting
    /// <see cref="interval"/> seconds between each, at a random or a chosen
    /// spawn point. Spawning is server-only, so every client's progress bar can
    /// safely call Trigger() -- clients simply do nothing.
    ///
    /// EnemyShip's crew (basicenemyship instances) can't be part of the
    /// EnemyShip prefab itself -- they each carry their own NetworkObject
    /// (Enemy.cs requires one), and NGO does not support nesting spawned
    /// NetworkObjects under another one. So this spawner creates the ship
    /// and its crew as separate sibling NetworkObjects, then wires them
    /// together at runtime via EnemyShipApproach.SetCrewServer and
    /// ShipEnemyAI.SetDeckBoundsServer -- the same manual wiring a
    /// scene-placed ship needs, just done in code instead of the Inspector.
    /// </summary>
    public sealed class NetworkEnemyShipSpawner2D : MonoBehaviour
    {
        private const string RandomStreamName = "EnemyShipSpawns";

        [Header("Spawn Spots")]
        [Tooltip("Empty GameObjects marking where enemy ships can appear.")]
        [SerializeField]
        private Transform[] spawnPoints = new Transform[3];

        [Header("Ship")]
        [SerializeField]
        private GameObject enemyShipPrefab;

        [Header("Spawning")]
        [Tooltip("How many ships to spawn each time the progress bar triggers.")]
        [SerializeField]
        [Min(0)]
        private int amount = 1;

        [Tooltip("Seconds to wait between each ship in a single trigger.")]
        [SerializeField]
        [Min(0f)]
        private float interval = 0.5f;

        [Tooltip(
            "How fast each spawned ship approaches the player (units/sec). " +
            "Overrides the Approach Speed on the ship prefab."
        )]
        [SerializeField]
        [Min(0f)]
        private float shipSpeed = 2f;

        [Tooltip(
            "ON: pick a random spawn point for each ship. " +
            "OFF: always use Chosen Spawn Point."
        )]
        [SerializeField]
        private bool randomSpawnPoint = true;

        [Tooltip("Spawn-point index used when Random Spawn Point is OFF.")]
        [SerializeField]
        [Min(0)]
        private int chosenSpawnPoint;

        [Header("Crew")]
        [SerializeField]
        private GameObject crewPrefab;

        [Min(0)]
        [SerializeField]
        private int crewPerShip = 2;

        [Tooltip(
            "Local offsets (relative to the ship's spawn position) each " +
            "crew member is placed at. Should be sized to at least " +
            "Crew Per Ship -- extras beyond Crew Per Ship are unused, and " +
            "offsets are reused (with a small jitter) if there are more " +
            "crew than offsets."
        )]
        [SerializeField]
        private Vector2[] crewLocalOffsets =
        {
            new Vector2(-1.5f, 0.5f),
            new Vector2(1.5f, -0.5f)
        };

        private System.Random rng;
        private bool initialized;

        // The ships spawned by the current trigger. Pruned of destroyed entries
        // by IsResolving; the progress bar waits on this to empty out.
        private readonly List<GameObject> activeShips = new List<GameObject>();
        private bool spawning;

        /// <summary>
        /// True from the moment a trigger starts spawning until every ship it
        /// spawned has been destroyed. The boat progress bar waits on this
        /// before it resumes and clears the event icon.
        /// </summary>
        public bool IsResolving
        {
            get
            {
                activeShips.RemoveAll(ship => ship == null);
                return spawning || activeShips.Count > 0;
            }
        }

        /// <summary>
        /// Called by the boat progress bar when the ship reaches an enemy event.
        /// Server-only; clients ignore it.
        /// </summary>
        public void Trigger()
        {
            if (!TryPrepareServer())
            {
                return;
            }

            // Mark busy up front so IsResolving is true the instant we return,
            // before the coroutine has added any ships to the list.
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

            if (!BoatRunDirector.Instance.IsServer)
            {
                return false;
            }

            if (enemyShipPrefab == null)
            {
                Debug.LogWarning(
                    "[Enemy Ship Spawner] No enemy ship prefab assigned.",
                    this
                );
                return false;
            }

            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                Debug.LogWarning(
                    "[Enemy Ship Spawner] No spawn points assigned.",
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
            // Fresh trigger: forget the previous batch (any survivors would
            // already have been pruned once destroyed).
            activeShips.Clear();

            // Decide where each ship in this trigger goes up front, so they
            // land on DIFFERENT spawn points and never stack on top of each
            // other.
            List<Transform> spots = BuildSpawnOrder();
            if (spots.Count == 0)
            {
                spawning = false;
                yield break;
            }

            for (int i = 0; i < amount; i++)
            {
                GameObject ship = SpawnOneShip(spots[i % spots.Count], rng);
                if (ship != null)
                {
                    activeShips.Add(ship);
                }

                if (interval > 0f && i < amount - 1)
                {
                    yield return new WaitForSeconds(interval);
                }
            }

            // Done placing ships; from here IsResolving depends only on whether
            // any spawned ship is still alive.
            spawning = false;
        }

        // The ordered list of spots to spawn at. When random, it is a shuffle
        // of every valid spot (so repeats only happen once all spots are used).
        // When not random, it is just the single chosen spot.
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
                    "[Enemy Ship Spawner] All spawn points are empty.",
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

        // The spot selected by the Chosen Spawn Point dropdown (an index into
        // the spawnPoints array). Returns null if that slot is empty.
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
                    $"[Enemy Ship Spawner] Chosen spawn point (index {index}) " +
                    "is empty.",
                    this
                );
            }

            return point;
        }

        // Returns the spawned ship GameObject (or null if it could not be
        // spawned), so the trigger can track it for IsResolving.
        private GameObject SpawnOneShip(Transform spot, System.Random rng)
        {
            GameObject shipObject = Instantiate(
                enemyShipPrefab,
                spot.position,
                spot.rotation
            );

            SceneManager.MoveGameObjectToScene(shipObject, gameObject.scene);

            NetworkObject shipNetworkObject =
                shipObject.GetComponent<NetworkObject>();

            if (shipNetworkObject == null)
            {
                Debug.LogWarning(
                    "[Enemy Ship Spawner] Enemy ship prefab has no " +
                    "NetworkObject -- destroying the bad instance.",
                    this
                );
                Destroy(shipObject);
                return null;
            }

            shipNetworkObject.Spawn(true);

            EnemyShipApproach shipApproach =
                shipObject.GetComponent<EnemyShipApproach>();

            if (shipApproach != null)
            {
                // Let the spawner dictate how fast the ship closes in.
                shipApproach.SetApproachSpeedServer(shipSpeed);
            }

            if (shipApproach == null || crewPrefab == null || crewPerShip <= 0)
            {
                return shipObject;
            }

            Enemy[] spawnedCrew = new Enemy[crewPerShip];

            for (int i = 0; i < crewPerShip; i++)
            {
                spawnedCrew[i] = SpawnOneCrewMember(
                    shipObject.transform,
                    shipApproach,
                    i,
                    rng
                );
            }

            shipApproach.SetCrewServer(spawnedCrew);

            return shipObject;
        }

        private Enemy SpawnOneCrewMember(
            Transform shipTransform,
            EnemyShipApproach shipApproach,
            int crewIndex,
            System.Random rng
        )
        {
            Vector2 offset = crewLocalOffsets.Length > 0
                ? crewLocalOffsets[crewIndex % crewLocalOffsets.Length]
                : Vector2.zero;

            // Extra crew beyond the configured offset list get a small
            // random jitter so they don't stack exactly on top of a reused
            // offset.
            if (crewLocalOffsets.Length > 0 &&
                crewIndex >= crewLocalOffsets.Length)
            {
                offset += new Vector2(
                    (float)(rng.NextDouble() - 0.5) * 0.6f,
                    (float)(rng.NextDouble() - 0.5) * 0.6f
                );
            }

            // Anchor to the deck collider's own actual world position, not
            // the ship root's Transform -- on this prefab the root pivot
            // sits well away from the visual deck (observed ~6 units off in
            // Y), so any offset measured from shipTransform lands off the
            // actual deck regardless of the offset's value. The deck
            // collider's bounds.center is always where the deck really is,
            // so we rotate the configured offset (via TransformVector, which
            // rotates/scales without translating) and add it on top of that.
            Vector3 spawnPosition = shipApproach.DeckBoundsForCrew != null
                ? shipApproach.DeckBoundsForCrew.bounds.center +
                    shipTransform.TransformVector(offset)
                : shipTransform.TransformPoint(offset);

            Debug.Log(
                $"[Enemy Ship Spawner] Crew {crewIndex} -- " +
                $"shipTransform.position={(Vector2)shipTransform.position}, " +
                $"offset={offset}, computed spawnPosition=" +
                $"{(Vector2)spawnPosition}.",
                this
            );

            GameObject crewObject = Instantiate(
                crewPrefab,
                spawnPosition,
                shipTransform.rotation
            );

            Debug.Log(
                $"[Enemy Ship Spawner] Crew {crewIndex} instantiated -- " +
                $"crewObject.transform.position=" +
                $"{(Vector2)crewObject.transform.position} (should match " +
                "spawnPosition above).",
                this
            );

            SceneManager.MoveGameObjectToScene(crewObject, gameObject.scene);

            NetworkObject crewNetworkObject =
                crewObject.GetComponent<NetworkObject>();

            if (crewNetworkObject == null)
            {
                Debug.LogWarning(
                    "[Enemy Ship Spawner] Crew prefab has no NetworkObject " +
                    "-- destroying the bad instance.",
                    this
                );
                Destroy(crewObject);
                return null;
            }

            // Wire deck bounds BEFORE Spawn() -- Spawn() immediately
            // triggers ShipEnemyAI.OnNetworkSpawn, which picks this crew
            // member's first wander target. If deckBounds is still null at
            // that moment, the first target is picked unconstrained (can
            // land off the ship entirely), so the pirate walks toward it
            // and ends up stuck right at the collider's edge once later
            // picks become properly bounded.
            ShipEnemyAI crewAi = crewObject.GetComponent<ShipEnemyAI>();
            crewAi?.SetDeckBoundsServer(shipApproach.DeckBoundsForCrew);

            crewNetworkObject.Spawn(true);

            return crewObject.GetComponent<Enemy>();
        }
    }
}
