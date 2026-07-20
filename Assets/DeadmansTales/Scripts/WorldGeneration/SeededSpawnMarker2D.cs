using System;
using System.Text;
using UnityEngine;

namespace DeadmansTales.WorldGeneration
{
    /// <summary>
    /// Designer-placed candidate location for seeded island content.
    ///
    /// The marker does not contain enemy, loot, healing, or reward behavior.
    /// It only describes which network prefabs may appear at this location.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SeededSpawnMarker2D : MonoBehaviour
    {
        [Header("Content")]
        [SerializeField]
        private SeededContentCategory category = SeededContentCategory.Prop;

        [SerializeField]
        [Tooltip(
            "Every prefab must have a NetworkObject on its root and be " +
            "registered in the NetworkManager prefab list."
        )]
        private GameObject[] networkPrefabs;

        [Header("Spawn Rules")]
        [SerializeField]
        private bool alwaysSpawn;

        [SerializeField]
        [Range(0f, 1f)]
        private float spawnChance = 0.5f;

        [SerializeField]
        [Min(1)]
        private int minimumStage = 1;

        [SerializeField]
        [Tooltip("Zero means there is no maximum stage.")]
        [Min(0)]
        private int maximumStage;

        [Header("Placement")]
        [SerializeField]
        private Vector3 positionOffset;

        [SerializeField]
        private bool useMarkerRotation = true;

        public SeededContentCategory Category => category;

        public bool AlwaysSpawn => alwaysSpawn;

        public float SpawnChance => Mathf.Clamp01(spawnChance);

        public string StreamName => $"IslandContent.{category}";

        public string DeterministicKey => BuildDeterministicKey();

        public Vector3 SpawnPosition => transform.position + positionOffset;

        public Quaternion SpawnRotation =>
            useMarkerRotation ? transform.rotation : Quaternion.identity;

        public bool IsEligibleForStage(int stageIndex)
        {
            if (stageIndex < minimumStage)
            {
                return false;
            }

            return maximumStage == 0 || stageIndex <= maximumStage;
        }

        public bool ShouldSpawn(System.Random random)
        {
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            return alwaysSpawn || random.NextDouble() <= spawnChance;
        }

        public bool TrySelectPrefab(
            System.Random random,
            out GameObject selectedPrefab
        )
        {
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            selectedPrefab = null;

            if (networkPrefabs == null || networkPrefabs.Length == 0)
            {
                return false;
            }

            int validPrefabCount = 0;

            foreach (GameObject prefab in networkPrefabs)
            {
                if (prefab != null)
                {
                    validPrefabCount++;
                }
            }

            if (validPrefabCount == 0)
            {
                return false;
            }

            int selectedValidIndex = random.Next(0, validPrefabCount);
            int currentValidIndex = 0;

            foreach (GameObject prefab in networkPrefabs)
            {
                if (prefab == null)
                {
                    continue;
                }

                if (currentValidIndex == selectedValidIndex)
                {
                    selectedPrefab = prefab;
                    return true;
                }

                currentValidIndex++;
            }

            return false;
        }

        private void OnValidate()
        {
            spawnChance = Mathf.Clamp01(spawnChance);
            minimumStage = Mathf.Max(1, minimumStage);
            maximumStage = Mathf.Max(0, maximumStage);

            if (maximumStage > 0 && maximumStage < minimumStage)
            {
                maximumStage = minimumStage;
            }
        }

        private string BuildDeterministicKey()
        {
            StringBuilder hierarchyPath = new StringBuilder();
            Transform current = transform;

            while (current != null)
            {
                hierarchyPath.Insert(
                    0,
                    $"{current.GetSiblingIndex():D4}:{current.name}/"
                );

                current = current.parent;
            }

            string sceneIdentity = string.IsNullOrWhiteSpace(gameObject.scene.path)
                ? gameObject.scene.name
                : gameObject.scene.path;

            return
                $"{sceneIdentity}|{hierarchyPath}|{category}";
        }
    }
}
