using DeadmansTales.Ship;
using DeadmansTales.WorldGeneration;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeadmansTales.Networking
{
    /// <summary>
    /// Server-authoritative synchronized exit between gameplay stages.
    /// </summary>
    public sealed class NetworkStagePortal : NetworkInteractable2D
    {
        [SerializeField]
        private string destinationSceneName = "Island_After_Ocean_01_2D";

        [SerializeField]
        private bool requireGenerationComplete;

        [SerializeField]
        private bool requireAllEnemiesDefeated;

        [Tooltip(
            "Blocks passage until every EnemyShip in the scene has been " +
            "sunk/despawned (see EnemyShipCleanup). Independent of " +
            "Require All Enemies Defeated, which only counts individual " +
            "Enemy (pirate) instances, not ships."
        )]
        [SerializeField]
        private bool requireAllEnemyShipsDefeated;

        [SerializeField]
        private bool advanceStage = true;

        private const float EnemyCountRefreshSeconds = 0.25f;

        private bool sceneLoadRequested;
        private int cachedRemainingEnemies;
        private int cachedRemainingEnemyShips;
        private float nextEnemyCountRefreshTime;

        /// <summary>
        /// Cached enemy count for UI prompts. OnGUI queries this several
        /// times per frame, so the scene scan is throttled. Authoritative
        /// checks use <see cref="CountRemainingEnemies"/> directly.
        /// </summary>
        public int RemainingEnemies
        {
            get
            {
                RefreshCountsIfStale();
                return cachedRemainingEnemies;
            }
        }

        /// <summary>Same throttled-cache pattern as RemainingEnemies.</summary>
        public int RemainingEnemyShips
        {
            get
            {
                RefreshCountsIfStale();
                return cachedRemainingEnemyShips;
            }
        }

        private void RefreshCountsIfStale()
        {
            if (Time.unscaledTime < nextEnemyCountRefreshTime)
            {
                return;
            }

            nextEnemyCountRefreshTime =
                Time.unscaledTime + EnemyCountRefreshSeconds;
            cachedRemainingEnemies = CountRemainingEnemies();
            cachedRemainingEnemyShips = CountRemainingEnemyShips();
        }

        public override string InteractionPrompt
        {
            get
            {
                if (sceneLoadRequested)
                {
                    return "Loading Next Stage...";
                }

                if (requireAllEnemyShipsDefeated && RemainingEnemyShips > 0)
                {
                    return $"Sink All Enemy Ships ({RemainingEnemyShips} Remaining)";
                }

                int remaining = RemainingEnemies;
                if (requireAllEnemiesDefeated && remaining > 0)
                {
                    return $"Defeat All Enemies ({remaining} Remaining)";
                }

                if (Leg != null && !Leg.IsComplete)
                {
                    return "Not there yet...";
                }

                return "Press E to Continue the Voyage";
            }
        }

        protected override bool CanInteractServer(
            NetworkInteractionController2D interactor
        )
        {
            if (sceneLoadRequested)
            {
                return false;
            }

            if (requireGenerationComplete)
            {
                SeededIslandContentGenerator generator =
                    FindFirstObjectByType<SeededIslandContentGenerator>();

                if (generator == null || !generator.GenerationComplete)
                {
                    return false;
                }
            }

            if (requireAllEnemyShipsDefeated && CountRemainingEnemyShips() > 0)
            {
                return false;
            }

            // If this scene has a boat-leg bar, block until it is complete.
            if (Leg != null && !Leg.IsComplete)
            {
                return false;
            }

            return !requireAllEnemiesDefeated || CountRemainingEnemies() == 0;
        }

        private BoatLegProgress boatLeg;
        private bool boatLegSearched;

        private BoatLegProgress Leg
        {
            get
            {
                if (!boatLegSearched)
                {
                    boatLeg = FindFirstObjectByType<BoatLegProgress>();
                    boatLegSearched = true;
                }
                return boatLeg;
            }
        }

        protected override void PerformInteractionServer(
            NetworkInteractionController2D interactor
        )
        {
            NetworkManager manager = NetworkManager.Singleton;

            if (
                manager == null ||
                !manager.IsListening ||
                !manager.IsServer ||
                string.IsNullOrWhiteSpace(destinationSceneName) ||
                !Application.CanStreamedLevelBeLoaded(destinationSceneName)
            )
            {
                Debug.LogError(
                    $"[Stage Portal] Destination is not loadable: " +
                    $"'{destinationSceneName}'.",
                    this
                );
                return;
            }

            // NGO destroys spawned objects from the current scene inside a
            // Single-scene LoadScene call, before LoadScene returns. Record
            // all portal-local state first and do not access this NetworkObject
            // after the call succeeds.
            NetworkRunState runState = NetworkRunState.Instance;
            bool shouldAdvanceStage = advanceStage;
            sceneLoadRequested = true;
            SceneEventProgressStatus status = manager.SceneManager.LoadScene(
                destinationSceneName,
                LoadSceneMode.Single
            );

            if (status == SceneEventProgressStatus.Started)
            {
                if (runState != null && runState.IsSpawned)
                {
                    if (shouldAdvanceStage)
                    {
                        runState.AdvanceStageServer();
                    }

                    runState.SetStatusServer(NetworkRunStatus.Loading);
                }
            }
            else
            {
                sceneLoadRequested = false;

                // A rejected load leaves this scene and portal alive. Restore
                // the one-shot interaction that the base class disabled before
                // entering this callback.
                if (IsSpawned && IsServer)
                {
                    SetInteractionEnabledServer(true);
                }

                Debug.LogError(
                    $"[Stage Portal] NGO rejected the scene load: {status}.",
                    this
                );
            }
        }

        private static int CountRemainingEnemies()
        {
            Enemy[] enemies = FindObjectsByType<Enemy>(
                FindObjectsSortMode.None
            );

            int remaining = 0;
            foreach (Enemy enemy in enemies)
            {
                if (enemy != null && enemy.IsAlive)
                {
                    remaining++;
                }
            }

            return remaining;
        }

        /// <summary>
        /// An EnemyShip's whole NetworkObject is despawned/destroyed by
        /// EnemyShipCleanup once it's dealt with (sunk or crew wiped), so
        /// simply counting how many EnemyShipApproach instances still exist
        /// in the scene is enough -- there's nothing left to check once a
        /// ship is actually gone.
        /// </summary>
        private static int CountRemainingEnemyShips()
        {
            return FindObjectsByType<EnemyShipApproach>(
                FindObjectsSortMode.None
            ).Length;
        }
    }
}
