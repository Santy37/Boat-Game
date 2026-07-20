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

        [SerializeField]
        private bool advanceStage = true;

        private const float EnemyCountRefreshSeconds = 0.25f;

        private bool sceneLoadRequested;
        private int cachedRemainingEnemies;
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
                if (Time.unscaledTime >= nextEnemyCountRefreshTime)
                {
                    nextEnemyCountRefreshTime =
                        Time.unscaledTime + EnemyCountRefreshSeconds;
                    cachedRemainingEnemies = CountRemainingEnemies();
                }

                return cachedRemainingEnemies;
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

                int remaining = RemainingEnemies;
                if (requireAllEnemiesDefeated && remaining > 0)
                {
                    return $"Defeat All Enemies ({remaining} Remaining)";
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

            return !requireAllEnemiesDefeated || CountRemainingEnemies() == 0;
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
    }
}
