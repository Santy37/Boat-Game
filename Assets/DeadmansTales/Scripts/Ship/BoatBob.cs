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

    private Vector3 basePosition;
    private Quaternion baseRotation;
    private float bobPhase;
    private float rockPhase;

    private void Awake()
    {
        basePosition = transform.localPosition;
        baseRotation = transform.localRotation;

        if (randomizePhase)
        {
            bobPhase = Random.value * Mathf.PI * 2f;
            rockPhase = Random.value * Mathf.PI * 2f;
        }
    }

    private void Update()
    {
        float t = Time.time;

        float y = Mathf.Sin(t * bobSpeed * Mathf.PI * 2f + bobPhase) * bobHeight;

        Vector3 p = basePosition + new Vector3(0f, y, 0f);

        if (snapToPixels && pixelsPerUnit > 0f)
        {
            float unitsPerPixel = 1f / pixelsPerUnit;
            p.y = Mathf.Round(p.y / unitsPerPixel) * unitsPerPixel;
        }

        transform.localPosition = p;

        if (rockDegrees > 0f)
        {
            float angle =
                Mathf.Sin(t * rockSpeed * Mathf.PI * 2f + rockPhase) * rockDegrees;
            transform.localRotation = baseRotation * Quaternion.Euler(0f, 0f, angle);
        }
    }
}
