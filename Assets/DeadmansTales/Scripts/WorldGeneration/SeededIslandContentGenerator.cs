using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DeadmansTales.Networking;
using Unity.Netcode;
using UnityEngine;

namespace DeadmansTales.WorldGeneration
{
    /// <summary>
    /// Server-authoritative island-content generator.
    ///
    /// Designers place SeededSpawnMarker2D components in a hand-built island.
    /// The server uses the synchronized stage seed to decide which markers are
    /// active and which registered NetworkObject prefab each marker receives.
    /// Clients never make authoritative spawn decisions.
    /// </summary>
    [RequireComponent(typeof(StageSeedProvider))]
    public sealed class SeededIslandContentGenerator : MonoBehaviour
    {
        [Header("References")]
        [SerializeField]
        private StageSeedProvider seedProvider;

        [Header("Generation")]
        [SerializeField]
        private bool generateAutomatically = true;

        [SerializeField]
        private bool logEachSpawn;

        private readonly List<NetworkObject> spawnedObjects =
            new List<NetworkObject>();

        private Coroutine generationRoutine;
        private bool generationComplete;

        public bool GenerationComplete => generationComplete;

        public int SpawnedObjectCount => spawnedObjects.Count;

        private void Awake()
        {
            if (seedProvider == null)
            {
                seedProvider = GetComponent<StageSeedProvider>();
            }
        }

        private void OnEnable()
        {
            if (generateAutomatically)
            {
                generationRoutine = StartCoroutine(GenerateWhenReady());
            }
        }

        private void OnDisable()
        {
            if (generationRoutine != null)
            {
                StopCoroutine(generationRoutine);
                generationRoutine = null;
            }
        }

        /// <summary>
        /// Generates the island once. Safe to call from a server-side stage
        /// director when automatic generation is disabled.
        /// </summary>
        public void GenerateServer()
        {
            if (generationComplete)
            {
                return;
            }

            if (!ValidateServerState())
            {
                return;
            }

            if (seedProvider == null || !seedProvider.TryGetContext(
                    out StageSeedContext context
                ))
            {
                Debug.LogWarning(
                    "[Island Generation] Stage seed context is not ready.",
                    this
                );
                return;
            }

            GenerateFromContext(context);
        }

        /// <summary>
        /// Removes content previously spawned by this generator. This is meant
        /// for server-side stage restarts and test tooling.
        /// </summary>
        public void ClearGeneratedServer()
        {
            if (!ValidateServerState())
            {
                return;
            }

            for (int index = spawnedObjects.Count - 1; index >= 0; index--)
            {
                NetworkObject spawnedObject = spawnedObjects[index];

                if (spawnedObject == null)
                {
                    continue;
                }

                if (spawnedObject.IsSpawned)
                {
                    spawnedObject.Despawn(true);
                }
                else
                {
                    Destroy(spawnedObject.gameObject);
                }
            }

            spawnedObjects.Clear();
            generationComplete = false;

            Debug.Log(
                "[Island Generation] Cleared generated island content.",
                this
            );
        }

        private IEnumerator GenerateWhenReady()
        {
            while (
                NetworkManager.Singleton == null ||
                !NetworkManager.Singleton.IsListening ||
                seedProvider == null ||
                !seedProvider.IsReady
            )
            {
                yield return null;
            }

            if (NetworkManager.Singleton.IsServer)
            {
                GenerateServer();
            }

            generationRoutine = null;
        }

        private void GenerateFromContext(StageSeedContext context)
        {
            SeededSpawnMarker2D[] markers =
                FindObjectsByType<SeededSpawnMarker2D>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None
                )
                .Where(
                    marker =>
                        marker != null &&
                        marker.enabled &&
                        marker.IsEligibleForStage(context.StageIndex)
                )
                .OrderBy(
                    marker => marker.DeterministicKey,
                    StringComparer.Ordinal
                )
                .ToArray();

            HashSet<string> usedKeys = new HashSet<string>(
                StringComparer.Ordinal
            );

            int attemptedMarkers = 0;
            int successfulSpawns = 0;

            foreach (SeededSpawnMarker2D marker in markers)
            {
                attemptedMarkers++;

                string markerKey = marker.DeterministicKey;

                if (!usedKeys.Add(markerKey))
                {
                    Debug.LogWarning(
                        "[Island Generation] Two markers share the same " +
                        $"deterministic key: {markerKey}",
                        marker
                    );
                }

                System.Random random = context.CreateRandom(
                    $"{marker.StreamName}|{markerKey}"
                );

                if (!marker.ShouldSpawn(random))
                {
                    continue;
                }

                if (!marker.TrySelectPrefab(random, out GameObject prefab))
                {
                    Debug.LogWarning(
                        "[Island Generation] Marker has no valid prefab: " +
                        marker.name,
                        marker
                    );
                    continue;
                }

                NetworkObject prefabNetworkObject =
                    prefab.GetComponent<NetworkObject>();

                if (prefabNetworkObject == null)
                {
                    Debug.LogError(
                        "[Island Generation] Prefab must have a NetworkObject " +
                        $"on its root: {prefab.name}",
                        prefab
                    );
                    continue;
                }

                GameObject spawnedGameObject = Instantiate(
                    prefab,
                    marker.SpawnPosition,
                    marker.SpawnRotation
                );

                NetworkObject spawnedNetworkObject =
                    spawnedGameObject.GetComponent<NetworkObject>();

                if (spawnedNetworkObject == null)
                {
                    Debug.LogError(
                        "[Island Generation] Spawned object unexpectedly has " +
                        $"no NetworkObject: {spawnedGameObject.name}",
                        spawnedGameObject
                    );

                    Destroy(spawnedGameObject);
                    continue;
                }

                spawnedNetworkObject.Spawn(true);
                spawnedObjects.Add(spawnedNetworkObject);
                successfulSpawns++;

                if (logEachSpawn)
                {
                    Debug.Log(
                        "[Island Generation] Spawned " +
                        $"'{prefab.name}' at marker '{marker.name}'.",
                        spawnedGameObject
                    );
                }
            }

            generationComplete = true;

            Debug.Log(
                "[Island Generation] Complete.\n" +
                $"Stage: {context.StageIndex}\n" +
                $"Master Seed: {context.MasterSeed}\n" +
                $"Eligible Markers: {attemptedMarkers}\n" +
                $"Spawned Objects: {successfulSpawns}",
                this
            );
        }

        private bool ValidateServerState()
        {
            if (
                NetworkManager.Singleton == null ||
                !NetworkManager.Singleton.IsListening
            )
            {
                Debug.LogWarning(
                    "[Island Generation] Networking has not started.",
                    this
                );
                return false;
            }

            if (!NetworkManager.Singleton.IsServer)
            {
                Debug.LogWarning(
                    "[Island Generation] Only the server may generate " +
                    "networked island content.",
                    this
                );
                return false;
            }

            return true;
        }
    }
}
