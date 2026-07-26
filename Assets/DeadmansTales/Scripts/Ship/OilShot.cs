using UnityEngine;

/// <summary>
/// A blob of ink/oil the kraken lobs at the ship. A straight-line kinematic
/// projectile in the spirit of <c>Cannonball</c> (Javier's "reuse the cannonball"
/// idea) but inverted: it flies from the boss toward where the ship was and
/// damages the SHIP through the mode-agnostic <see cref="RunContext"/> when it
/// lands. Client-side and guarded, like the arena's other hazards -- the crew
/// dodges by steering off the firing line. No collider; a cheap distance check
/// against the ship's hitbox centre decides the hit.
/// </summary>
public class OilShot : MonoBehaviour
{
    private Vector2 velocity;
    private float life;
    private int damage;
    private Collider2D shipHitbox;
    private float hitRadius;
    private float spin;
    private bool spent;

    public void Launch(
        Vector2 velocity, float life, int damage,
        Collider2D shipHitbox, float hitRadius)
    {
        this.velocity = velocity;
        this.life = life;
        this.damage = damage;
        this.shipHitbox = shipHitbox;
        this.hitRadius = hitRadius;
        spin = Random.Range(-120f, 120f);
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

        Vector2 shipCenter = shipHitbox.bounds.center;
        if (((Vector2)transform.position - shipCenter).sqrMagnitude
            <= hitRadius * hitRadius)
        {
            if (RunContext.HasActive)
            {
                RunContext.Active.DamageShip(damage);
            }
            spent = true;
            Destroy(gameObject);
        }
    }
}
