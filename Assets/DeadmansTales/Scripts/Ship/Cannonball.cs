using UnityEngine;

/// <summary>
/// Simple straight-line projectile. Fired by ShipCannon with a velocity, moves
/// each frame, and destroys itself after a lifetime or on hitting a trigger.
/// </summary>
public class Cannonball : MonoBehaviour
{
    [SerializeField] private float lifeSeconds = 3f;

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
        Destroy(gameObject);
    }
}
