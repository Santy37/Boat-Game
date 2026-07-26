using System.Collections;
using Unity.Netcode;
using UnityEngine;

public sealed class PlayerDamageFlashUI : MonoBehaviour
{
    [Header("Reference")]
    [SerializeField]
    private CanvasGroup damageFlash;

    [Header("Damage Intensity")]
    [SerializeField]
    [Range(0f, 1f)]
    private float minimumHitIncrease = 0.2f;

    [SerializeField]
    [Range(0f, 1f)]
    private float maximumHitIncrease = 0.55f;

    [SerializeField]
    [Range(0.01f, 1f)]
    private float fullStrengthDamagePercent = 0.25f;

    [SerializeField]
    [Range(0f, 1f)]
    private float maximumDamageAlpha = 0.75f;

    [Header("Timing")]
    [SerializeField]
    [Min(0f)]
    private float holdTime = 0.2f;

    [SerializeField]
    [Min(0.01f)]
    private float fadeDuration = 1.5f;

    [Header("Death")]
    [SerializeField]
    [Range(0f, 1f)]
    private float deathAlpha = 1f;

    private PlayerHealth localPlayerHealth;
    private float damageIntensity;
    private float lastDamageTime;
    private bool playerIsDead;

    private void Awake()
    {
        if (damageFlash != null)
        {
            damageFlash.alpha = 0f;
            damageFlash.interactable = false;
            damageFlash.blocksRaycasts = false;
        }
    }

    private void Start()
    {
        StartCoroutine(FindLocalPlayer());
    }

    private void Update()
    {
        if (damageFlash == null)
        {
            return;
        }

        // At zero health, keep the effect permanently visible.
        if (playerIsDead)
        {
            damageIntensity = deathAlpha;
            damageFlash.alpha = damageIntensity;
            return;
        }

        bool holdFinished =
            Time.unscaledTime >=
            lastDamageTime + holdTime;

        if (holdFinished && damageIntensity > 0f)
        {
            float fadeSpeed =
                1f / Mathf.Max(0.01f, fadeDuration);

            damageIntensity = Mathf.MoveTowards(
                damageIntensity,
                0f,
                fadeSpeed * Time.unscaledDeltaTime
            );
        }

        damageFlash.alpha = damageIntensity;
    }

    private IEnumerator FindLocalPlayer()
    {
        while (localPlayerHealth == null)
        {
            NetworkManager networkManager =
                NetworkManager.Singleton;

            if (
                networkManager != null &&
                networkManager.IsListening &&
                networkManager.LocalClient != null &&
                networkManager.LocalClient.PlayerObject != null
            )
            {
                localPlayerHealth =
                    networkManager.LocalClient.PlayerObject
                        .GetComponent<PlayerHealth>();
            }

            yield return null;
        }

        localPlayerHealth.CurrentHealth.OnValueChanged +=
            HandleHealthChanged;

        playerIsDead =
            localPlayerHealth.CurrentHealth.Value <= 0f;

        damageIntensity =
            playerIsDead ? deathAlpha : 0f;

        damageFlash.alpha = damageIntensity;
    }

    private void HandleHealthChanged(
        float previousHealth,
        float currentHealth
    )
    {
        if (
            damageFlash == null ||
            localPlayerHealth == null
        )
        {
            return;
        }

        // Death ignores the normal cap and uses Death Alpha.
        if (currentHealth <= 0f)
        {
            playerIsDead = true;
            damageIntensity = deathAlpha;
            damageFlash.alpha = damageIntensity;
            return;
        }

        // Remove the effect if the player is revived.
        if (previousHealth <= 0f)
        {
            playerIsDead = false;
            damageIntensity = 0f;
            damageFlash.alpha = 0f;
            return;
        }

        // Healing does not add any darkness.
        if (currentHealth >= previousHealth)
        {
            return;
        }

        float damageTaken =
            previousHealth - currentHealth;

        float maximumHealth = Mathf.Max(
            1f,
            localPlayerHealth.MaximumHealth
        );

        float damagePercent =
            damageTaken / maximumHealth;

        float damageStrength = Mathf.Clamp01(
            damagePercent /
            fullStrengthDamagePercent
        );

        float addedIntensity = Mathf.Lerp(
            minimumHitIncrease,
            maximumHitIncrease,
            damageStrength
        );

        // New damage only makes the effect darker.
        // It cannot exceed Maximum Damage Alpha while alive.
        damageIntensity = Mathf.Min(
            maximumDamageAlpha,
            damageIntensity + addedIntensity
        );

        lastDamageTime = Time.unscaledTime;
        damageFlash.alpha = damageIntensity;
    }

    private void OnDestroy()
    {
        if (localPlayerHealth != null)
        {
            localPlayerHealth.CurrentHealth
                .OnValueChanged -= HandleHealthChanged;
        }
    }
}