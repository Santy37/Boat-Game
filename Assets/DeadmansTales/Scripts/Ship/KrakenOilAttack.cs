using System.Collections;
using UnityEngine;

/// <summary>
/// The kraken periodically lobs oil blobs at the ship -- a ranged pressure
/// attack that runs on its own cadence alongside the tentacle slam, so the crew
/// juggles dodging rocks, the slam, and incoming oil. Reuses the simple
/// straight-line projectile pattern (Javier's cannonball idea) via
/// <see cref="OilShot"/>, aimed at the ship's CURRENT position with no lead, so
/// it is always dodgeable by steering off the firing line.
/// </summary>
public class KrakenOilAttack : MonoBehaviour
{
    [Tooltip("Prefab with a SpriteRenderer (the oil glob) and an OilShot.")]
    [SerializeField] private GameObject oilPrefab;
    [SerializeField] private Collider2D shipHitbox;

    [Header("Cadence (seconds)")]
    [SerializeField] private float firstDelay = 3.5f;
    [SerializeField] private float interval = 2.75f;

    [Header("Projectile")]
    [Tooltip(
        "60% slower than the original 16 -- gives the crew a fair chance "
        + "to read and dodge the firing line."
    )]
    [SerializeField] private float projectileSpeed = 6.4f;
    [SerializeField] private float projectileLife = 4f;
    [SerializeField] private float hitRadius = 2.5f;
    [Tooltip(
        "SinkLevel damage on landing, applied the same way a cannonball is "
        + "(NetworkShipSinkMeter.ApplyCannonHitServer). Matches "
        + "NetworkCannonball's default of 25 -- bump it higher here if the "
        + "oil should hurt more than a regular cannon hit."
    )]
    [SerializeField] private float oilDamage = 25f;
    [Tooltip("Above the tentacle (6) so the oil reads in front as it flies.")]
    [SerializeField] private int sortingOrder = 7;

    private void Start()
    {
        if (oilPrefab == null)
        {
            Debug.LogWarning("[KrakenOilAttack] No oil prefab; no oil.", this);
            return;
        }
        StartCoroutine(Loop());
    }

    private IEnumerator Loop()
    {
        yield return new WaitForSeconds(firstDelay);
        while (true)
        {
            Fire();
            yield return new WaitForSeconds(interval);
        }
    }

    private void Fire()
    {
        Vector2 origin = transform.position;
        Vector2 targetPos = shipHitbox != null
            ? (Vector2)shipHitbox.bounds.center
            : origin + Vector2.down * 10f;

        Vector2 dir = targetPos - origin;
        if (dir.sqrMagnitude < 0.0001f)
        {
            dir = Vector2.down;
        }
        dir.Normalize();

        GameObject go = Instantiate(oilPrefab);
        go.transform.position = new Vector3(origin.x, origin.y, 0f);

        SpriteRenderer sr = go.GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
        {
            sr.sortingOrder = sortingOrder;
        }

        OilShot shot = go.GetComponent<OilShot>();
        if (shot == null)
        {
            shot = go.AddComponent<OilShot>();
        }
        shot.Launch(
            dir * projectileSpeed, projectileLife, oilDamage, shipHitbox, hitRadius);
    }
}
