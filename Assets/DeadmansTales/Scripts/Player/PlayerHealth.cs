using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/* PlayerHealth
Gives the player the same health / damage / death behaviour the Enemy has,
so a player can be hurt the same way it hurts enemies. Mirrors Enemy.cs:
a health value, a fading health-bar slider, TakeDamage(), and a Die() that
fires the "Die" animator trigger.
Difference from Enemy: a player is a network object, so instead of
Destroy(), death disables the player's control scripts (movement + attack)
and stops the Rigidbody2D. Revive() restores control when you add respawns. */

public class PlayerHealth : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;

    [Header("Health Bar")]
    [SerializeField] private Slider healthBar;
    [Tooltip("Optional. Leave empty (or tick Always Show Bar) to keep the bar visible.")]
    [SerializeField] private CanvasGroup healthBarVisibility;
    [SerializeField] private bool alwaysShowBar = true;
    [SerializeField] private float visibleTime = 2f;
    [SerializeField] private float fadeTime = 1f;

    [Header("Death")]
    [SerializeField] private Animator animator;
    [Tooltip("Scripts disabled on death, e.g. TopDownNetworkPlayer2D and PlayerAttack.")]
    [SerializeField] private MonoBehaviour[] disableOnDeath;

    private float currentHealth;
    private bool isAlive = true;
    public bool IsAlive => isAlive;

    private Coroutine hideHealthBarCoroutine;

    private void Start()
    {
        currentHealth = maxHealth;

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }

        if (healthBar != null)
        {
            healthBar.minValue = 0f;
            healthBar.maxValue = maxHealth;
            healthBar.value = currentHealth;
        }

        if (healthBarVisibility != null)
        {
            healthBarVisibility.alpha = alwaysShowBar ? 1f : 0f;
        }
    }

    public void TakeDamage(float damage)
    {
        if (!isAlive || damage <= 0f)
        {
            return;
        }

        currentHealth = Mathf.Clamp(
            currentHealth - damage,
            0f,
            maxHealth
        );

        if (healthBar != null)
        {
            healthBar.value = currentHealth;
        }

        ShowHealthBar();

        Debug.Log(gameObject.name + " health: " + currentHealth);

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    public void Heal(float amount)
    {
        if (!isAlive || amount <= 0f)
        {
            return;
        }

        currentHealth = Mathf.Clamp(
            currentHealth + amount,
            0f,
            maxHealth
        );

        if (healthBar != null)
        {
            healthBar.value = currentHealth;
        }

        ShowHealthBar();
    }

    private void ShowHealthBar()
    {
        if (healthBarVisibility == null || alwaysShowBar)
        {
            return;
        }

        if (hideHealthBarCoroutine != null)
        {
            StopCoroutine(hideHealthBarCoroutine);
        }

        healthBarVisibility.alpha = 1f;
        hideHealthBarCoroutine = StartCoroutine(HideHealthBar());
    }

    private IEnumerator HideHealthBar()
    {
        yield return new WaitForSeconds(visibleTime);

        float timer = 0f;

        while (timer < fadeTime)
        {
            timer += Time.deltaTime;

            healthBarVisibility.alpha = Mathf.Lerp(
                1f,
                0f,
                timer / fadeTime
            );

            yield return null;
        }

        healthBarVisibility.alpha = 0f;
        hideHealthBarCoroutine = null;
    }

    private void Die()
    {
        isAlive = false;

        if (animator != null)
        {
            animator.SetTrigger("Die");
        }

        // Stop control: movement, attack, etc.
        if (disableOnDeath != null)
        {
            foreach (MonoBehaviour behaviour in disableOnDeath)
            {
                if (behaviour != null)
                {
                    behaviour.enabled = false;
                }
            }
        }

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        Debug.Log(gameObject.name + " died.");
    }

    // Call this from a respawn system later to bring the player back.
    public void Revive()
    {
        isAlive = true;
        currentHealth = maxHealth;

        if (healthBar != null)
        {
            healthBar.value = currentHealth;
        }

        if (animator != null)
        {
            animator.ResetTrigger("Die");
        }

        if (disableOnDeath != null)
        {
            foreach (MonoBehaviour behaviour in disableOnDeath)
            {
                if (behaviour != null)
                {
                    behaviour.enabled = true;
                }
            }
        }
    }
}
