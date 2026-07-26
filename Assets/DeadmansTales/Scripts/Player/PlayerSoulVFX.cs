using System.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PlayerHealth))]
public sealed class PlayerSoulVFX : NetworkBehaviour
{
    [Header("Soul Animation")]
    [SerializeField] private Sprite[] soulFrames;

    [SerializeField]
    [Min(1f)]
    private float framesPerSecond = 8f;

    [Header("Movement")]
    [SerializeField]
    private Vector3 startOffset =
        new Vector3(0f, 0.5f, 0f);

    [SerializeField]
    private float riseDistance = 2.5f;

    [SerializeField]
    private float duration = 1.2f;

    [SerializeField]
    private float soulScale = 0.65f;

    private PlayerHealth playerHealth;
    private GameObject activeSoul;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        playerHealth = GetComponent<PlayerHealth>();

        playerHealth.CurrentHealth.OnValueChanged +=
            HandleHealthChanged;
    }

    public override void OnNetworkDespawn()
    {
        if (playerHealth != null)
        {
            playerHealth.CurrentHealth.OnValueChanged -=
                HandleHealthChanged;
        }

        if (activeSoul != null)
        {
            Destroy(activeSoul);
        }

        base.OnNetworkDespawn();
    }

    private void HandleHealthChanged(
        float previousHealth,
        float currentHealth
    )
    {
        if (previousHealth > 0f && currentHealth <= 0f)
        {
            StartCoroutine(PlaySoulEffect());
        }
    }

    private IEnumerator PlaySoulEffect()
    {
        if (soulFrames == null || soulFrames.Length == 0)
        {
            yield break;
        }

        activeSoul = new GameObject("SoulDeathVFX");

        Vector3 startingPosition =
            transform.position + startOffset;

        activeSoul.transform.position = startingPosition;
        activeSoul.transform.localScale =
            Vector3.one * soulScale;

        SpriteRenderer renderer =
            activeSoul.AddComponent<SpriteRenderer>();

        renderer.sprite = soulFrames[0];

        Transform visual = transform.Find("Visual");

        SpriteRenderer playerRenderer =
            visual != null
                ? visual.GetComponent<SpriteRenderer>()
                : GetComponentInChildren<SpriteRenderer>();

        if (playerRenderer != null)
        {
            renderer.sortingLayerID =
                playerRenderer.sortingLayerID;

            renderer.sortingOrder =
                playerRenderer.sortingOrder + 10;
        }

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float progress =
                Mathf.Clamp01(elapsed / duration);

            int frameIndex =
                Mathf.FloorToInt(
                    elapsed * framesPerSecond
                ) % soulFrames.Length;

            renderer.sprite = soulFrames[frameIndex];

            activeSoul.transform.position =
                startingPosition +
                Vector3.up * riseDistance * progress;

            renderer.color =
                new Color(1f, 1f, 1f, 1f - progress);

            yield return null;
        }

        Destroy(activeSoul);
        activeSoul = null;
    }
}