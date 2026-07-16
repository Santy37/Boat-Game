using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class Enemy : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;

    [Header("Health Bar")]
    [SerializeField] private Slider healthBar;
    [SerializeField] private CanvasGroup healthBarVisibility;
    [SerializeField] private float visibleTime = 2f;
    [SerializeField] private float fadeTime = 1f;

    [Header("Death")]
    [SerializeField] private Animator animator;
    [SerializeField] private float deathDelay = 0.75f;

    private float currentHealth;
    private bool isAlive = true;
    private Coroutine hideHealthBarCoroutine;
    public bool IsAlive => isAlive;
    private void Start()
    {
        currentHealth = maxHealth;

        if (healthBar != null)
        {
            healthBar.minValue = 0f;
            healthBar.maxValue = maxHealth;
            healthBar.value = currentHealth;
        }

        if (healthBarVisibility != null)
        {
            healthBarVisibility.alpha = 0f;
        }
    }

    public void TakeDamage(float damage)
    {
        if (!isAlive || damage <= 0f)
        {
            return;
        }

        currentHealth -= damage;
        currentHealth = Mathf.Clamp(
            currentHealth,
            0f,
            maxHealth
        );

        if (healthBar != null)
        {
            healthBar.value = currentHealth;
        }

        ShowHealthBar();

        Debug.Log(
            gameObject.name + " health: " + currentHealth
        );

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    private void ShowHealthBar()
    {
        if (healthBarVisibility == null)
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

        Collider2D[] enemyColliders =
            GetComponentsInChildren<Collider2D>();

        foreach (Collider2D enemyCollider in enemyColliders)
        {
            enemyCollider.enabled = false;
        }

        if (animator != null)
        {
            animator.SetTrigger("Die");
        }

        Debug.Log(gameObject.name + " died.");

        Destroy(gameObject, deathDelay);
    }
}