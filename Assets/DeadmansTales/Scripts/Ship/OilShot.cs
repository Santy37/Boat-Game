using DeadmansTales.Ship;
using UnityEngine;

/// <summary>
/// A blob of ink/oil the kraken lobs at the ship. A straight-line kinematic
/// projectile in the spirit of <c>Cannonball</c> (Javier's "reuse the cannonball"
/// idea) but inverted: it flies from the boss toward where the ship was and, on
/// landing, damages the ship's SinkLevel through
/// <see cref="NetworkShipSinkMeter"/>.ApplyCannonHitServer -- the same
/// server-authoritative entry point a cannonball uses, self-guarded so it's a
/// no-op on every peer except whichever one is actually the server. The crew
/// dodges by steering off the firing line. No collider of its own; the hit
/// test is a Physics2D.ClosestPoint query against the ship's actual
/// ShipHitBox shape, not a circle around its bounds centre.
/// </summary>
public class OilShot : MonoBehaviour
{
    private Vector2 velocity;
    private float life;
    private float damage;
    private Collider2D shipHitbox;
    private NetworkShipSinkMeter sinkMeter;
    private float hitRadius;
    private float spin;
    private bool spent;

    public void Launch(
        Vector2 velocity, float life, float damage,
        Collider2D shipHitbox, float hitRadius)
    {
        this.velocity = velocity;
        this.life = life;
        this.damage = damage;
        this.shipHitbox = shipHitbox;
        this.hitRadius = hitRadius;
        spin = Random.Range(-120f, 120f);

        if (shipHitbox != null)
        {
            sinkMeter = shipHitbox.GetComponentInParent<NetworkShipSinkMeter>();
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        transform.position += (Vector3)(velocity * dt);
        transform.Rotate(0f, 0f, spin * dt);

        life -= dt;
        if (life <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        if (spent || shipHitbox == null)
        {
            return;
        }

        // Tested against the hull's ACTUAL shape via Physics2D.ClosestPoint,
        // not distance to shipHitbox.bounds.center -- ShipHitBox is a long
        // polygon authored well off-centre from its own transform/bounds
        // (see NetworkCannonball's own notes on this same hitbox), so a
        // centre-point circle check missed real hits and registered fake
        // ones. ClosestPoint returns the position itself (0 distance) when
        // already inside the hull, so this also just works as a direct
        // overlap test.
        Vector2 closestOnHull = Physics2D.ClosestPoint(transform.position, shipHitbox);
        if (((Vector2)transform.position - closestOnHull).sqrMagnitude
            <= hitRadius * hitRadius)
        {
            if (sinkMeter != null)
            {
                sinkMeter.ApplyCannonHitServer(damage, 1f);
            }
            spent = true;
            Destroy(gameObject);
        }
    }
}
