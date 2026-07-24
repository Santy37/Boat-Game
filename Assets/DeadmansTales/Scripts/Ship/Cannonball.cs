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

    public void Launch(Vector2 startVelocity)
    {
        velocity = startVelocity;
        Destroy(gameObject, lifeSeconds);
    }

    private void Update()
    {
        transform.position += (Vector3)(velocity * Time.deltaTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Deal damage to a kraken if we hit one, then always spend the ball.
        KrakenHealth kraken = other.GetComponentInParent<KrakenHealth>();
        if (kraken != null)
        {
            kraken.TakeHit(damage);
        }

        Destroy(gameObject);
    }
}
