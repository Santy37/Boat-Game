using System.Collections;
using DeadmansTales.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class SinglePlayerDeathScreenUI : MonoBehaviour
{
    [SerializeField]
    private GameObject deathPanel;

    [SerializeField]
    private float deathScreenDelay = 1.5f;

    private Coroutine showDeathScreenCoroutine;
    private PlayerHealth localPlayerHealth;
    private PauseMenu pauseMenu;

    private void Awake()
    {
        if (deathPanel != null)
        {
            deathPanel.SetActive(false);
        }
    }

    private void Start()
    {
        StartCoroutine(FindLocalPlayer());
    }

    private IEnumerator FindLocalPlayer()
    {
        while (localPlayerHealth == null)
        {
            NetworkManager networkManager = NetworkManager.Singleton;

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

        UpdateDeathScreen(localPlayerHealth.CurrentHealth.Value);
    }

    private void HandleHealthChanged(
        float previousHealth,
        float currentHealth
    )
    {
        UpdateDeathScreen(currentHealth);
    }

    private void UpdateDeathScreen(float health)
    {
        bool isOnlineMultiplayer =
            OnlineLobbyService.Instance != null &&
            OnlineLobbyService.Instance.IsInSession;

        bool playerDied =
            !isOnlineMultiplayer && health <= 0f;

        if (!playerDied)
        {
            if (showDeathScreenCoroutine != null)
            {
                StopCoroutine(showDeathScreenCoroutine);
                showDeathScreenCoroutine = null;
            }

            if (deathPanel != null)
            {
                deathPanel.SetActive(false);
            }

            SetPauseMenuBlocked(false);
            return;
        }

        // Immediately prevent the pause menu from opening.
        SetPauseMenuBlocked(true);

        if (showDeathScreenCoroutine == null)
        {
            showDeathScreenCoroutine =
                StartCoroutine(ShowDeathScreenAfterDelay());
        }
    }

    private IEnumerator ShowDeathScreenAfterDelay()
    {
        yield return new WaitForSecondsRealtime(deathScreenDelay);

        if (deathPanel != null)
        {
            deathPanel.SetActive(true);
        }

        showDeathScreenCoroutine = null;
    }

    private void SetPauseMenuBlocked(bool blocked)
    {
        if (pauseMenu == null)
        {
            pauseMenu = FindFirstObjectByType<PauseMenu>(
                FindObjectsInactive.Include
            );
        }

        if (pauseMenu != null)
        {
            pauseMenu.SetDeathScreenBlocking(blocked);
        }
    }

    public void OpenLevelSelect()
    {
        PlayerPrefs.SetInt("OpenLevelSelectAfterDeath", 1);
        ReturnToMenu();
    }

    public void OpenMainMenu()
    {
        PlayerPrefs.DeleteKey("OpenLevelSelectAfterDeath");
        ReturnToMenu();
    }

    private void ReturnToMenu()
    {
        Time.timeScale = 1f;

        NetworkManager networkManager = NetworkManager.Singleton;

        if (networkManager != null && networkManager.IsListening)
        {
            networkManager.Shutdown();
        }

        SceneManager.LoadScene("MainMenu");
    }

    private void OnDestroy()
    {
        if (localPlayerHealth != null)
        {
            localPlayerHealth.CurrentHealth.OnValueChanged -=
                HandleHealthChanged;
        }
    }
}