using System.Collections;
using DeadmansTales.Networking;
using UnityEngine;

/// <summary>
/// End-of-run UI for the kraken arena.
///
/// Killing the kraken does not win the run by itself -- it only opens the way.
/// The run is finished when a player actually steps into the arena's victory
/// portal (a NetworkStagePortal with Completes Run enabled), which raises
/// <see cref="NetworkStagePortal.RunCompleted"/> on every peer and flips
/// NetworkRunState.Status to Completed. This listens for both: the event is
/// the immediate signal, the run status is the durable one a late-arriving
/// listener can still read.
/// </summary>
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
    private NetworkRunState runState;
    private bool bossBannerShown;
    private bool victoryShown;

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

    private void OnEnable()
    {
        NetworkStagePortal.RunCompleted += ShowVictory;
    }

    private void OnDisable()
    {
        NetworkStagePortal.RunCompleted -= ShowVictory;
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
        }
        else
        {
            kraken.Defeated += HandleBossDefeated;
        }

        StartCoroutine(WatchRunStatus());
    }

    private void OnDestroy()
    {
        if (kraken != null)
        {
            kraken.Defeated -= HandleBossDefeated;
        }

        if (runState != null)
        {
            runState.Status.OnValueChanged -= HandleRunStatusChanged;
        }
    }

    /// <summary>
    /// NetworkRunState is spawned by the network session rather than placed in
    /// this scene, so it may not exist yet when this UI wakes up.
    /// </summary>
    private IEnumerator WatchRunStatus()
    {
        while (runState == null)
        {
            NetworkRunState candidate = NetworkRunState.Instance;

            if (candidate != null && candidate.IsSpawned)
            {
                runState = candidate;
                break;
            }

            yield return new WaitForSecondsRealtime(0.25f);
        }

        runState.Status.OnValueChanged += HandleRunStatusChanged;

        if (runState.Status.Value == NetworkRunStatus.Completed)
        {
            ShowVictory();
        }
    }

    private void HandleRunStatusChanged(
        NetworkRunStatus previousStatus,
        NetworkRunStatus currentStatus
    )
    {
        if (currentStatus == NetworkRunStatus.Completed)
        {
            ShowVictory();
        }
    }

    private void HandleBossDefeated()
    {
        if (bossBannerShown || victoryShown)
        {
            return;
        }

        bossBannerShown = true;

        // When both fields point at the same object, the "boss defeated"
        // banner IS the victory screen -- showing it on the kill would spoil
        // the portal. Only announce when there is a separate banner to show.
        if (bossDefeatedPanel == null || bossDefeatedPanel == victoryPanel)
        {
            return;
        }

        StartCoroutine(ShowBossDefeatedBanner());
    }

    private IEnumerator ShowBossDefeatedBanner()
    {
        bossDefeatedPanel.SetActive(true);

        yield return new WaitForSecondsRealtime(
            Mathf.Max(0f, bossDefeatedDisplaySeconds)
        );

        // The victory screen may have taken over while the banner was up.
        if (!victoryShown)
        {
            bossDefeatedPanel.SetActive(false);
        }
    }

    private void ShowVictory()
    {
        if (victoryShown)
        {
            return;
        }

        victoryShown = true;

        if (bossDefeatedPanel != null && bossDefeatedPanel != victoryPanel)
        {
            bossDefeatedPanel.SetActive(false);
        }

        if (pauseMenu != null)
        {
            pauseMenu.SetDeathScreenBlocking(true);
        }

        if (hotbar != null)
        {
            hotbar.SetActive(false);
        }

        if (victoryPanel != null)
        {
            victoryPanel.SetActive(true);
        }
    }
}
