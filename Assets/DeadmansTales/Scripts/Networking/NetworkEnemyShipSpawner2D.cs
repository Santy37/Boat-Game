using System.Collections;
using DeadmansTales.Ship;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeadmansTales.Networking
{
    /// <summary>
    /// Seeds 1-3 enemy ships into a fixed set of designated spawn spots for
    /// the ocean voyage. Follows the same seeded-RNG pattern as
    /// BoatObstacleGenerator (BoatRunDirector.CreateRandom) and the same
    /// server-ready spawn/scene-placement pattern as
    /// NetworkSceneEnemySpawner2D.
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
        [Tooltip("Exactly one enemy ship will be attempted per chosen spot, up to Maximum Ships Spawned.")]
        [SerializeField]
        private Transform[] spawnPoints = new Transform[3];

        [Header("Ship")]
        [SerializeField]
        private GameObject enemyShipPrefab;

        [Tooltip("Random count of ships spawned is chosen between these, inclusive.")]
        [SerializeField]
        [Min(1)]
        private int minimumShipsSpawned = 1;

        [SerializeField]
        [Min(1)]
        private int maximumShipsSpawned = 3;

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

        private Coroutine spawnRoutine;

        private void OnEnable()
        {
            Debug.Log(
                $"[Enemy Ship Spawner] {name} OnEnable -- starting " +
                "SpawnWhenReady coroutine.",
                this
            );
            spawnRoutine = StartCoroutine(SpawnWhenReady());
        }

        private void OnDisable()
        {
            if (spawnRoutine != null)
            {
                StopCoroutine(spawnRoutine);
                spawnRoutine = null;
            }
        }

        private IEnumerator SpawnWhenReady()
        {
            float nextWaitLogTime = 0f;

            while (
                BoatRunDirector.Instance == null ||
                !BoatRunDirector.Instance.IsRunReady
            )
            {
                if (Time.time >= nextWaitLogTime)
                {
                    nextWaitLogTime = Time.time + 2f;
                    Debug.Log(
                        $"[Enemy Ship Spawner] {name} waiting -- " +
                        $"BoatRunDirector.Instance=" +
                        $"{(BoatRunDirector.Instance != null)}, IsRunReady=" +
                        $"{(BoatRunDirector.Instance != null && BoatRunDirector.Instance.IsRunReady)}.",
                        this
                    );
                }

                yield return null;
            }

            BoatRunDirector runDirector = BoatRunDirector.Instance;

            Debug.Log(
                $"[Enemy Ship Spawner] {name} run is ready -- " +
                $"runDirector.IsServer={runDirector.IsServer}.",
                this
            );

            if (!runDirector.IsServer)
            {
                Debug.Log(
                    $"[Enemy Ship Spawner] {name} is not the server -- " +
                    "skipping spawn on this client.",
                    this
                );
                spawnRoutine = null;
                yield break;
            }

            SpawnEnemyShips(runDirector);
            spawnRoutine = null;
        }

        private void SpawnEnemyShips(BoatRunDirector runDirector)
        {
            if (enemyShipPrefab == null)
            {
                Debug.LogWarning(
                    "[Enemy Ship Spawner] No enemy ship prefab assigned.",
                    this
                );
                return;
            }

            Transform[] validSpawnPoints = FilterValidSpawnPoints();

            if (validSpawnPoints.Length == 0)
            {
                Debug.LogWarning(
                    "[Enemy Ship Spawner] No spawn points assigned.",
                    this
                );
                return;
            }

            System.Random rng = runDirector.CreateRandom(RandomStreamName);

            int lowClamped = Mathf.Clamp(
                minimumShipsSpawned,
                1,
                validSpawnPoints.Length
            );
            int highClamped = Mathf.Clamp(
                maximumShipsSpawned,
                lowClamped,
                validSpawnPoints.Length
            );

            // System.Random.Next's upper bound is exclusive.
            int shipCount = rng.Next(lowClamped, highClamped + 1);

            int[] spotOrder = ShuffledIndices(validSpawnPoints.Length, rng);

            int spawned = 0;
            for (
                int index = 0;
                index < spotOrder.Length && spawned < shipCount;
                index++
            )
            {
                Transform spot = validSpawnPoints[spotOrder[index]];
                SpawnOneShip(spot, rng);
                spawned++;
            }

            Debug.Log(
                $"[Enemy Ship Spawner] Spawned {spawned} enemy ship(s) " +
                $"in {gameObject.scene.name}.",
                this
            );
        }

        private Transform[] FilterValidSpawnPoints()
        {
            int validCount = 0;

            foreach (Transform point in spawnPoints)
            {
                if (point != null)
                {
                    validCount++;
                }
            }

            Transform[] valid = new Transform[validCount];
            int writeIndex = 0;

            foreach (Transform point in spawnPoints)
            {
                if (point != null)
                {
                    valid[writeIndex] = point;
                    writeIndex++;
                }
            }

            return valid;
        }

        private static int[] ShuffledIndices(int count, System.Random rng)
        {
            int[] indices = new int[count];

            for (int i = 0; i < count; i++)
            {
                indices[i] = i;
            }

            // Fisher-Yates, driven by the seeded RNG so spot selection is
            // deterministic for a given seed.
            for (int i = count - 1; i > 0; i--)
            {
                int swapIndex = rng.Next(0, i + 1);
                (indices[i], indices[swapIndex]) =
                    (indices[swapIndex], indices[i]);
            }

            return indices;
        }

        private void SpawnOneShip(Transform spot, System.Random rng)
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
                return;
            }

            shipNetworkObject.Spawn(true);

            EnemyShipApproach shipApproach =
                shipObject.GetComponent<EnemyShipApproach>();

            if (shipApproach == null || crewPrefab == null || crewPerShip <= 0)
            {
                return;
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
