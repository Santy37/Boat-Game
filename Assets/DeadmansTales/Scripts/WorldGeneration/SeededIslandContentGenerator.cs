using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DeadmansTales.Networking;
using Unity.Netcode;
using UnityEngine;

namespace DeadmansTales.WorldGeneration
{
    [Serializable]
    public sealed class SeededContentBudget
    {
        [SerializeField]
        private SeededContentCategory category;

        [SerializeField]
        [Min(0)]
        private int minimumCount;

        [SerializeField]
        [Min(0)]
        private int maximumCount;

        public SeededContentCategory Category => category;

        public int MinimumCount => Mathf.Max(0, minimumCount);

        public int MaximumCount => Mathf.Max(MinimumCount, maximumCount);
    }

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

        [SerializeField]
        [Tooltip(
            "Optional per-category budgets. Categories without a budget use " +
            "each marker's independent spawn chance."
        )]
        private SeededContentBudget[] contentBudgets =
            Array.Empty<SeededContentBudget>();

        private readonly List<NetworkObject> spawnedObjects =
            new List<NetworkObject>();

        private Coroutine generationRoutine;
        private bool generationComplete;

        public bool GenerationComplete => generationComplete;

        public int SpawnedObjectCount
        {
            get
            {
                spawnedObjects.RemoveAll(spawnedObject =>
                    spawnedObject == null || !spawnedObject.IsSpawned);
                return spawnedObjects.Count;
            }
        }

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
            // OnEnable can run before later scene objects (including designer
            // markers) have completed activation. Waiting one frame prevents
            // a fast host from scanning an only partially activated scene.
            yield return null;

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
            // Query this scene's hierarchy directly. A global active-object
            // query can return no markers during an NGO scene activation even
            // though the serialized hierarchy has already loaded.
            GameObject[] sceneRoots = gameObject.scene.GetRootGameObjects();
            SeededSpawnMarker2D[] markers = sceneRoots
                .SelectMany(root =>
                    root.GetComponentsInChildren<SeededSpawnMarker2D>(true))
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

            if (markers.Length == 0)
            {
                int serializedMarkerCount = sceneRoots
                    .SelectMany(root =>
                        root.GetComponentsInChildren<MonoBehaviour>(true))
                    .Count(component =>
                        component != null &&
                        component.GetType() == typeof(SeededSpawnMarker2D));

                Debug.LogWarning(
                    "[Island Generation] No eligible markers were found.\n" +
                    $"Generator Scene: {gameObject.scene.name}\n" +
                    $"Scene Roots: {string.Join(", ", sceneRoots.Select(root => root.name))}\n" +
                    $"Serialized Marker Components: {serializedMarkerCount}",
                    this
                );
            }

            ValidateUniqueMarkerKeys(markers);

            List<SeededSpawnMarker2D> selectedMarkers =
                SelectMarkersWithinBudgets(markers, context);

            int successfulSpawns = 0;

            foreach (SeededSpawnMarker2D marker in selectedMarkers)
            {
                if (TrySpawnMarker(marker, context))
                {
                    successfulSpawns++;
                }
            }

            generationComplete = true;

            Debug.Log(
                "[Island Generation] Complete.\n" +
                $"Stage: {context.StageIndex}\n" +
                $"Master Seed: {context.MasterSeed}\n" +
                $"Eligible Markers: {markers.Length}\n" +
                $"Selected Markers: {selectedMarkers.Count}\n" +
                $"Spawned Objects: {successfulSpawns}",
                this
            );
        }

        private void ValidateUniqueMarkerKeys(
            IEnumerable<SeededSpawnMarker2D> markers
        )
        {
            HashSet<string> usedKeys = new HashSet<string>(
                StringComparer.Ordinal
            );

            foreach (SeededSpawnMarker2D marker in markers)
            {
                string markerKey = marker.DeterministicKey;

                if (!usedKeys.Add(markerKey))
                {
                    Debug.LogWarning(
                        "[Island Generation] Two markers share the same " +
                        $"deterministic key: {markerKey}",
                        marker
                    );
                }
            }
        }

        private List<SeededSpawnMarker2D> SelectMarkersWithinBudgets(
            IEnumerable<SeededSpawnMarker2D> markers,
            StageSeedContext context
        )
        {
            List<SeededSpawnMarker2D> selected =
                new List<SeededSpawnMarker2D>();

            foreach (IGrouping<SeededContentCategory, SeededSpawnMarker2D>
                categoryGroup in markers
                    .GroupBy(marker => marker.Category)
                    .OrderBy(group => (byte)group.Key))
            {
                List<SeededSpawnMarker2D> categoryMarkers =
                    categoryGroup
                        .OrderBy(
                            marker => marker.DeterministicKey,
                            StringComparer.Ordinal
                        )
                        .ToList();

                SeededContentBudget budget = FindBudget(categoryGroup.Key);

                if (budget == null)
                {
                    foreach (SeededSpawnMarker2D marker in categoryMarkers)
                    {
                        System.Random markerRandom = context.CreateRandom(
                            $"{marker.StreamName}|{marker.DeterministicKey}|Active"
                        );

                        if (marker.ShouldSpawn(markerRandom))
                        {
                            selected.Add(marker);
                        }
                    }

                    continue;
                }

                System.Random budgetRandom = context.CreateRandom(
                    $"IslandContent.{categoryGroup.Key}.Budget"
                );

                Shuffle(categoryMarkers, budgetRandom);

                int available = categoryMarkers.Count;
                int minimum = Mathf.Min(budget.MinimumCount, available);
                int maximum = Mathf.Min(budget.MaximumCount, available);
                int targetCount = maximum <= minimum
                    ? minimum
                    : budgetRandom.Next(minimum, maximum + 1);

                List<SeededSpawnMarker2D> categorySelection =
                    categoryMarkers
                        .Where(marker => marker.AlwaysSpawn)
                        .ToList();

                if (categorySelection.Count > maximum)
                {
                    Debug.LogWarning(
                        $"[Island Content] Category " +
                        $"'{categoryGroup.Key}' has " +
                        $"{categorySelection.Count} AlwaysSpawn markers, " +
                        $"exceeding its budget maximum of {maximum}. All " +
                        "guaranteed markers will still spawn.",
                        this
                    );
                }

                foreach (SeededSpawnMarker2D marker in categoryMarkers)
                {
                    if (
                        categorySelection.Count >= targetCount ||
                        marker.AlwaysSpawn
                    )
                    {
                        continue;
                    }

                    System.Random markerRandom = context.CreateRandom(
                        $"{marker.StreamName}|{marker.DeterministicKey}|Active"
                    );

                    if (marker.ShouldSpawn(markerRandom))
                    {
                        categorySelection.Add(marker);
                    }
                }

                if (categorySelection.Count < minimum)
                {
                    foreach (SeededSpawnMarker2D marker in categoryMarkers)
                    {
                        if (
                            categorySelection.Count >= minimum ||
                            categorySelection.Contains(marker)
                        )
                        {
                            continue;
                        }

                        categorySelection.Add(marker);
                    }
                }

                selected.AddRange(categorySelection);
            }

            return selected
                .OrderBy(marker => marker.DeterministicKey, StringComparer.Ordinal)
                .ToList();
        }

        private SeededContentBudget FindBudget(
            SeededContentCategory category
        )
        {
            if (contentBudgets == null)
            {
                return null;
            }

            return contentBudgets.FirstOrDefault(
                budget => budget != null && budget.Category == category
            );
        }

        private bool TrySpawnMarker(
            SeededSpawnMarker2D marker,
            StageSeedContext context
        )
        {
            System.Random random = context.CreateRandom(
                $"{marker.StreamName}|{marker.DeterministicKey}|Prefab"
            );

            if (!marker.TrySelectPrefab(random, out GameObject prefab))
            {
                Debug.LogWarning(
                    "[Island Generation] Marker has no valid prefab: " +
                    marker.name,
                    marker
                );
                return false;
            }

            if (prefab.GetComponent<NetworkObject>() == null)
            {
                Debug.LogError(
                    "[Island Generation] Prefab must have a NetworkObject " +
                    $"on its root: {prefab.name}",
                    prefab
                );
                return false;
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
                Destroy(spawnedGameObject);
                return false;
            }

            spawnedNetworkObject.Spawn(true);
            spawnedObjects.Add(spawnedNetworkObject);

            if (logEachSpawn)
            {
                Debug.Log(
                    "[Island Generation] Spawned " +
                    $"'{prefab.name}' at marker '{marker.name}'.",
                    spawnedGameObject
                );
            }

            return true;
        }

        private static void Shuffle<T>(
            IList<T> values,
            System.Random random
        )
        {
            for (int index = values.Count - 1; index > 0; index--)
            {
                int swapIndex = random.Next(0, index + 1);
                (values[index], values[swapIndex]) =
                    (values[swapIndex], values[index]);
            }
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
