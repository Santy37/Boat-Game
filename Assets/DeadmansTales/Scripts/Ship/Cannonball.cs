using UnityEngine;

/// <summary>
/// Simple straight-line projectile. Fired by ShipCannon with a velocity, moves
/// each frame, and destroys itself after a lifetime or on hitting a trigger.
/// </summary>
public class Cannonball : MonoBehaviour
{
    [SerializeField] private float lifeSeconds = 3f;
    [SerializeField] private int damage = 1;

    private Vector2 velocity;
    private KrakenHealth kraken;
    private Collider2D krakenBody;

    public void Launch(Vector2 startVelocity)
    {
        velocity = startVelocity;
        Destroy(gameObject, lifeSeconds);
    }

    private void Start()
    {
        // The ball prefab has no collider or rigidbody, so OnTriggerEnter2D
        // below never actually fires -- cannons could not hurt the boss at
        // all. Rather than bolt physics onto the ball (it spawns inside the
        // ship's trigger hitbox and would pop instantly), hit-test the boss
        // directly, the same way OilShot hit-tests the ship.
        kraken = FindFirstObjectByType<KrakenHealth>();
        if (kraken != null)
        {
            krakenBody = kraken.GetComponent<Collider2D>();
        }
    }

    private void Update()
    {
        transform.position += (Vector3)(velocity * Time.deltaTime);

        if (kraken != null && !kraken.IsDead && krakenBody != null
            && krakenBody.OverlapPoint(transform.position))
        {
            // KrakenHealth is now server-authoritative (see that class) --
            // this only actually lands if this peer is the server, which is
            // true for the normal single-host test setup but not for a
            // remote (non-host) client. The arena is networked-only for now
            // anyway (see KrakenArenaBuilder), so this local-only ball is
            // effectively unused there.
            kraken.TakeHitServer(damage);
            Destroy(gameObject);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Deal damage to a kraken if we hit one, then always spend the ball.
        KrakenHealth kraken = other.GetComponentInParent<KrakenHealth>();
        if (kraken != null)
        {
            kraken.TakeHitServer(damage);
        }

        Destroy(gameObject);
    }
}
