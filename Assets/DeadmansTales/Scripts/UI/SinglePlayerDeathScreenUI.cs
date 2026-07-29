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
    private DeadmansTales.Ship.NetworkShipHealth playerShipHealth;
    private bool shipHasSunk;
    private PauseMenu pauseMenu;
    private bool deathScreenIsBlocking;

    private void Awake()
    {
        if (deathPanel != null)
        {
            deathPanel.SetActive(false);
        }
    }

    /// <summary>
    /// Losing the ship is the run's other lose state, and NetworkShipHealth
    /// already flips the run to Failed when it happens -- but nothing was
    /// listening, so the crew's ship could go down with no screen at all.
    /// Same panel as dying: the run is over either way.
    ///
    /// Resolved through PlayerShipMarker rather than a bare
    /// NetworkShipHealth search, because enemy ships carry that same
    /// component and sinking one of those must not end the player's run.
    /// Polled on an interval, since the ship spawns after this UI does and
    /// scenes with no ship at all (the islands) would otherwise run a
    /// FindFirstObjectByType every frame forever.
    /// </summary>
    private IEnumerator FindPlayerShip()
    {
        WaitForSecondsRealtime pollInterval =
            new WaitForSecondsRealtime(0.5f);

        while (playerShipHealth == null)
        {
            DeadmansTales.Ship.PlayerShipMarker marker =
                FindFirstObjectByType<DeadmansTales.Ship.PlayerShipMarker>();

            if (marker != null)
            {
                playerShipHealth =
                    marker.GetComponent<DeadmansTales.Ship.NetworkShipHealth>();
            }

            if (playerShipHealth == null)
            {
                yield return pollInterval;
            }
        }

        playerShipHealth.ShipSunk += HandleShipSunk;

        // It may already have gone down before this resolved.
        if (playerShipHealth.IsSunk)
        {
            HandleShipSunk();
        }
    }

    private void HandleShipSunk()
    {
        shipHasSunk = true;
        UpdateDeathScreen(
            localPlayerHealth != null
                ? localPlayerHealth.CurrentHealth.Value
                : 0f);
    }

    private void Start()
    {
        StartCoroutine(FindLocalPlayer());
        StartCoroutine(FindPlayerShip());
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

        // The two lose states are not equally personal. One player going down
        // in co-op is that player's problem and the crew fights on, so their
        // death screen stays solo-only. Losing the SHIP ends the run for
        // everybody aboard and never recovers, so that one shows in an online
        // session too, where it used to show nothing at all.
        bool playerDied =
            shipHasSunk ||
            (!isOnlineMultiplayer && health <= 0f);

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

            // Only unblock the pause menu if the death screen
            // had previously blocked it.
            if (deathScreenIsBlocking)
            {
                deathScreenIsBlocking = false;
                SetPauseMenuBlocked(false);
            }

            return;
        }

        // Only close/block the pause menu when the player actually dies.
        if (!deathScreenIsBlocking)
        {
            deathScreenIsBlocking = true;
            SetPauseMenuBlocked(true);
        }

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

        if (playerShipHealth != null)
        {
            playerShipHealth.ShipSunk -= HandleShipSunk;
        }
    }
}