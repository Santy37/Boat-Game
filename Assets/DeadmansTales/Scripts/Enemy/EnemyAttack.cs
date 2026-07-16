using System.Collections.Generic;
using UnityEngine;

// EnemyAttack
// The enemy's sword. Mirrors PlayerAttack
public class EnemyAttack : MonoBehaviour
{
    [SerializeField] private Animator anim;
    [Tooltip("Cooldown between swings, same idea as the player's meleeSpeed.")]
    [SerializeField] private float meleeSpeed = 1.25f;
    [SerializeField] private float damage = 10f;
    [Tooltip("How long the blade can deal damage after a swing starts.")]
    [SerializeField] private float hitActiveTime = 0.2f;

    private float timeUntilMelee;
    private float hitActiveTimer;

    // Prevents a single swing from hitting the same player many times.
    private readonly HashSet<PlayerHealth> hitThisSwing =
        new HashSet<PlayerHealth>();

    private void Update()
    {
        if (timeUntilMelee > 0f)
        {
            timeUntilMelee -= Time.deltaTime;
        }

        if (hitActiveTimer > 0f)
        {
            hitActiveTimer -= Time.deltaTime;
        }
    }

    // Called by EnemyAI when a player is in attack range.
    // Returns true if a swing actually started (i.e. not on cooldown).
    public bool TryAttack()
    {
        if (timeUntilMelee > 0f)
        {
            return false;
        }

        if (anim != null)
        {
            anim.SetTrigger("Attack");
        }

        timeUntilMelee = meleeSpeed;
        hitActiveTimer = hitActiveTime;
        hitThisSwing.Clear();
        return true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryHit(other);
    }

    // Covers players who were already overlapping the blade when the swing began.
    private void OnTriggerStay2D(Collider2D other)
    {
        TryHit(other);
    }

    private void TryHit(Collider2D other)
    {
        if (hitActiveTimer <= 0f)
        {
            return;
        }

        PlayerHealth player = other.GetComponentInParent<PlayerHealth>();

        if (player == null || hitThisSwing.Contains(player))
        {
            return;
        }

        hitThisSwing.Add(player);
        player.TakeDamage(damage);
    }
}
