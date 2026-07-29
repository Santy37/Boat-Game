using System.Collections;
using UnityEngine;

public sealed class BossDefeatedUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField]
    private GameObject bossDefeatedPanel;

    [SerializeField]
    private GameObject victoryPanel;

    [SerializeField]
    private GameObject hotbar;

    [Header("Pause Menu")]
    [SerializeField]
    private PauseMenu pauseMenu;

    [Header("Timing")]
    [SerializeField]
    [Min(0f)]
    private float bossDefeatedDisplaySeconds = 2f;

    private KrakenHealth kraken;
    private bool victorySequenceStarted;

    private void Awake()
    {
        if (bossDefeatedPanel != null)
        {
            bossDefeatedPanel.SetActive(false);
        }

        if (victoryPanel != null)
        {
            victoryPanel.SetActive(false);
        }
    }

    private void Start()
    {
        kraken = FindFirstObjectByType<KrakenHealth>();

        if (kraken == null)
        {
            Debug.LogWarning(
                "[Boss Defeated UI] No KrakenHealth found.",
                this
            );

            return;
        }

        kraken.Defeated += HandleBossDefeated;
    }

    private void OnDestroy()
    {
        if (kraken != null)
        {
            kraken.Defeated -= HandleBossDefeated;
        }
    }

    private void HandleBossDefeated()
    {
        if (victorySequenceStarted)
        {
            return;
        }

        victorySequenceStarted = true;

        if (pauseMenu != null)
        {
            pauseMenu.SetDeathScreenBlocking(true);
        }

        if (hotbar != null)
        {
            hotbar.SetActive(false);
        }

        StartCoroutine(ShowVictorySequence());
    }

    private IEnumerator ShowVictorySequence()
    {
        if (bossDefeatedPanel != null)
        {
            bossDefeatedPanel.SetActive(true);
        }

        yield return new WaitForSecondsRealtime(
            Mathf.Max(0f, bossDefeatedDisplaySeconds)
        );

        if (bossDefeatedPanel != null)
        {
            bossDefeatedPanel.SetActive(false);
        }

        if (victoryPanel != null)
        {
            victoryPanel.SetActive(true);
        }
    }
}