using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Server-authoritative health for a network player.
///
/// Only the server changes CurrentHealth. Every client renders the synchronized
/// value and applies the same alive/dead presentation. This prevents one client
/// from dying while the host and other clients still see that player alive.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public sealed class PlayerHealth : NetworkBehaviour
{
    private static readonly int DieTrigger = Animator.StringToHash("Die");

    [Header("Health")]
    [SerializeField]
    [Min(1f)]
    private float maxHealth = 100f;

    [Header("Health Bar")]
    [SerializeField]
    private Slider healthBar;

    [SerializeField]
    [Tooltip("Optional. Leave empty (or enable Always Show Bar) to keep it visible.")]
    private CanvasGroup healthBarVisibility;

    [SerializeField]
    private bool alwaysShowBar = true;

    [SerializeField]
    [Min(0f)]
    private float visibleTime = 2f;

    [SerializeField]
    [Min(0.01f)]
    private float fadeTime = 1f;

    [Header("Death")]
    [SerializeField]
    private Animator animator;

    [SerializeField]
    [Tooltip("Owner-control scripts disabled while this player is dead.")]
    private MonoBehaviour[] disableOnDeath;

    public readonly NetworkVariable<float> CurrentHealth =
        new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    public bool IsAlive =>
        IsSpawned && CurrentHealth.Value > 0f;

    public float MaximumHealth =>
        Mathf.Max(1f, maxHealth) +
        (loadout != null ? loadout.BonusMaxHealth : 0f);

    private NetworkPlayerLoadout loadout;
    private Coroutine hideHealthBarCoroutine;
    private bool deathPresentationApplied;
    private bool hasDeathTrigger;

    private void Awake()
    {
        loadout = GetComponent<NetworkPlayerLoadout>();

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }

        hasDeathTrigger = HasTrigger(animator, DieTrigger);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        CurrentHealth.OnValueChanged += HandleHealthChanged;

        if (IsServer && CurrentHealth.Value <= 0f)
        {
            CurrentHealth.Value = MaximumHealth;
        }

        ApplyHealthPresentation(CurrentHealth.Value, false);
    }

    public override void OnNetworkDespawn()
    {
        CurrentHealth.OnValueChanged -= HandleHealthChanged;

        if (hideHealthBarCoroutine != null)
        {
            StopCoroutine(hideHealthBarCoroutine);
            hideHealthBarCoroutine = null;
        }

        base.OnNetworkDespawn();
    }

    /// <summary>
    /// Applies damage on the authoritative server. Callers on clients are
    /// ignored so client-side trigger callbacks cannot mutate shared health.
    /// </summary>
    public bool TakeDamage(float damage)
    {
        if (!IsSpawned || !IsServer || damage <= 0f || !IsAlive)
        {
            return false;
        }

        CurrentHealth.Value = Mathf.Clamp(
            CurrentHealth.Value - damage,
            0f,
            MaximumHealth
        );

        return true;
    }

    public bool Heal(float amount)
    {
        if (!IsSpawned || !IsServer || amount <= 0f || !IsAlive)
        {
            return false;
        }

        CurrentHealth.Value = Mathf.Clamp(
            CurrentHealth.Value + amount,
            0f,
            MaximumHealth
        );

        return true;
    }

    public bool Revive()
    {
        if (!IsSpawned || !IsServer)
        {
            return false;
        }

        CurrentHealth.Value = MaximumHealth;
        return true;
    }

    private void HandleHealthChanged(float previousValue, float currentValue)
    {
        ApplyHealthPresentation(currentValue, true);
    }

    private void ApplyHealthPresentation(float health, bool showTemporaryBar)
    {
        float safeMaximum = MaximumHealth;

        if (healthBar != null)
        {
            healthBar.minValue = 0f;
            healthBar.maxValue = safeMaximum;
            healthBar.value = Mathf.Clamp(health, 0f, safeMaximum);
        }

        bool alive = health > 0f;

        if (alive)
        {
            if (deathPresentationApplied)
            {
                deathPresentationApplied = false;

                if (hasDeathTrigger)
                {
                    animator.ResetTrigger(DieTrigger);
                }

                SetControlScriptsEnabled(true);

                Rigidbody2D revivedBody = GetComponent<Rigidbody2D>();
                if (revivedBody != null)
                {
                    revivedBody.constraints =
                        RigidbodyConstraints2D.FreezeRotation;
                }
            }

            if (healthBarVisibility != null)
            {
                healthBarVisibility.alpha = alwaysShowBar ? 1f : 0f;
            }

            if (showTemporaryBar)
            {
                ShowHealthBar();
            }

            return;
        }

        if (deathPresentationApplied)
        {
            return;
        }

        deathPresentationApplied = true;

        if (hasDeathTrigger)
        {
            animator.SetTrigger(DieTrigger);
        }

        SetControlScriptsEnabled(false);

        Rigidbody2D body = GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;

            // A dead body must not slide when live enemies or players bump
            // into it. Control scripts are disabled, so nothing would ever
            // stop the drift otherwise.
            body.constraints = RigidbodyConstraints2D.FreezeAll;
        }

        Debug.Log($"[Player Health] {name} died on the server.", this);
    }

    private void SetControlScriptsEnabled(bool enabled)
    {
        if (disableOnDeath == null)
        {
            return;
        }

        foreach (MonoBehaviour behaviour in disableOnDeath)
        {
            if (behaviour != null)
            {
                behaviour.enabled = enabled;
            }
        }
    }

    private static bool HasTrigger(Animator target, int parameterHash)
    {
        if (target == null)
        {
            return false;
        }

        foreach (AnimatorControllerParameter parameter in target.parameters)
        {
            if (
                parameter.nameHash == parameterHash &&
                parameter.type == AnimatorControllerParameterType.Trigger
            )
            {
                return true;
            }
        }

        return false;
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
        float safeFadeTime = Mathf.Max(0.01f, fadeTime);

        while (timer < safeFadeTime)
        {
            timer += Time.deltaTime;
            healthBarVisibility.alpha = Mathf.Lerp(
                1f,
                0f,
                timer / safeFadeTime
            );
            yield return null;
        }

        healthBarVisibility.alpha = 0f;
        hideHealthBarCoroutine = null;
    }
}
