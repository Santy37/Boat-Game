using UnityEngine;

/// <summary>
/// One shared camera for local co-op. Centres on all players and zooms out as
/// they spread apart, back in as they regroup, and — because zoom is capped —
/// leashes players so they can never leave the frame.
///
/// Works for orthographic (2D) and perspective (3D) cameras, so the same
/// component serves a 2D island and a 3D island scene.
/// </summary>
[RequireComponent(typeof(Camera))]
public class LocalCoopCamera : MonoBehaviour
{
    [Header("Zoom")]
    [Tooltip("OFF: the camera never zooms from player spread - it holds Fixed " +
             "Zoom below. (The helm/cannon zoom-out still works.)")]
    [SerializeField] private bool enableZoom = true;
    [Tooltip("Camera size when Enable Zoom is OFF (bigger = more zoomed out).")]
    [SerializeField] private float fixedZoom = 8f;

    [Header("Framing")]
    [Tooltip("Extra world units kept around the players.")]
    [SerializeField] private float padding = 3f;
    [SerializeField] private float minZoom = 6f;
    [SerializeField] private float maxZoom = 14f;
    [SerializeField] private float smooth = 5f;
    [Tooltip("Shift the framed centre away from the crew, e.g. push them into "
        + "the lower part of the screen so a boss fits above. Zero = centred.")]
    [SerializeField] private Vector2 frameOffset = Vector2.zero;

    [Header("2D")]
    [SerializeField] private float zOffset = -10f;

    [Header("Leash")]
    [Tooltip("Players cannot move outside the visible frame.")]
    [SerializeField] private bool leashPlayers = true;
    [Tooltip("How far inside the edge players are held.")]
    [SerializeField] private float leashMargin = 1f;

    [Header("3D")]
    [Tooltip("Perspective cameras pull back along this offset as players spread.")]
    [SerializeField] private Vector3 perspectiveOffset = new Vector3(0f, 9f, -11f);

    private Camera cam;
    private bool hasZoomOverride;
    private float zoomOverride;

    private void Awake()
    {
        cam = GetComponent<Camera>();
    }

    /// <summary>
    /// Force a zoom level, ignoring player spread. Used by stations (helm,
    /// cannon) to pull the view out while someone is manning them.
    /// </summary>
    public void SetZoomOverride(float orthographicSize)
    {
        hasZoomOverride = true;
        zoomOverride = orthographicSize;
    }

    /// <summary>Return to framing the players normally.</summary>
    public void ClearZoomOverride()
    {
        hasZoomOverride = false;
    }

    /// <summary>True while a station (helm / cannon) is holding a zoom-out.</summary>
    public bool HasZoomOverride => hasZoomOverride;

    private void LateUpdate()
    {
        PlayerCharacter[] players =
            FindObjectsByType<PlayerCharacter>(FindObjectsSortMode.None);

        if (players.Length == 0)
        {
            return;
        }

        Bounds bounds = new Bounds(players[0].transform.position, Vector3.zero);
        for (int i = 1; i < players.Length; i++)
        {
            bounds.Encapsulate(players[i].transform.position);
        }

        if (cam.orthographic)
        {
            FrameOrthographic(bounds);

            if (leashPlayers)
            {
                LeashOrthographic(players);
            }
        }
        else
        {
            FramePerspective(bounds);
        }
    }

    private void FrameOrthographic(Bounds bounds)
    {
        float target;

        if (hasZoomOverride)
        {
            // Helm / cannon zoom-out always wins.
            target = zoomOverride;
        }
        else if (!enableZoom)
        {
            // No spread zooming - hold the fixed size.
            target = fixedZoom;
        }
        else
        {
            float neededVertical = bounds.extents.y + padding;
            float neededHorizontal =
                (bounds.extents.x + padding) / Mathf.Max(0.0001f, cam.aspect);

            target = Mathf.Clamp(
                Mathf.Max(neededVertical, neededHorizontal), minZoom, maxZoom);
        }

        // Snap the zoom (no easing) so the progress bar snaps with it instead
        // of drifting/scaling during the transition.
        cam.orthographicSize = target;

        Vector3 desired = new Vector3(
            bounds.center.x + frameOffset.x,
            bounds.center.y + frameOffset.y,
            zOffset);
        transform.position = Vector3.Lerp(
            transform.position, desired, smooth * Time.deltaTime);
    }

    private void FramePerspective(Bounds bounds)
    {
        float spread = Mathf.Max(bounds.size.x, bounds.size.z);
        float t = Mathf.InverseLerp(0f, Mathf.Max(0.001f, maxZoom), spread);

        Vector3 desired = bounds.center + perspectiveOffset * Mathf.Lerp(1f, 2f, t);

        transform.position = Vector3.Lerp(
            transform.position, desired, smooth * Time.deltaTime);

        transform.LookAt(bounds.center);
    }

    // Because zoom is capped, a player could otherwise walk off screen. Clamp
    // them back inside the visible rectangle.
    private void LeashOrthographic(PlayerCharacter[] players)
    {
        float halfHeight = Mathf.Max(0.1f, cam.orthographicSize - leashMargin);
        float halfWidth =
            Mathf.Max(0.1f, cam.orthographicSize * cam.aspect - leashMargin);

        Vector3 centre = transform.position;

        foreach (PlayerCharacter player in players)
        {
            Vector3 position = player.transform.position;

            float clampedX = Mathf.Clamp(
                position.x, centre.x - halfWidth, centre.x + halfWidth);
            float clampedY = Mathf.Clamp(
                position.y, centre.y - halfHeight, centre.y + halfHeight);

            if (Mathf.Approximately(clampedX, position.x) &&
                Mathf.Approximately(clampedY, position.y))
            {
                continue;
            }

            Vector3 clamped = new Vector3(clampedX, clampedY, position.z);
            player.transform.position = clamped;

            if (player.TryGetComponent(out Rigidbody2D body))
            {
                body.position = clamped;
            }
        }
    }
}
