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

        [Tooltip(
            "Blocks passage until every KrakenHealth in the scene is gone " +
            "(defeated and despawned -- KrakenHealth.TakeHitServer despawns " +
            "it on death). This is the boss-arena's final portal; ordinary " +
            "stage portals between islands leave this off."
        )]
        [SerializeField]
        private bool requireKrakenDefeated;

        [SerializeField]
        private bool advanceStage = true;

        [Tooltip(
            "The final portal after the boss: interacting sets " +
            "NetworkRunState.Status to Completed instead of loading " +
            "destinationSceneName -- there is no next stage after a win. " +
            "No win screen exists yet ('eventually'); this NetworkRunStatus " +
            "is the hook a future WinScreenUI can react to, the same way " +
            "SinglePlayerDeathScreenUI reacts to Failed. destinationSceneName " +
            "and Advance Stage are ignored when this is on."
        )]
        [SerializeField]
        private bool completesRun;

        /// <summary>
        /// Raised locally on every peer the moment a Completes Run portal is
        /// used, so end-of-run UI does not have to poll. Subscribers must
        /// unsubscribe when they are destroyed -- this is static and outlives
        /// any single scene.
        /// </summary>
        public static event System.Action RunCompleted;

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

        /// <summary>
        /// The run is over once the crew's own ship goes down --
        /// NetworkShipHealth sets the run Failed. Nothing was consulting
        /// that, so a sunk ship still let anyone press E on the door and
        /// carry on. Checked against the shared run state so it holds for
        /// every portal in every scene, not just the one on the boat.
        /// </summary>
        private static bool RunIsLost
        {
            get
            {
                NetworkRunState runState = NetworkRunState.Instance;

                return
                    runState != null &&
                    runState.IsSpawned &&
                    runState.RunStatus == NetworkRunStatus.Failed;
            }
        }

        public override string InteractionPrompt
        {
            get
            {
                if (sceneLoadRequested)
                {
                    return "Loading Next Stage...";
                }

                if (RunIsLost)
                {
                    return "THE SHIP IS LOST";
                }

                if (requireKrakenDefeated && FindFirstObjectByType<KrakenHealth>() != null)
                {
                    return "Defeat the Kraken First";
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

                // The boat leg has to actually finish before any exit opens,
                // including the one that ends the run.
                if (Leg != null && !Leg.IsComplete)
                {
                    return "NOT THERE YET...";
                }

                return completesRun
                    ? "PRESS E TO CLAIM VICTORY"
                    : "PRESS E TO CONTINUE VOYAGE";
            }
        }

        protected override bool CanInteractServer(
            NetworkInteractionController2D interactor
        )
        {
            if (sceneLoadRequested || RunIsLost)
            {
                return false;
            }

            if (requireKrakenDefeated && FindFirstObjectByType<KrakenHealth>() != null)
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
            if (completesRun)
            {
                NetworkRunState completionRunState = NetworkRunState.Instance;
                if (completionRunState != null && completionRunState.IsSpawned)
                {
                    completionRunState.SetStatusServer(NetworkRunStatus.Completed);
                }

                // The win screen lives in the arena scene, so there is no
                // scene to load here -- every peer just needs to be told the
                // run is over. NetworkRunState.Status is the durable record
                // (a late joiner can still read Completed from it); this RPC
                // is the immediate nudge that BossDefeatedUI listens for.
                NotifyRunCompletedClientRpc();

                Debug.Log(
                    "[Stage Portal] Run completed through the victory portal.",
                    this
                );
                return;
            }

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

        [ClientRpc]
        private void NotifyRunCompletedClientRpc()
        {
            RunCompleted?.Invoke();
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
