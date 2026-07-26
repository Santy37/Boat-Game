using UnityEngine;

/// <summary>
/// Moves the kraken as a boss that hovers ABOVE the ship, slides left and right,
/// and every so often shifts to hover BELOW the ship instead. The crew dodges it
/// (and the scrolling reef) while repositioning to line up cannon shots.
///
/// The kraken always faces the camera (single sprite), so it just translates. It
/// skirts the STATIONARY margin fillers it's handed so it doesn't clip through
/// them (the scrolling reef streams behind it, so those it just overlaps).
/// Purely kinematic; it is the sole writer of the kraken's position (BoatBob is
/// removed from the boss in the arena so the two don't fight over the transform).
/// </summary>
public class KrakenStrafe : MonoBehaviour
{
    [Header("Position (world units)")]
    [Tooltip("The ship's home position -- the kraken hovers relative to this.")]
    [SerializeField] private Vector2 shipCenter = new Vector2(0f, 2f);
    [Tooltip("How far above/below the ship it looms.")]
    [SerializeField] private float hoverDistance = 16f;

    [Header("Left-right strafe")]
    [Tooltip("Half-width of the side-to-side sweep.")]
    [SerializeField] private float strafeRange = 14f;
    [Tooltip("Radians per second of the sweep (bigger = faster strafing).")]
    [SerializeField] private float strafeSpeed = 0.7f;

    [Header("Top / bottom switching")]
    [Tooltip("Seconds between shifting from above the ship to below (and back). "
        + "Set very high to keep it always above.")]
    [SerializeField] private float sideSwitchSeconds = 9f;
    [Tooltip("Seconds to travel between the top and bottom hover spots.")]
    [SerializeField] private float sideSwitchTravel = 1.6f;

    [Header("Life")]
    [SerializeField] private float bobHeight = 0.4f;
    [SerializeField] private float bobSpeed = 1.4f;

    [Header("Filler avoidance")]
    [SerializeField] private Transform[] rocks;
    [SerializeField] private float avoidRadius = 5.5f;

    private float time;
    private float sideTimer;
    private int side = 1;          // +1 = above the ship, -1 = below
    private float currentBaseY;

    private void Start()
    {
        currentBaseY = shipCenter.y + hoverDistance * side;
    }

    private void Update()
    {
        time += Time.deltaTime;
        sideTimer += Time.deltaTime;

        // Switch sides only while at a strafe extreme, so the vertical shift
        // happens off to the side and doesn't cut straight through the ship.
        if (sideTimer >= sideSwitchSeconds
            && Mathf.Abs(Mathf.Sin(time * strafeSpeed)) > 0.8f)
        {
            side = -side;
            sideTimer = 0f;
        }

        float targetBaseY = shipCenter.y + hoverDistance * side;
        float travelSpeed = 2f * hoverDistance / Mathf.Max(0.1f, sideSwitchTravel);
        currentBaseY = Mathf.MoveTowards(
            currentBaseY, targetBaseY, travelSpeed * Time.deltaTime);

        Vector2 pos = new Vector2(
            shipCenter.x + Mathf.Sin(time * strafeSpeed) * strafeRange,
            currentBaseY + Mathf.Sin(time * bobSpeed) * bobHeight);

        // Skirt any stationary filler it wanders too close to.
        if (rocks != null)
        {
            foreach (Transform rock in rocks)
            {
                if (rock == null)
                {
                    continue;
                }
                Vector2 away = pos - (Vector2)rock.position;
                float dist = away.magnitude;
                if (dist > 0.001f && dist < avoidRadius)
                {
                    pos += away / dist * (avoidRadius - dist);
                }
            }
        }

        Vector3 p = transform.position;
        transform.position = new Vector3(pos.x, pos.y, p.z);
    }
}
