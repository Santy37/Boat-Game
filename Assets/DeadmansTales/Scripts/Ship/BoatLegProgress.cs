using System.Collections.Generic;
using DeadmansTales.Networking;
using UnityEngine;

/// <summary>
/// Owns the boat leg's progress bar: fills over time, positions the line /
/// islands / ship, and spawns event icons on the line. Landing is handled
/// elsewhere (e.g. the stage portal) - call <see cref="LandOnIsland"/>.
///
/// In multiplayer the SERVER owns the leg. It advances progress, decides when
/// an event is really over (its spawner has nothing left alive), and publishes
/// both to <see cref="NetworkRunState"/>; clients render what they are told.
/// Every peer still builds its own copy of the icons, but from the run's shared
/// seed, so the layout is identical everywhere. Before this, each peer ran the
/// whole leg independently: layouts differed, and a client waited out a fixed
/// three-second pause per event instead of the real fight, so it sailed on and
/// announced the leg finished while the host was still mid-battle.
///
/// Runs after the default execution order so its LateUpdate lands AFTER the
/// camera has moved for the frame. The bar is parented to the camera, so
/// positioning it from a camera position that is about to change means the
/// camera's own movement gets applied to it a second time -- which is what
/// slid the bar off to one side once the camera started following a player.
/// </summary>
[DefaultExecutionOrder(200)]
public class BoatLegProgress : MonoBehaviour
{
    [Header("Progress")]
    [Tooltip("0 = leg just started, 1 = leg complete and the button appears.")]
    [SerializeField, Range(0f, 1f)] private float progress01;

    [Header("Progress Bar - Screen Position")]
    [Tooltip("Where the whole bar sits on screen: (0.5, 0.9) = top-centre. " +
             "x: 0=left..1=right,  y: 0=bottom..1=top.")]
    [SerializeField] private Vector2 screenPosition = new Vector2(0.5f, 0.9f);
    [Tooltip("Distance in front of the camera the bar is drawn.")]
    [SerializeField] private float distanceFromCamera = 10f;
    [Tooltip("Leave empty to use the Main Camera.")]
    [SerializeField] private Camera targetCamera;

    [Header("Progress Bar - When Manning (helm/cannon zoom)")]
    [Tooltip("Screen spot the bar moves to while a station is manned.")]
    [SerializeField] private Vector2 manningScreenPosition = new Vector2(0.32f, 0.42f);
    [Tooltip("Scale multiplier while a station is manned (2 = twice as big).")]
    [SerializeField] private float manningScale = 2f;

    [Header("Progress Bar - Pieces (children of this object)")]
    [Tooltip("START island - placed on the RIGHT (the ship starts here).")]
    [SerializeField] private Transform startIsland;
    [Tooltip("END island - placed on the LEFT (the destination).")]
    [SerializeField] private Transform endIsland;
    [Tooltip("The ship - slides from the right island to the left one.")]
    [SerializeField] private Transform ship;
    [Tooltip("RIGHT spot (start island / where the ship starts).")]
    [SerializeField] private Vector2 startSpot = new Vector2(4f, 0f);
    [Tooltip("LEFT spot (end island / destination).")]
    [SerializeField] private Vector2 endSpot = new Vector2(-4f, 0f);

    [Header("Progress Bar - Timing")]
    [Tooltip("Fill the bar automatically over time (this moves the ship).")]
    [SerializeField] private bool autoAdvance = true;
    [Tooltip("Seconds to cross from the start island to the end island.")]
    [SerializeField] private float legDuration = 60f;

    [Header("Progress Bar - Events Per Level")]
    [Tooltip("Number of events (rocks / pirate ships) that spawn on level 1.")]
    [SerializeField, Min(0)] private int level1Events = 1;
    [Tooltip("Number of events that spawn on level 2.")]
    [SerializeField, Min(0)] private int level2Events = 2;
    [Tooltip("Number of events that spawn on level 3.")]
    [SerializeField, Min(0)] private int level3Events = 3;
    [Tooltip(
        "Level used when the boat is entered with no menu selection (e.g. " +
        "playing this scene directly to test). 1-based.")]
    [SerializeField, Min(1)] private int defaultLevel = 1;

    [Header("Progress Bar - Events (spawn ON THE LINE)")]
    [Tooltip(
        "Chance each event is a PIRATE SHIP rather than an obstacle (0 = all " +
        "rocks, 1 = all ships).")]
    [SerializeField, Range(0f, 1f)] private float enemyShipChance = 0.6f;

    [Tooltip(
        "With room for two or more events, place at least one pirate ship "
        + "AND at least one obstacle before filling the rest by chance. Stops "
        + "a leg coming up all rocks or all ships. Turn off to roll every "
        + "event independently.")]
    [SerializeField] private bool guaranteeBothEventTypes = true;
    [Tooltip("Obstacle icon - hidden at start, cloned onto the line.")]
    [SerializeField] private Transform obstacleIcon;
    [Tooltip("Pirate ship icon - hidden at start, cloned onto the line.")]
    [SerializeField] private Transform pirateShipIcon;
    [Tooltip("Nudge event icons off the line, e.g. y = 6 to lift the rocks " +
             "up above it.")]
    [SerializeField] private Vector2 eventOffset = new Vector2(0f, 6f);
    [Tooltip("Events spawn no closer to the START island than this " +
             "(0 = start island, 1 = end island).")]
    [SerializeField, Range(0f, 1f)] private float eventMinFraction = 0.25f;
    [Tooltip("Events spawn no closer to the END island than this.")]
    [SerializeField, Range(0f, 1f)] private float eventMaxFraction = 0.9f;
    [Tooltip("Smallest gap allowed between two events along the line so they " +
             "never sit on top of each other (fraction of the whole line).")]
    [SerializeField, Range(0f, 0.5f)] private float eventMinSpacing = 0.12f;

    [Header("Progress Bar - Real Spawns (driven by the events above)")]
    [Tooltip("Fires when the ship reaches a PIRATE SHIP event. Leave empty " +
             "to only show the icon/message with no real spawn.")]
    [SerializeField] private NetworkEnemyShipSpawner2D enemyShipSpawner;
    [Tooltip("Fires when the ship reaches an OBSTACLE event. Leave empty to " +
             "only show the icon/message with no real spawn.")]
    [SerializeField] private BoatObstacleGenerator obstacleGenerator;

    [Header("Progress Bar - Event Pause")]
    [Tooltip("Ship pauses when it gets within this many units of an event.")]
    [SerializeField] private float eventTriggerRange = 2.5f;
    [Tooltip("Seconds the bar pauses at each event.")]
    [SerializeField] private float eventPauseDuration = 3f;
    [Tooltip("Message shown for OBSTACLE events.")]
    [SerializeField] private string obstacleMessage = "PROTECT THE SHIP";
    [Tooltip("Message shown for ENEMY SHIP events.")]
    [SerializeField] private string enemyMessage = "ATTACK THE ENEMIES";
    [Tooltip("Seconds the event message stays on screen. The bar still keeps " +
             "waiting for the event to clear even after the message hides.")]
    [SerializeField] private float messageDuration = 5f;

    [Header("Progress Bar - Arrival Message")]
    [Tooltip("Shown when the bar finishes (then the portal is usable).")]
    [SerializeField] private string arrivalMessage = "YOU HAVE ARRIVED";
    [Tooltip(
        "Seconds a freshly-raised centred message outranks every other prompt " +
        "so the player can't miss it, before it drops to the lowest priority.")]
    [SerializeField] private float messagePrioritySeconds = 5f;

    private int activeEvent = -1;
    private float eventEndTime;
    // When true, the active event just waits out eventPauseDuration (used on
    // clients, or when no spawner is wired). When false, it waits until the
    // triggered spawner reports IsResolving == false.
    private bool activeEventTimed;
    private string currentMessage = string.Empty;
    // Clock time at which the current event message hides (the bar keeps
    // waiting for the event even after this).
    private float messageHideTime;

    // The message currently pushed to the shared HUD, and the clock time until
    // which it holds top (banner) priority. A new message restarts the window.
    private string hudMessage;
    private float hudPriorityUntil;

    private bool sailed;
    private Vector3 normalScale = Vector3.one;
    private ShipHelm cachedHelm;
    private ShipCannon[] cachedCannons;
    private readonly List<Transform> events = new List<Transform>();
    private readonly List<float> eventFractions = new List<float>();
    private readonly List<bool> eventIsEnemy = new List<bool>();
    private readonly List<bool> eventDone = new List<bool>();

    // Icons are built once, and in multiplayer only once the run's shared seed
    // has arrived -- so every peer lays the same leg out. Until then there is
    // no leg to run yet.
    private bool eventsBuilt;

    // How many events this peer has finished. On a client this is compared
    // against the server's count to decide when an event is really over.
    private int eventsCompleted;

    private void Awake()
    {
        normalScale = transform.localScale;
    }

    private void Start()
    {
        // Hide the source icons so they NEVER show at their scene position.
        if (obstacleIcon != null)
        {
            obstacleIcon.gameObject.SetActive(false);
        }
        if (pirateShipIcon != null)
        {
            pirateShipIcon.gameObject.SetActive(false);
        }

        TryBuildEvents();
    }

    // ------------------------------------------------------- network roles

    /// <summary>
    /// The shared run state, but only once it is actually usable. Null in
    /// single-player / local co-op, which keeps the whole original local code
    /// path below intact for those modes.
    /// </summary>
    private static NetworkRunState Run
    {
        get
        {
            NetworkRunState run = NetworkRunState.Instance;
            return run != null && run.IsSpawned ? run : null;
        }
    }

    private static bool IsNetworkedLeg => Run != null;

    private static bool IsLegServer
    {
        get
        {
            NetworkRunState run = Run;
            return run != null && run.IsServer;
        }
    }

    private static bool IsLegClient
    {
        get
        {
            NetworkRunState run = Run;
            return run != null && !run.IsServer;
        }
    }

    // The level this leg runs at: the one chosen in the menu if there is one,
    // otherwise the inspector's Default Level (for testing the scene directly).
    private int ResolveLevel()
    {
        return BoatLevelSelection.PendingLevel > 0
            ? BoatLevelSelection.PendingLevel
            : Mathf.Max(1, defaultLevel);
    }

    // How many events that level spawns. Levels past 3 use the level 3 count.
    private int EventCountForLevel(int oneBasedLevel)
    {
        switch (oneBasedLevel)
        {
            case 1: return Mathf.Max(0, level1Events);
            case 2: return Mathf.Max(0, level2Events);
            default: return Mathf.Max(0, level3Events);
        }
    }

    /// <summary>
    /// Builds the event icons, if it can. In multiplayer this waits for the
    /// run's shared seed, so it is retried from Update until it succeeds.
    /// Returns true once the leg is laid out.
    /// </summary>
    private bool TryBuildEvents()
    {
        if (eventsBuilt)
        {
            return true;
        }

        // Single-player / local co-op: nothing to agree with anyone about, so
        // Unity's own Random is fine and the leg starts immediately.
        if (!IsNetworkedLeg)
        {
            SpawnEvents(ResolveLevel(), null);
            eventsBuilt = true;
            return true;
        }

        // Multiplayer: the layout MUST come from the run seed, or the host and
        // client fight different battles at different points on the same bar.
        BoatRunDirector director = BoatRunDirector.Instance;

        if (director == null || !director.IsRunReady)
        {
            return false;
        }

        NetworkRunState run = NetworkRunState.Instance;

        if (IsLegServer)
        {
            // Published BEFORE the layout is built, so a client that is
            // already waiting can start building the moment it hears the
            // level.
            run.BeginLegServer(ResolveLevel());
        }
        else if (run.LegLevel.Value <= 0)
        {
            // The server has not started the leg yet. Building now would use
            // this client's own (unset) level and lay out a different number
            // of fights.
            return false;
        }

        SpawnEvents(
            run.LegLevel.Value,
            director.CreateRandom(RandomStreamName)
        );

        eventsBuilt = true;
        return true;
    }

    private const string RandomStreamName = "BoatLegEvents";

    // Random source for the leg layout: the run's seeded stream in
    // multiplayer, or Unity's global Random in single-player. Wrapped so
    // SpawnEvents below reads identically either way.
    private static float NextFloat(System.Random rng, float min, float max)
    {
        return rng == null
            ? Random.Range(min, max)
            : min + (float)rng.NextDouble() * (max - min);
    }

    private static int NextInt(System.Random rng, int minInclusive, int maxExclusive)
    {
        return rng == null
            ? Random.Range(minInclusive, maxExclusive)
            : rng.Next(minInclusive, maxExclusive);
    }

    private void SpawnEvents(int level, System.Random rng)
    {
        // Place exactly this level's event count, each independently a pirate
        // ship or an obstacle per Enemy Ship Chance.
        int count = EventCountForLevel(level);

        List<Transform> chosen = new List<Transform>();

        // Rolling each event independently meant a whole leg could come up
        // all rocks or all ships -- at three events and an even chance that
        // is one leg in eight each way, which is exactly often enough to feel
        // broken ("we just get rocks, no ships"). With room for both, one of
        // each is placed up front and only the remainder is left to chance.
        int remaining = count;

        if (guaranteeBothEventTypes &&
            count >= 2 &&
            pirateShipIcon != null &&
            obstacleIcon != null)
        {
            chosen.Add(pirateShipIcon);
            chosen.Add(obstacleIcon);
            remaining -= 2;
        }

        for (int i = 0; i < remaining; i++)
        {
            chosen.Add(NextFloat(rng, 0f, 1f) < enemyShipChance
                ? pirateShipIcon
                : obstacleIcon);
        }

        // Shuffle, or the guaranteed ship would always be the first thing the
        // leg throws at you and the guaranteed rock always the second.
        for (int i = chosen.Count - 1; i > 0; i--)
        {
            int swap = NextInt(rng, 0, i + 1);
            (chosen[i], chosen[swap]) = (chosen[swap], chosen[i]);
        }

        for (int i = 0; i < chosen.Count; i++)
        {
            Transform template = chosen[i];
            if (template == null)
            {
                continue;
            }

            // Clone the hidden template, turn it on, and parent it to the bar so
            // its LOCAL position lands on the line between the islands.
            GameObject clone = Instantiate(template.gameObject, transform);
            clone.SetActive(true);
            events.Add(clone.transform);
            // Random spot on the line, kept away from the start island and
            // spaced apart from the events already placed.
            eventFractions.Add(PickSpacedFraction(rng));
            eventIsEnemy.Add(template == pirateShipIcon);
            eventDone.Add(false);
        }
    }

    // Picks a fraction along the line that is at least eventMinSpacing away
    // from every event placed so far, so icons never overlap. Tries a number
    // of random spots; if the line is too crowded to satisfy the spacing it
    // falls back to the least-crowded spot it found rather than looping forever.
    private float PickSpacedFraction(System.Random rng)
    {
        const int attempts = 30;
        float best = NextFloat(rng, eventMinFraction, eventMaxFraction);
        float bestGap = -1f;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            float candidate = NextFloat(rng, eventMinFraction, eventMaxFraction);

            // Distance to the nearest already-placed event.
            float nearest = float.MaxValue;
            for (int i = 0; i < eventFractions.Count; i++)
            {
                nearest = Mathf.Min(
                    nearest, Mathf.Abs(candidate - eventFractions[i]));
            }

            // First event, or far enough from all the others: accept it.
            if (eventFractions.Count == 0 || nearest >= eventMinSpacing)
            {
                return candidate;
            }

            // Otherwise remember the roomiest spot as a fallback.
            if (nearest > bestGap)
            {
                bestGap = nearest;
                best = candidate;
            }
        }

        return best;
    }

    private void Update()
    {
        // In multiplayer the leg cannot start until the shared seed has landed
        // and the icons are laid out from it.
        if (!TryBuildEvents())
        {
            return;
        }

        // A client renders the server's leg: progress and "is the fight over"
        // both come down the wire.
        if (IsLegClient)
        {
            UpdateAsClient();
            return;
        }

        UpdateAsAuthority();

        // Hand this frame's authoritative state to the clients.
        if (IsLegServer)
        {
            NetworkRunState.Instance.PublishLegProgressServer(
                progress01,
                eventsCompleted
            );
        }
    }

    /// <summary>
    /// Single-player, local co-op, and the host all run the real leg: they own
    /// progress and they wait for the actual spawned enemies/rocks to be gone.
    /// This is the original behaviour, unchanged.
    /// </summary>
    private void UpdateAsAuthority()
    {
        // Paused at an event: hold the bar until the event is resolved.
        if (activeEvent >= 0)
        {
            // Hide the message after its duration, but keep waiting.
            if (currentMessage.Length > 0 && Time.time >= messageHideTime)
            {
                currentMessage = string.Empty;
            }

            if (IsActiveEventResolved())
            {
                CompleteActiveEvent();
            }
            return;
        }

        // Ship reached an event? -> pause, show its message, and spawn.
        int reached = FindReachedEvent();

        if (reached >= 0)
        {
            activeEvent = reached;
            currentMessage = eventIsEnemy[reached] ? enemyMessage : obstacleMessage;
            messageHideTime = Time.time + messageDuration;

            // Tell the matching spawner to spawn its stuff. This runs once
            // per event (we return while paused). The spawners are
            // server-guarded, so it is safe for every peer to call.
            bool spawnerWired;
            if (eventIsEnemy[reached])
            {
                spawnerWired = enemyShipSpawner != null;
                if (spawnerWired)
                {
                    enemyShipSpawner.Trigger();
                }
            }
            else
            {
                spawnerWired = obstacleGenerator != null;
                if (spawnerWired)
                {
                    obstacleGenerator.Trigger();
                }
            }

            // Only the networked server actually spawns anything, so only the
            // server can wait for it to be cleared. Everything else -- an
            // unwired event (the kraken arena's bar has no spawners) or a
            // local/offline session, where the server-guarded spawners do
            // nothing -- waits out a fixed pause instead of hanging forever
            // on a fight that was never going to happen.
            activeEventTimed = !(IsLegServer && spawnerWired);
            eventEndTime = Time.time + eventPauseDuration;
            return;
        }

        // Otherwise advance the bar.
        if (autoAdvance && !sailed && legDuration > 0f && progress01 < 1f)
        {
            progress01 = Mathf.Clamp01(progress01 + Time.deltaTime / legDuration);
        }
    }

    /// <summary>
    /// A client's bar is a view of the server's leg. Progress is replicated
    /// rather than advanced locally, and an event ends when the SERVER says it
    /// ended -- never on a local timer, which is what used to let a client
    /// skip a fight it could not see and declare the leg over.
    /// </summary>
    private void UpdateAsClient()
    {
        NetworkRunState run = NetworkRunState.Instance;

        progress01 = Mathf.Clamp01(run.LegProgress.Value);

        int serverCompleted = run.LegEventsCompleted.Value;

        // The server has finished more events than this bar has cleared, so
        // clear them. Normally that is the one event currently showing; the
        // loop also covers a client that joined mid-leg or lagged badly enough
        // to never register an event it has already sailed past.
        while (eventsCompleted < serverCompleted)
        {
            if (activeEvent >= 0)
            {
                CompleteActiveEvent();
                continue;
            }

            int next = FindNextPendingEvent();

            if (next < 0)
            {
                // Nothing left locally to reconcile against, so stop rather
                // than spinning.
                eventsCompleted = serverCompleted;
                break;
            }

            activeEvent = next;
            CompleteActiveEvent();
        }

        // Show the event message while the server holds the bar at one. The
        // client detects this locally off the replicated progress; the layout
        // is seeded identically, so it lands on the same event the host is
        // actually fighting.
        if (activeEvent < 0)
        {
            int reached = FindReachedEvent();

            if (reached >= 0)
            {
                activeEvent = reached;
                currentMessage =
                    eventIsEnemy[reached] ? enemyMessage : obstacleMessage;
                messageHideTime = Time.time + messageDuration;
            }
        }
        else if (currentMessage.Length > 0 && Time.time >= messageHideTime)
        {
            currentMessage = string.Empty;
        }
    }

    // The first not-yet-done event the ship icon is currently close enough to
    // trigger, or -1.
    private int FindReachedEvent()
    {
        Vector2 shipPos = Vector2.Lerp(startSpot, endSpot, progress01);

        for (int i = 0; i < events.Count; i++)
        {
            if (eventDone[i])
            {
                continue;
            }

            Vector2 evPos = Vector2.Lerp(startSpot, endSpot, eventFractions[i]);

            if (Vector2.Distance(shipPos, evPos) <= eventTriggerRange)
            {
                return i;
            }
        }

        return -1;
    }

    // The pending event earliest along the line. Events are fought in the order
    // the ship sails past them, not in list order (the list is shuffled), so
    // reconciling against the server's count has to pick by position.
    private int FindNextPendingEvent()
    {
        int best = -1;

        for (int i = 0; i < events.Count; i++)
        {
            if (eventDone[i])
            {
                continue;
            }

            if (best < 0 || eventFractions[i] < eventFractions[best])
            {
                best = i;
            }
        }

        return best;
    }

    // Has the current event finished? Timed events wait out the pause; spawn
    // events wait until their spawner has cleared everything it spawned.
    private bool IsActiveEventResolved()
    {
        if (activeEventTimed)
        {
            return Time.time >= eventEndTime;
        }

        bool stillResolving = eventIsEnemy[activeEvent]
            ? enemyShipSpawner != null && enemyShipSpawner.IsResolving
            : obstacleGenerator != null && obstacleGenerator.IsResolving;

        return !stillResolving;
    }

    // Finish the active event: take its icon off the line and let the bar move.
    private void CompleteActiveEvent()
    {
        int i = activeEvent;
        eventDone[i] = true;
        eventsCompleted++;

        if (events[i] != null)
        {
            Destroy(events[i].gameObject);
            events[i] = null;
        }

        activeEvent = -1;
        currentMessage = string.Empty;
    }

    /// <summary>
    /// True while this machine's player is manning the helm or a cannon, which
    /// moves and enlarges the bar to suit the zoomed-out station view.
    ///
    /// Asks the stations themselves rather than reading
    /// LocalCoopCamera.HasZoomOverride as it used to. That flag lives on the
    /// local split-screen camera rig, which is switched OFF in the networked
    /// boat scene (the networked camera is Camera2DFollow) -- so in multiplayer
    /// it reported whatever the disabled rig happened to hold and the bar
    /// stopped agreeing with what the player was actually doing.
    /// </summary>
    private bool IsManning()
    {
        if (cachedHelm == null)
        {
            cachedHelm = FindFirstObjectByType<ShipHelm>();
        }

        if (cachedHelm != null && cachedHelm.IsManned)
        {
            return true;
        }

        // Cannons are many and are never created at runtime in these scenes,
        // so one lookup is enough.
        if (cachedCannons == null || cachedCannons.Length == 0)
        {
            cachedCannons =
                FindObjectsByType<ShipCannon>(FindObjectsSortMode.None);
        }

        foreach (ShipCannon cannon in cachedCannons)
        {
            if (cannon != null && cannon.IsManned)
            {
                return true;
            }
        }

        return false;
    }

    // STEP 1: keep the whole bar pinned to the top-centre of the screen so the
    // line (and, later, the islands/ship) follow the camera.
    private void LateUpdate()
    {
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam != null)
        {
            // While a station (helm/cannon) is manned, move + scale up; back to
            // normal the moment you leave.
            bool manning = IsManning();
            Vector2 pos = manning ? manningScreenPosition : screenPosition;
            transform.position = cam.ViewportToWorldPoint(new Vector3(
                pos.x, pos.y, distanceFromCamera));
            transform.localScale = manning ? normalScale * manningScale : normalScale;
        }

        // Islands on the ends; ship slides from the right (start) to the left.
        if (startIsland != null)
        {
            startIsland.localPosition = startSpot;
        }
        if (endIsland != null)
        {
            endIsland.localPosition = endSpot;
        }
        if (ship != null)
        {
            ship.localPosition = Vector2.Lerp(startSpot, endSpot, progress01);
        }

        // Events sit ON THE LINE between the islands (same Lerp as the ship),
        // forced here every frame so they can never stay at their scene spot.
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i] != null)
            {
                // Offset only obstacles (drop the rocks); leave the ship alone.
                Vector2 off = eventIsEnemy[i] ? Vector2.zero : eventOffset;
                events[i].localPosition =
                    Vector2.Lerp(startSpot, endSpot, eventFractions[i]) + off;
            }
        }

        UpdateMessageHud();
    }

    /// <summary>Current leg progress, 0 to 1.</summary>
    public float Progress01 => progress01;

    /// <summary>True once the leg is finished and the button is available.</summary>
    public bool IsComplete => progress01 >= 1f;

    /// <summary>
    /// Called by the progress bar / leg timer once it exists.
    /// </summary>
    public void SetProgress(float value01)
    {
        progress01 = Mathf.Clamp01(value01);
    }

    /// <summary>Fill the bar immediately (debug, or an instant-win pickup).</summary>
    public void CompleteLeg()
    {
        progress01 = 1f;
    }

    /// <summary>
    /// Ends the boat leg and moves the run on to the chosen island. Safe to
    /// call from a UI Button's OnClick as well as from the built-in button.
    /// </summary>
    public void LandOnIsland()
    {
        if (sailed)
        {
            return;
        }

        if (!RunContext.HasActive)
        {
            Debug.LogWarning(
                "[Boat Leg] No active run, so there is nowhere to sail to. " +
                "Start from the menu so a run manager exists.",
                this);
            return;
        }

        sailed = true;
        RunContext.Active.OnBoatArrived();
    }

    // The event message while paused, else "You have arrived" once the leg is
    // done. Routed through the shared pirate-themed panel so it matches every
    // other prompt, and shown CENTRED on screen (not down at the prompt spot).
    //
    // For its first few seconds a freshly-raised message outranks everything
    // (BannerPriority) so the player can't miss it -- even over a manned
    // station. After that window it drops to the lowest priority, so a proximity
    // prompt ("Press E to Continue") cleanly overrides a lingering "You have
    // arrived" when the player walks up to the portal. No permanent blocking.
    private void UpdateMessageHud()
    {
        InteractionPromptHUD hud = InteractionPromptHUD.Instance;
        if (hud == null)
        {
            return;
        }

        string message = !string.IsNullOrEmpty(currentMessage)
            ? currentMessage
            : (IsComplete ? arrivalMessage : null);

        if (string.IsNullOrEmpty(message))
        {
            hud.Hide(this);
            hudMessage = null;
            return;
        }

        // A newly-raised (or changed) message restarts the priority window.
        if (message != hudMessage)
        {
            hudMessage = message;
            hudPriorityUntil = Time.time + messagePrioritySeconds;
        }

        int priority = Time.time < hudPriorityUntil
            ? InteractionPromptHUD.BannerPriority
            : InteractionPromptHUD.StatusPriority;

        hud.Show(message, this, priority, centered: false);
    }

    private void OnDisable()
    {
        // Release the panel if we're torn down while still showing a message.
        InteractionPromptHUD.Instance?.Hide(this);
    }
}
