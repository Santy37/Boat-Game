using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Progress-bar layout: puts the two island children on the ends of the line
/// and slides the ship along it as the boat leg fills.
///
/// - Start island sits on the RIGHT, end island on the LEFT.
/// - The ship starts on the start (right) island and moves to the left one.
///
/// Positions are LOCAL (relative to the progress bar), so edit the two spot
/// fields to line them up with your line. Boat-only - reads BoatLegProgress.
/// </summary>
public class BoatBarShip : MonoBehaviour
{
    [Header("Screen position - top-centre by default")]
    [Tooltip("Where the whole bar sits on screen: (0.5, 0.9) = top-centre. " +
             "x: 0=left..1=right,  y: 0=bottom..1=top.")]
    [SerializeField] private Vector2 screenPosition = new Vector2(0.5f, 0.9f);
    [Tooltip("Distance in front of the camera.")]
    [SerializeField] private float distanceFromCamera = 10f;
    [Tooltip("Leave empty to use the Main Camera.")]
    [SerializeField] private Camera targetCamera;

    [Header("Positions - edit each piece")]
    [Tooltip("START island (RIGHT) - where the ship starts.")]
    [SerializeField] private Vector2 startIslandSpot = new Vector2(4f, 0f);
    [Tooltip("END island (LEFT) - the destination.")]
    [SerializeField] private Vector2 endIslandSpot = new Vector2(-4f, 0f);
    [Tooltip("LINE - nudge it relative to the island spots (e.g. y down).")]
    [SerializeField] private Vector2 lineOffset = Vector2.zero;
    [Tooltip("SHIP - nudge it relative to the line (e.g. y up to ride on top).")]
    [SerializeField] private Vector2 shipOffset = Vector2.zero;

    [Header("Children of the progress bar")]
    [Tooltip("Island on the RIGHT / start.")]
    [SerializeField] private Transform startIsland;
    [Tooltip("Island on the LEFT / destination.")]
    [SerializeField] private Transform endIsland;
    [Tooltip("The ship icon.")]
    [SerializeField] private Transform ship;
    [Tooltip("The line object (a sprite OR a line renderer) - moved by Line " +
             "Offset and centred between the islands.")]
    [SerializeField] private Transform line;
    [Tooltip("Leave empty to find the BoatLegProgress in the scene.")]
    [SerializeField] private BoatLegProgress leg;

    [Header("Events")]
    [Tooltip("Marker templates to clone for each event. The originals are " +
             "hidden - only clones show, and each vanishes as the ship passes.")]
    [SerializeField] private Transform[] eventMarkers;
    [Tooltip("Level: 1 = 2 events, 2 = 3 events, 3 = 4 events (events = level + 1).")]
    [SerializeField] private int level = 1;
    [Tooltip("ON: spawn events when the scene starts. OFF: call StartLevel() " +
             "later to turn them on.")]
    [SerializeField] private bool startOnPlay = false;

    private readonly List<Transform> eventInstances = new List<Transform>();
    private readonly List<float> eventFractions = new List<float>();

    private void Awake()
    {
        if (leg == null)
        {
            leg = FindFirstObjectByType<BoatLegProgress>();
        }

        // Hide the template markers - only spawned clones show.
        if (eventMarkers != null)
        {
            foreach (Transform m in eventMarkers)
            {
                if (m != null)
                {
                    m.gameObject.SetActive(false);
                }
            }
        }
    }

    private void Start()
    {
        if (startOnPlay)
        {
            StartLevel();
        }
    }

    /// <summary>
    /// Start the events for the Level set in the inspector. Call this from a UI
    /// button or other code whenever you want to turn the events on.
    /// </summary>
    public void StartLevel()
    {
        StartLevel(Mathf.Max(1, level) + 1);
    }

    /// <summary>Start with an explicit number of events.</summary>
    public void StartLevel(int eventCount)
    {
        // Clear any events from the last run.
        foreach (Transform t in eventInstances)
        {
            if (t != null)
            {
                Destroy(t.gameObject);
            }
        }
        eventInstances.Clear();
        eventFractions.Clear();

        // Restart the leg so the ship sails from the start again.
        if (leg != null)
        {
            leg.SetProgress(0f);
        }

        for (int i = 0; i < eventCount; i++)
        {
            float fraction = (i + 1f) / (eventCount + 1f);
            eventFractions.Add(fraction);

            Transform template = PickEventMarker();
            if (template == null)
            {
                eventInstances.Add(null);
                continue;
            }

            GameObject clone = Instantiate(template.gameObject, template.parent);
            clone.SetActive(true);
            eventInstances.Add(clone.transform);
        }
    }

    private Transform PickEventMarker()
    {
        if (eventMarkers == null || eventMarkers.Length == 0)
        {
            return null;
        }
        return eventMarkers[Random.Range(0, eventMarkers.Length)];
    }

    private void LateUpdate()
    {
        // Put the whole bar at the top-centre of the screen, recomputed every
        // frame - so the line AND every piece follow the camera at any zoom.
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam != null)
        {
            transform.position = cam.ViewportToWorldPoint(new Vector3(
                screenPosition.x, screenPosition.y, distanceFromCamera));
        }

        // Place the islands on the ends of the line.
        if (startIsland != null)
        {
            startIsland.localPosition = startIslandSpot;
        }
        if (endIsland != null)
        {
            endIsland.localPosition = endIslandSpot;
        }

        // Centre the line between the two islands, plus the line offset.
        if (line != null)
        {
            line.localPosition =
                (startIslandSpot + endIslandSpot) * 0.5f + lineOffset;
        }

        // Slide the ship from the start (right) island to the end (left) island.
        if (ship != null)
        {
            if (leg == null)
            {
                leg = FindFirstObjectByType<BoatLegProgress>();
            }

            float progress = leg != null ? leg.Progress01 : 0f;
            ship.localPosition =
                Vector2.Lerp(startIslandSpot, endIslandSpot, progress) + shipOffset;
        }

        // Events sit on the line where the ship rides, and each disappears once
        // the ship reaches it (progress passes its spot).
        float legProgress = leg != null ? leg.Progress01 : 0f;
        for (int i = 0; i < eventInstances.Count; i++)
        {
            if (eventInstances[i] == null)
            {
                continue;
            }

            if (legProgress >= eventFractions[i])
            {
                Destroy(eventInstances[i].gameObject);
                eventInstances[i] = null;
                continue;
            }

            eventInstances[i].localPosition =
                Vector2.Lerp(startIslandSpot, endIslandSpot, eventFractions[i])
                + shipOffset;
        }
    }
}
