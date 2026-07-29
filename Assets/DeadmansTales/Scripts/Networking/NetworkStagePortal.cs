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

        // Matches KrakenArenaVictoryPortal's VisualChildName -- when
        // requireKrakenDefeated is set, the portal's visual (and its
        // interaction collider) start hidden and are revealed once the
        // kraken is gone, so the boss-arena portal does not "spawn in"
        // mid-fight. Portals without that flag (ordinary stage-to-stage
        // portals) are unaffected and reveal immediately on Awake.
        private const string GatedVisualChildName = "PortalVisual";
        private const float ArenaGateRefreshSeconds = 0.25f;

        private bool sceneLoadRequested;
        private int cachedRemainingEnemies;
        private int cachedRemainingEnemyShips;
        private float nextEnemyCountRefreshTime;

        private GameObject gatedVisual;
        private BoxCollider2D interactionCollider;
        private bool arenaGateRevealed;
        private float nextArenaGateCheckTime;

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

                return completesRun
                    ? "Press E to Free Your Soul!"
                    : "PRESS E TO CONTINUE VOYAGE";
            }
        }

        /// <summary>
        /// True only for the arena's boss portal (requireKrakenDefeated).
        /// Deliberately narrower than every requireX flag: an ordinary stage
        /// portal like PostOceanIslandPortal sets requireAllEnemyShipsDefeated
        /// without requireKrakenDefeated, and relies on being visible the
        /// whole time so its "Sink All Enemy Ships (N Remaining)" prompt can
        /// actually guide the player -- hiding it too would silently swallow
        /// that message. Only the kraken gate implies "this portal has no
        /// business existing yet."
        /// </summary>
        private bool HasArenaGate => requireKrakenDefeated;

        /// <summary>
        /// Whether the kraken gate above is satisfied, reused here to decide
        /// when the portal's visual and collider should reveal.
        /// </summary>
        private bool AllArenaGatesCleared()
        {
            return !requireKrakenDefeated ||
                FindFirstObjectByType<KrakenHealth>() == null;
        }

        private void Awake()
        {
            interactionCollider = GetComponent<BoxCollider2D>();

            Transform visual = transform.Find(GatedVisualChildName);
            gatedVisual = visual != null ? visual.gameObject : null;

            // Reveal immediately unless this portal actually has something to
            // wait for -- covers ordinary stage portals with no requireX
            // flags set, which should look exactly as before.
            SetPortalRevealed(!HasArenaGate || AllArenaGatesCleared());
        }

        private void Update()
        {
            if (arenaGateRevealed || !HasArenaGate)
            {
                return;
            }

            if (Time.unscaledTime < nextArenaGateCheckTime)
            {
                return;
            }

            nextArenaGateCheckTime = Time.unscaledTime + ArenaGateRefreshSeconds;

            if (AllArenaGatesCleared())
            {
                SetPortalRevealed(true);
            }
        }

        /// <summary>
        /// Hides (or shows) the portal's own visual child and its interaction
        /// collider. The collider doubles as what NetworkInteractionInput2D's
        /// overlap query finds, so disabling it also keeps a not-yet-revealed
        /// arena portal from being targeted or prompting at all -- not just
        /// invisible.
        /// </summary>
        private void SetPortalRevealed(bool revealed)
        {
            arenaGateRevealed = revealed;

            if (gatedVisual != null)
            {
                gatedVisual.SetActive(revealed);
            }

            if (interactionCollider != null)
            {
                interactionCollider.enabled = revealed;
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

            return !requireAllEnemiesDefeated || CountRemainingEnemies() == 0;
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
