using UnityEngine;

/// <summary>
/// Boss health for the kraken. Prototype-simple: takes hits from cannonballs,
/// flashes on damage, dies at zero. Server-authority and a real health bar can
/// come later; this is enough to make the fight a fight.
/// </summary>
public class KrakenHealth : MonoBehaviour
{
    [SerializeField] private int maxHealth = 20;
    [SerializeField] private float flashSeconds = 0.1f;

    private int health;
    private SpriteRenderer sprite;
    private float flashUntil;
    private Color baseColor;

    public bool IsDead => health <= 0;

    private void Awake()
    {
        health = maxHealth;
        sprite = GetComponentInChildren<SpriteRenderer>();
        if (sprite != null)
        {
            baseColor = sprite.color;
        }
    }

    /// <summary>Called by a cannonball that hits the kraken.</summary>
    public void TakeHit(int damage)
    {
        if (IsDead)
        {
            return;
        }

        health = Mathf.Max(0, health - Mathf.Max(1, damage));
        flashUntil = Time.time + flashSeconds;

        Debug.Log($"[Kraken] Hit for {damage}. Health {health}/{maxHealth}.");

        if (health <= 0)
        {
            Die();
        }
    }

    private void Update()
    {
        if (sprite == null)
        {
            return;
        }

        sprite.color = Time.time < flashUntil
            ? Color.red
            : baseColor;
    }

    private void Die()
    {
        Debug.Log("[Kraken] Defeated.");
        // Prototype: just vanish. A death animation / win trigger comes later.
        Destroy(gameObject);
    }
}
