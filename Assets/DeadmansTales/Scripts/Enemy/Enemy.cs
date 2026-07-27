using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Server-authoritative enemy health and death lifecycle.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public sealed class Enemy : NetworkBehaviour
{
    [Header("Health")]
    [SerializeField]
    [Min(1f)]
    private float maxHealth = 100f;

    [Header("Health Bar")]
    [SerializeField]
    private Slider healthBar;

    [SerializeField]
    private CanvasGroup healthBarVisibility;

    [Header("Death")]
    [SerializeField]
    private Animator animator;

    [SerializeField]
    [Min(0f)]
    private float deathDelay = 0.75f;

    public readonly NetworkVariable<float> CurrentHealth =
        new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    public bool IsAlive =>
        IsSpawned && CurrentHealth.Value > 0f;

    public float MaximumHealth => Mathf.Max(1f, maxHealth);

    private Coroutine despawnCoroutine;
    private Coroutine hitFlashCoroutine;
    private bool deathPresentationApplied;
    private SpriteRenderer[] flashRenderers;
    private Color[] flashOriginalColors;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }

        flashRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        flashOriginalColors = new Color[flashRenderers.Length];
        for (int index = 0; index < flashRenderers.Length; index++)
        {
            flashOriginalColors[index] = flashRenderers[index].color;
        }

        CenterHealthBarOverBody();
    }

    /// <summary>
    /// Centres the health bar over the enemy's VISIBLE body.
    ///
    /// The bar canvas is authored at x = 0 on the prefab root, but the art
    /// lives on an offset child (basicenemy's GFX sits at x 0.709), so every
    /// bar drew noticeably left of the sprite it belonged to. Aligning to the
    /// largest renderer's bounds at spawn fixes the whole family of enemy
    /// prefabs and their variants in one place, whatever their art offset.
    /// </summary>
    private void CenterHealthBarOverBody()
    {
        if (healthBarVisibility == null)
        {
            return;
        }

        SpriteRenderer body = null;
        float bodyArea = 0f;
        foreach (SpriteRenderer renderer in flashRenderers)
        {
            if (renderer == null || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            Vector3 size = renderer.bounds.size;
            float area = size.x * size.y;
            if (area > bodyArea)
            {
                bodyArea = area;
                body = renderer;
            }
        }

        if (body == null)
        {
            return;
        }

        Transform bar = healthBarVisibility.transform;
        Vector3 barPosition = bar.position;
        bar.position = new Vector3(
            body.bounds.center.x,
            barPosition.y,
            barPosition.z
        );
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        CurrentHealth.OnValueChanged += HandleHealthChanged;

        if (IsServer)
        {
            CurrentHealth.Value = MaximumHealth;
        }

        ApplyHealthPresentation(CurrentHealth.Value);
    }

    public override void OnNetworkDespawn()
    {
        CurrentHealth.OnValueChanged -= HandleHealthChanged;

        // Legacy scene-authored enemies are preserved by Despawn(false).
        // Make that preserved shell completely invisible so old scenes cannot
        // leave a standing, non-networked "zombie" behind after death.
        if (deathPresentationApplied && NetworkObject.IsSceneObject == true)
        {
            HideDeathPresentation();
        }

        if (despawnCoroutine != null)
        {
            StopCoroutine(despawnCoroutine);
            despawnCoroutine = null;
        }

        if (hitFlashCoroutine != null)
        {
            StopCoroutine(hitFlashCoroutine);
            hitFlashCoroutine = null;
            RestoreFlashTint();
        }

        base.OnNetworkDespawn();
    }

    public bool TakeDamage(float damage)
    {
        if (!IsSpawned || !IsServer || !IsAlive || damage <= 0f)
        {
            return false;
        }

        CurrentHealth.Value = Mathf.Clamp(
            CurrentHealth.Value - damage,
            0f,
            MaximumHealth
        );

        if (CurrentHealth.Value <= 0f && despawnCoroutine == null)
        {
            despawnCoroutine = StartCoroutine(DespawnAfterDeath());
        }

        return true;
    }

    private void HandleHealthChanged(float previousValue, float currentValue)
    {
        // Every peer sees the same synchronized hit feedback.
        if (currentValue < previousValue && currentValue > 0f)
        {
            if (hitFlashCoroutine != null)
            {
                StopCoroutine(hitFlashCoroutine);
            }

            hitFlashCoroutine = StartCoroutine(PlayHitFlash());
        }

        ApplyHealthPresentation(currentValue);
    }

    private IEnumerator PlayHitFlash()
    {
        SetFlashTint(new Color(1f, 0.35f, 0.35f, 1f));
        yield return new WaitForSeconds(0.08f);
        RestoreFlashTint();
        hitFlashCoroutine = null;
    }

    private void SetFlashTint(Color tint)
    {
        foreach (SpriteRenderer renderer in flashRenderers)
        {
            if (renderer != null)
            {
                renderer.color = tint;
            }
        }
    }

    private void RestoreFlashTint()
    {
        for (int index = 0; index < flashRenderers.Length; index++)
        {
            if (flashRenderers[index] != null)
            {
                flashRenderers[index].color = flashOriginalColors[index];
            }
        }
    }

    private void ApplyHealthPresentation(float health)
    {
        if (healthBar != null)
        {
            healthBar.minValue = 0f;
            healthBar.maxValue = MaximumHealth;
            healthBar.value = Mathf.Clamp(health, 0f, MaximumHealth);
        }

        if (health > 0f)
        {
            // ALWAYS visible while the enemy lives. Every conditional-
            // visibility scheme tried here (show-after-hit timer, then
            // show-while-damaged) kept surfacing as "the bar disappeared" in
            // playtests; a permanently shown bar has no path that can hide it
            // short of the enemy actually dying.
            if (healthBarVisibility != null)
            {
                healthBarVisibility.alpha = 1f;
            }

            return;
        }

        if (deathPresentationApplied)
        {
            return;
        }

        deathPresentationApplied = true;

        if (animator != null)
        {
            animator.SetTrigger("Die");
        }

        foreach (Collider2D enemyCollider in GetComponentsInChildren<Collider2D>())
        {
            enemyCollider.enabled = false;
        }

        Rigidbody2D body = GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }

        Debug.Log($"[Enemy] {name} entered its networked death state.", this);
    }

    private IEnumerator DespawnAfterDeath()
    {
        yield return new WaitForSeconds(Mathf.Max(0f, deathDelay));

        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            // Scene-placed NGO objects must survive as scene objects until the
            // scene unloads. Runtime-spawned enemies can be destroyed normally.
            bool destroyRuntimeObject = NetworkObject.IsSceneObject != true;
            NetworkObject.Despawn(destroyRuntimeObject);
        }

        despawnCoroutine = null;
    }

    private void HideDeathPresentation()
    {
        foreach (Renderer enemyRenderer in GetComponentsInChildren<Renderer>(
            true
        ))
        {
            enemyRenderer.enabled = false;
        }

        if (healthBarVisibility != null)
        {
            healthBarVisibility.alpha = 0f;
            healthBarVisibility.interactable = false;
            healthBarVisibility.blocksRaycasts = false;
        }
    }

}
