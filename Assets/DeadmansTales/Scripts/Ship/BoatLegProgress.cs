using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the boat leg's progress bar: fills over time, positions the line /
/// islands / ship, and spawns event icons on the line. Landing is handled
/// elsewhere (e.g. the stage portal) - call <see cref="LandOnIsland"/>.
/// </summary>
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

    [Header("Progress Bar - Events (spawn ON THE LINE)")]
    [Tooltip("1 = obstacles only, 2 = one ship + one obstacle, 3 = 3 random.")]
    [SerializeField, Range(1, 3)] private int level = 1;
    [Tooltip("Obstacle icon - hidden at start, cloned onto the line.")]
    [SerializeField] private Transform obstacleIcon;
    [Tooltip("Pirate ship icon - hidden at start, cloned onto the line.")]
    [SerializeField] private Transform pirateShipIcon;
    [Tooltip("Nudge event icons off the line, e.g. y = -1 to drop the rocks " +
             "down onto it.")]
    [SerializeField] private Vector2 eventOffset = new Vector2(0f, -1f);
    [Tooltip("Events spawn no closer to the START island than this " +
             "(0 = start island, 1 = end island).")]
    [SerializeField, Range(0f, 1f)] private float eventMinFraction = 0.25f;
    [Tooltip("Events spawn no closer to the END island than this.")]
    [SerializeField, Range(0f, 1f)] private float eventMaxFraction = 0.9f;

    [Header("Progress Bar - Event Pause")]
    [Tooltip("Ship pauses when it gets within this many units of an event.")]
    [SerializeField] private float eventTriggerRange = 2.5f;
    [Tooltip("Seconds the bar pauses at each event.")]
    [SerializeField] private float eventPauseDuration = 3f;
    [Tooltip("Message shown for OBSTACLE events.")]
    [SerializeField] private string obstacleMessage = "Protect the ship!";
    [Tooltip("Message shown for ENEMY SHIP events.")]
    [SerializeField] private string enemyMessage = "Attack the pirates!";

    [Header("Progress Bar - Arrival Message")]
    [Tooltip("Shown centred when the bar finishes (then the portal is usable).")]
    [SerializeField] private string arrivalMessage = "You have arrived";
    [Tooltip("Font size of the centred messages (events + arrival).")]
    [SerializeField] private int messageFontSize = 40;
    [SerializeField] private Color messageColor = Color.white;
    private GUIStyle messageStyle;

    private int activeEvent = -1;
    private float eventEndTime;
    private string currentMessage = string.Empty;

    private bool sailed;
    private Vector3 normalScale = Vector3.one;
    private LocalCoopCamera coopCam;
    private readonly List<Transform> events = new List<Transform>();
    private readonly List<float> eventFractions = new List<float>();
    private readonly List<bool> eventIsEnemy = new List<bool>();
    private readonly List<bool> eventDone = new List<bool>();

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

        SpawnEvents();
    }

    private void SpawnEvents()
    {
        // Which icons to place, per the level (only one level is active).
        List<Transform> chosen = new List<Transform>();
        if (level <= 1)                       // one obstacle only
        {
            chosen.Add(obstacleIcon);
        }
        else if (level == 2)                  // one ship + one obstacle
        {
            chosen.Add(pirateShipIcon);
            chosen.Add(obstacleIcon);
        }
        else                                  // 3 random of the two
        {
            for (int i = 0; i < 3; i++)
            {
                chosen.Add(Random.value < 0.5f ? pirateShipIcon : obstacleIcon);
            }
        }

        int count = chosen.Count;
        for (int i = 0; i < count; i++)
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
            // Random spot on the line, kept away from the start island.
            eventFractions.Add(Random.Range(eventMinFraction, eventMaxFraction));
            eventIsEnemy.Add(template == pirateShipIcon);
            eventDone.Add(false);
        }
    }

    private void Update()
    {
        // Paused at an event: hold the bar until the pause time is up.
        if (activeEvent >= 0)
        {
            if (Time.time >= eventEndTime)
            {
                eventDone[activeEvent] = true;
                activeEvent = -1;
                currentMessage = string.Empty;
            }
            return;
        }

        // Ship reached an event? -> pause and show its message.
        Vector2 shipPos = Vector2.Lerp(startSpot, endSpot, progress01);
        for (int i = 0; i < events.Count; i++)
        {
            if (eventDone[i] || events[i] == null)
            {
                continue;
            }

            Vector2 evPos = Vector2.Lerp(startSpot, endSpot, eventFractions[i]);
            if (Vector2.Distance(shipPos, evPos) <= eventTriggerRange)
            {
                activeEvent = i;
                eventEndTime = Time.time + eventPauseDuration;
                currentMessage = eventIsEnemy[i] ? enemyMessage : obstacleMessage;
                return;
            }
        }

        // Otherwise advance the bar.
        if (autoAdvance && !sailed && legDuration > 0f && progress01 < 1f)
        {
            progress01 = Mathf.Clamp01(progress01 + Time.deltaTime / legDuration);
        }
    }

    private bool IsManning()
    {
        if (coopCam == null)
        {
            coopCam = FindFirstObjectByType<LocalCoopCamera>();
        }
        return coopCam != null && coopCam.HasZoomOverride;
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

    // Centred messages: the event message while paused, else "You have arrived".
    private void OnGUI()
    {
        string message = !string.IsNullOrEmpty(currentMessage)
            ? currentMessage
            : (IsComplete ? arrivalMessage : null);

        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        if (messageStyle == null)
        {
            messageStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
        }
        messageStyle.fontSize = messageFontSize;

        Rect rect = new Rect(0f, Screen.height * 0.5f - 40f, Screen.width, 80f);
        Color saved = GUI.color;
        GUI.color = messageColor;
        GUI.Label(rect, message, messageStyle);
        GUI.color = saved;
    }
}
