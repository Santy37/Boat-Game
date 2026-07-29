using System.Collections;
using DeadmansTales.Ship;
using UnityEngine;

/// <summary>
/// The kraken's telegraphed tentacle slam. Every so often it marks a spot (where
/// the ship currently is) with a whirlpool that grows and reddens over a warning
/// window; a TENTACLE erupts out of that whirlpool, rears back, and SLAMS down --
/// if the ship is still inside the strike zone when it lands, it takes a big hit
/// to its SinkLevel (see <see cref="NetworkShipSinkMeter"/>). Then the tentacle
/// sinks and the whirlpool fades. The crew dodges by steering out of the marked
/// zone before the slam lands.
///
/// The whirlpool is the "wave layer" telegraph; the tentacle (an 8-frame
/// rise -> arc -> slam -> sink flipbook, Santiago's sprite) is the payload.
/// Damage is applied through NetworkShipSinkMeter.ApplyCannonHitServer, the same
/// server-authoritative entry point a cannonball uses -- self-guarded, so it's a
/// silent no-op on every peer except whichever one is actually the server. If no
/// tentacle frames are wired it degrades to the old whirlpool-only strike, so
/// nothing breaks when art is missing.
/// </summary>
public class KrakenAttack : MonoBehaviour
{
    [Header("Telegraph (whirlpool wave layer)")]
    [Tooltip("The whirlpool prefab used as the telegraph marker the tentacle "
        + "erupts from. Optional -- leave null for a tentacle with no wave.")]
    [SerializeField] private GameObject whirlpoolPrefab;
    [SerializeField] private int whirlpoolSortingOrder = 5;
    [Tooltip("World size of the whirlpool at full (strike) size.")]
    [SerializeField] private float strikeWorldSize = 6.3f;

    [Header("Tentacle slam")]
    [Tooltip("8-frame flipbook: splash -> rise -> arc -> slam -> sink. If empty, "
        + "the attack falls back to a whirlpool-only strike.")]
    [SerializeField] private Sprite[] tentacleFrames;
    [Tooltip("Above the whirlpool (5) so the tentacle reads in front of its wave.")]
    [SerializeField] private int tentacleSortingOrder = 6;
    [Tooltip("Extra scale on top of the sprite's native (import-PPU) size.")]
    [SerializeField] private float tentacleScale = 1f;
    [Tooltip("How long the arc-over-and-slam takes once the telegraph ends.")]
    [SerializeField] private float slamTime = 0.4f;

    [Header("Targeting / damage")]
    [Tooltip("The ship hull collider -- its centre is the attack target and the "
        + "thing checked against the strike zone.")]
    [SerializeField] private Collider2D shipHitbox;
    [SerializeField] private float strikeRadius = 4.5f;
    [Tooltip(
        "SinkLevel damage if the ship is still in the strike zone when the "
        + "tentacle lands. Deliberately a big chunk of SinkLevel's default "
        + "150 max -- getting caught in the whirlpool should hurt a lot "
        + "more than a single cannon hit (NetworkCannonball's default is "
        + "25). Applied the same way a cannonball is, via "
        + "NetworkShipSinkMeter.ApplyCannonHitServer, so it drains SinkLevel "
        + "first and spills into Health if SinkLevel can't absorb it all."
    )]
    [SerializeField] private float sinkMeterDamage = 100f;

    [Header("Timing (seconds)")]
    [SerializeField] private float firstDelay = 2.5f;
    [SerializeField] private float attackInterval = 3.5f;
    [Tooltip("Warning window: the whirlpool ALONE grows and reddens; the "
        + "tentacle only appears when the strike lands.")]
    [SerializeField] private float telegraphTime = 2.7f;
    [SerializeField] private float strikeHold = 0.35f;
    [SerializeField] private float fadeTime = 0.5f;

    private NetworkShipSinkMeter sinkMeter;

    private void Start()
    {
        bool hasTentacle = tentacleFrames != null && tentacleFrames.Length > 0;
        if (whirlpoolPrefab == null && !hasTentacle)
        {
            Debug.LogWarning(
                "[KrakenAttack] No whirlpool prefab and no tentacle frames; "
                + "no attacks.", this);
            return;
        }
        StartCoroutine(AttackLoop());
    }

    private IEnumerator AttackLoop()
    {
        yield return new WaitForSeconds(firstDelay);
        while (true)
        {
            yield return StartCoroutine(OneAttack());
            yield return new WaitForSeconds(attackInterval);
        }
    }

    private IEnumerator OneAttack()
    {
        Vector2 target = shipHitbox != null
            ? (Vector2)shipHitbox.bounds.center
            : (Vector2)transform.position;

        // --- Telegraph marker: the whirlpool the tentacle erupts from.
        GameObject whirl = null;
        SpriteRenderer whirlSr = null;
        float whirlBaseScale = 1f;
        if (whirlpoolPrefab != null)
        {
            whirl = Instantiate(whirlpoolPrefab);
            whirl.transform.position = new Vector3(target.x, target.y, 0f);
            whirlSr = whirl.GetComponentInChildren<SpriteRenderer>();
            if (whirlSr != null)
            {
                whirlSr.sortingOrder = whirlpoolSortingOrder;
                if (whirlSr.sprite != null)
                {
                    float spriteSize = Mathf.Max(
                        whirlSr.sprite.bounds.size.x,
                        whirlSr.sprite.bounds.size.y);
                    if (spriteSize > 0.001f)
                    {
                        whirlBaseScale = strikeWorldSize / spriteSize;
                    }
                }
            }
        }

        // Frame split across the 8-frame flipbook: the whole rise -> arc ->
        // slam plays as the STRIKE (the telegraph is whirlpool-only), then the
        // last frame is the sink. Robust to shorter sheets by clamping.
        bool hasTentacle = tentacleFrames != null && tentacleFrames.Length > 0;
        int last = hasTentacle ? tentacleFrames.Length - 1 : 0;
        int slamFrame = Mathf.Clamp(last - 1, 0, last);   // frame 6 of 0..7
        int sinkFrame = last;                             // frame 7

        // --- Telegraph: the whirlpool ALONE grows + reddens. No tentacle yet;
        // the crew reads the marked spot and steers clear.
        float t = 0f;
        while (t < telegraphTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / telegraphTime);
            if (whirl != null)
            {
                whirl.transform.localScale =
                    Vector3.one * whirlBaseScale * Mathf.Lerp(0.25f, 1f, k);
                if (whirlSr != null)
                {
                    whirlSr.color = Color.Lerp(
                        new Color(1f, 1f, 1f, 0.55f),
                        new Color(1f, 0.5f, 0.5f, 1f), k);
                }
            }
            yield return null;
        }

        // --- Strike: the tentacle ERUPTS out of the marked spot -- the whole
        // rise-arc-slam flipbook plays fast as the hit itself.
        if (whirl != null)
        {
            whirl.transform.localScale = Vector3.one * whirlBaseScale;
            if (whirlSr != null)
            {
                whirlSr.color = Color.white;
            }
        }

        SpriteRenderer tentSr = null;
        GameObject tentacle = null;
        if (hasTentacle)
        {
            tentacle = new GameObject("Tentacle");
            tentacle.transform.position = new Vector3(target.x, target.y, 0f);
            tentacle.transform.localScale = Vector3.one * tentacleScale;
            tentSr = tentacle.AddComponent<SpriteRenderer>();
            tentSr.sortingOrder = tentacleSortingOrder;
            tentSr.sprite = tentacleFrames[0];
        }

        float s = 0f;
        while (s < slamTime)
        {
            s += Time.deltaTime;
            float k = Mathf.Clamp01(s / Mathf.Max(0.0001f, slamTime));
            if (tentSr != null)
            {
                SetFrame(tentSr,
                    Mathf.RoundToInt(Mathf.Lerp(0, slamFrame, k)));
            }
            yield return null;
        }
        if (tentSr != null)
        {
            SetFrame(tentSr, slamFrame);
        }

        // --- Impact: the slam lands. Damage the ship's SinkLevel a lot if
        // it's still in the zone. Tested against the hull's ACTUAL shape via
        // Physics2D.ClosestPoint, not distance to shipHitbox.bounds.center --
        // ShipHitBox is a long polygon whose authored points sit well away
        // from its own centre (see NetworkCannonball.ComputeHitDirectness's
        // notes on this exact same hitbox), so a single centre-point circle
        // check was very often wrong: a "miss" while the ship visually sat
        // right under the strike, or a "hit" while it was nowhere close.
        // ClosestPoint finds the nearest point ON THE HULL itself (0 if
        // target already lands inside it), so this respects the real shape.
        if (shipHitbox != null)
        {
            Vector2 closestOnHull = Physics2D.ClosestPoint(target, shipHitbox);
            if ((closestOnHull - target).sqrMagnitude <= strikeRadius * strikeRadius)
            {
                NetworkShipSinkMeter resolvedSinkMeter = ResolveSinkMeter();
                if (resolvedSinkMeter != null)
                {
                    resolvedSinkMeter.ApplyCannonHitServer(sinkMeterDamage, 1f);
                }
            }
        }
        yield return new WaitForSeconds(strikeHold);

        // --- Sink + fade: tentacle drops to its sink frame, both fade out.
        if (tentSr != null)
        {
            SetFrame(tentSr, sinkFrame);
        }
        float f = 0f;
        Vector3 whirlFull = whirl != null
            ? whirl.transform.localScale : Vector3.one;
        while (f < fadeTime)
        {
            f += Time.deltaTime;
            float k = Mathf.Clamp01(f / fadeTime);
            if (whirl != null)
            {
                whirl.transform.localScale = whirlFull * Mathf.Lerp(1f, 0.1f, k);
                if (whirlSr != null)
                {
                    Color c = whirlSr.color;
                    c.a = Mathf.Lerp(1f, 0f, k);
                    whirlSr.color = c;
                }
            }
            if (tentSr != null)
            {
                Color c = tentSr.color;
                c.a = Mathf.Lerp(1f, 0f, k);
                tentSr.color = c;
            }
            yield return null;
        }

        if (whirl != null)
        {
            Destroy(whirl);
        }
        if (tentacle != null)
        {
            Destroy(tentacle);
        }
    }

    private void SetFrame(SpriteRenderer sr, int index)
    {
        if (sr == null || tentacleFrames == null || tentacleFrames.Length == 0)
        {
            return;
        }
        index = Mathf.Clamp(index, 0, tentacleFrames.Length - 1);
        sr.sprite = tentacleFrames[index];
    }

    // Lazily resolved from shipHitbox rather than a scene-wide
    // FindFirstObjectByType -- same reasoning as NetworkCannonball: an enemy
    // ship carries its own NetworkShipSinkMeter too, and shipHitbox is
    // already wired to the PLAYER's own hull specifically.
    private NetworkShipSinkMeter ResolveSinkMeter()
    {
        if (sinkMeter == null && shipHitbox != null)
        {
            sinkMeter = shipHitbox.GetComponentInParent<NetworkShipSinkMeter>();
        }
        return sinkMeter;
    }
}
