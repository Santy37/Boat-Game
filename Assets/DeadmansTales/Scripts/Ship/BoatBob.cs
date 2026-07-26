using UnityEngine;

/// <summary>
/// Makes a boat gently float: bobs up and down on a sine wave and can rock
/// slightly side to side. Put this on the ship (or a visual child of it) and
/// it oscillates around whatever position/rotation it starts with.
///
/// Pair it with a static waterline shadow / sprite-mask so the lower hull
/// looks like it dips under the surface as the boat bobs down.
/// </summary>
public class BoatBob : MonoBehaviour
{
    [Header("Up / Down Bob")]
    [Tooltip("How far, in world units, the boat rises and falls from rest.")]
    [SerializeField] private float bobHeight = 0.15f;

    [Tooltip("Bobs per second. Lower = slow, heavy swell. Higher = choppy.")]
    [SerializeField] private float bobSpeed = 0.6f;

    [Header("Rocking (optional)")]
    [Tooltip("How many degrees the boat tilts back and forth. 0 = no rock.")]
    [SerializeField] private float rockDegrees = 1.5f;

    [Tooltip("Rocks per second. Usually a bit slower than the bob feels best.")]
    [SerializeField] private float rockSpeed = 0.45f;

    [Header("Feel")]
    [Tooltip("Randomizes the starting point of the wave so multiple boats " +
             "don't bob in perfect sync.")]
    [SerializeField] private bool randomizePhase = true;

    [Tooltip("Snap vertical movement to the pixel grid for a crisp pixel look.")]
    [SerializeField] private bool snapToPixels = true;
    [SerializeField] private float pixelsPerUnit = 32f;

    private Quaternion baseRotation;
    private float bobPhase;
    private float rockPhase;

    // The offset this script itself added last frame. Subtracting it back
    // out of the current transform recovers whatever "real" position/
    // rotation another script (e.g. EnemyShipApproach, ShipHelm) set this
    // frame, so bobbing layers on top of movement instead of overwriting it.
    // Without this, BoatBob would cache the ship's spawn position once in
    // Awake and silently snap it back there every frame -- even with
    // bobHeight/rockDegrees at 0 -- which fully cancels out any other
    // script's movement on the same object.
    private Vector3 lastAppliedPositionOffset;
    private Quaternion lastAppliedRotationOffset = Quaternion.identity;

    private void Awake()
    {
        baseRotation = transform.localRotation;

        if (randomizePhase)
        {
            bobPhase = Random.value * Mathf.PI * 2f;
            rockPhase = Random.value * Mathf.PI * 2f;
        }
    }

    private void LateUpdate()
    {
        float t = Time.time;

        Vector3 externalPosition =
            transform.localPosition - lastAppliedPositionOffset;

        float y = Mathf.Sin(t * bobSpeed * Mathf.PI * 2f + bobPhase) * bobHeight;
        Vector3 offset = new Vector3(0f, y, 0f);

        Vector3 p = externalPosition + offset;

        if (snapToPixels && pixelsPerUnit > 0f)
        {
            float unitsPerPixel = 1f / pixelsPerUnit;
            p.y = Mathf.Round(p.y / unitsPerPixel) * unitsPerPixel;
        }

        transform.localPosition = p;
        lastAppliedPositionOffset = p - externalPosition;

        if (rockDegrees > 0f)
        {
            Quaternion externalRotation =
                transform.localRotation *
                Quaternion.Inverse(lastAppliedRotationOffset);

            float angle =
                Mathf.Sin(t * rockSpeed * Mathf.PI * 2f + rockPhase) * rockDegrees;
            Quaternion rockOffset = Quaternion.Euler(0f, 0f, angle);

            transform.localRotation = externalRotation * rockOffset;
            lastAppliedRotationOffset = rockOffset;
        }
    }
}
