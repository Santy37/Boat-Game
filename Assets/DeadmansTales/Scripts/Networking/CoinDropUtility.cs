using Unity.Netcode;
using UnityEngine;

namespace DeadmansTales.Networking
{
    /// <summary>
    /// Server-only helper for scattering loose <see cref="NetworkCoinPickup"/>
    /// instances. An enemy's kill drop and a hand-placed
    /// <see cref="NetworkCoinScatterSpawner"/> both go through this, so there
    /// is exactly one place that resolves which prefab "a coin" is and one
    /// place that decides how a handful of them spread out.
    ///
    /// The prefab is never referenced directly by field: enemies come in
    /// several prefab families (BloodFiend, Crab, DemonReaver, OrcBrute,
    /// RobedSkeleton, SkeletonWarrior...), and wiring a coin prefab into
    /// every one of them by hand is exactly the kind of thing that gets
    /// missed when a new enemy is added. Instead this reads the same
    /// registered prefab list the network bootstrap already uses
    /// (<see cref="DeadmansNetworkBootstrapSettings.AdditionalNetworkPrefabs"/>),
    /// and picks out whichever entry carries a NetworkCoinPickup — the exact
    /// asset the shop island already scatters and registers as a spawnable
    /// network prefab.
    /// </summary>
    public static class CoinDropUtility
    {
        private const string SettingsResourcePath =
            "Networking/DeadmansNetworkBootstrapSettings";

        /// <summary>
        /// Weighted 1-5 roll for a kill's drop count. 2 is the common case;
        /// 1 and 5 are the rare tails, tied at the bottom on purpose so
        /// neither end reads as "the real minimum/maximum" more than the
        /// other. 3 and 4 taper down from the peak toward the 5 tail.
        /// </summary>
        private static readonly int[] DropCountWeights = { 10, 40, 25, 15, 10 };

        private static GameObject cachedCoinPrefab;

        /// <summary>Rolls how many coins a single kill should drop (1-5).</summary>
        public static int RollDropCount()
        {
            int total = 0;
            foreach (int weight in DropCountWeights)
            {
                total += weight;
            }

            int roll = Random.Range(0, total);
            int cumulative = 0;

            for (int index = 0; index < DropCountWeights.Length; index++)
            {
                cumulative += DropCountWeights[index];
                if (roll < cumulative)
                {
                    return index + 1;
                }
            }

            return 2;
        }

        /// <summary>
        /// Spawns <paramref name="count"/> coins clustered around
        /// <paramref name="origin"/> (an enemy's death position, say), each
        /// nudged apart so a multi-coin drop doesn't stack invisibly on one
        /// point.
        /// </summary>
        public static void SpawnScattered(
            Vector3 origin,
            int count,
            float scatterRadius = 0.6f
        )
        {
            if (count <= 0 || !IsServerReady())
            {
                return;
            }

            GameObject prefab = ResolveCoinPrefab();

            if (prefab == null)
            {
                Debug.LogWarning(
                    "[Coins] No NetworkCoinPickup prefab is registered in " +
                    "the bootstrap settings' AdditionalNetworkPrefabs; " +
                    "nothing was dropped."
                );
                return;
            }

            for (int index = 0; index < count; index++)
            {
                Vector2 offset = count == 1
                    ? Vector2.zero
                    : Random.insideUnitCircle * scatterRadius;

                SpawnCoin(prefab, origin + (Vector3)offset);
            }
        }

        /// <summary>
        /// Spawns a single coin at an exact position, with no scatter
        /// jitter — what a random-scatter-across-the-map spawner wants,
        /// since it already picks its own random points.
        /// </summary>
        public static void SpawnAt(Vector3 position)
        {
            if (!IsServerReady())
            {
                return;
            }

            GameObject prefab = ResolveCoinPrefab();

            if (prefab == null)
            {
                Debug.LogWarning(
                    "[Coins] No NetworkCoinPickup prefab is registered in " +
                    "the bootstrap settings' AdditionalNetworkPrefabs; " +
                    "nothing was dropped."
                );
                return;
            }

            SpawnCoin(prefab, position);
        }

        private static void SpawnCoin(GameObject prefab, Vector3 position)
        {
            GameObject instance = Object.Instantiate(
                prefab,
                position,
                Quaternion.identity
            );

            NetworkObject networkObject = instance.GetComponent<NetworkObject>();

            if (networkObject == null)
            {
                Debug.LogError(
                    "[Coins] The registered coin prefab has no " +
                    "NetworkObject; destroying the stray instance.",
                    instance
                );
                Object.Destroy(instance);
                return;
            }

            networkObject.Spawn(true);
        }

        private static bool IsServerReady()
        {
            return NetworkManager.Singleton != null &&
                NetworkManager.Singleton.IsListening &&
                NetworkManager.Singleton.IsServer;
        }

        private static GameObject ResolveCoinPrefab()
        {
            if (cachedCoinPrefab != null)
            {
                return cachedCoinPrefab;
            }

            DeadmansNetworkBootstrapSettings settings =
                Resources.Load<DeadmansNetworkBootstrapSettings>(
                    SettingsResourcePath
                );

            if (settings == null)
            {
                return null;
            }

            foreach (GameObject candidate in settings.AdditionalNetworkPrefabs)
            {
                if (
                    candidate != null &&
                    candidate.GetComponent<NetworkCoinPickup>() != null
                )
                {
                    cachedCoinPrefab = candidate;
                    return cachedCoinPrefab;
                }
            }

            return null;
        }
    }
}
