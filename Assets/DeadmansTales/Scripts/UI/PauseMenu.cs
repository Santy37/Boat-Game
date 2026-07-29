using DeadmansTales.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class PauseMenu : MonoBehaviour
{
    public GameObject pauseMenuPanel;
    public GameObject pauseButton;

    private bool menuIsOpen;
    private bool returningToMainMenu;
    private bool deathScreenBlocking;
    public static bool InputBlocked { get; private set; }

    [SerializeField]
    private GameObject hotbar;

    private void Start()
    {
        ResumeGame();
    }

    private void Update()
    {
        if (
            Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame
        )
        {
            TogglePauseMenu();
        }
    }

    public void TogglePauseMenu()
    {
        if (deathScreenBlocking)
        {
            return;
        }

        if (returningToMainMenu)
        {
            return;
        }

        if (menuIsOpen)
        {
            ResumeGame();
        }
        else
        {
            OpenPauseMenu();
        }
    }

    public void OpenPauseMenu()
    {
        menuIsOpen = true;
        pauseMenuPanel.SetActive(true);
        pauseButton.SetActive(false);

        if (hotbar != null)
        {
            hotbar.SetActive(false);
        }

        InputBlocked = true;
        ApplyTimeScale();
    }

    /// <summary>
    /// Blocking input alone left the world running behind the panel: enemies
    /// kept moving, attacking and landing hits on a "paused" player. Freezing
    /// time fixes that, but only solo -- Time.timeScale is local, so in an
    /// online session it would stall this peer against a world that keeps
    /// advancing, and one player opening a menu must not stop everyone else's
    /// game. Same solo test SinglePlayerDeathScreenUI already uses.
    /// </summary>
    private static bool CanFreezeTime =>
        OnlineLobbyService.Instance == null ||
        !OnlineLobbyService.Instance.IsInSession;

    private void ApplyTimeScale()
    {
        if (!CanFreezeTime)
        {
            return;
        }

        Time.timeScale = menuIsOpen ? 0f : 1f;
    }

    public void OpenLevelSelect()
    {
        PlayerPrefs.SetInt("OpenLevelSelectAfterDeath", 1);
        ReturnToMainMenu();
    }

    public void SetDeathScreenBlocking(bool blocked)
    {
        deathScreenBlocking = blocked;

        if (blocked)
        {
            // Dying (or the boss dying) does close an open pause menu and
            // hands input to the death/victory screen. Time runs again so the
            // death animation and that screen's delay can play out.
            menuIsOpen = false;
            pauseMenuPanel.SetActive(false);
            pauseButton.SetActive(false);
            InputBlocked = true;
            ApplyTimeScale();
            return;
        }

        // Clearing the block must leave an open pause menu alone. This is
        // called on EVERY PlayerHealth change, not just on revive, so closing
        // unconditionally meant any enemy hit while paused threw the player
        // straight back into the fight.
        pauseButton.SetActive(!menuIsOpen);
        InputBlocked = menuIsOpen;
        ApplyTimeScale();
    }

    public void ResumeGame()
    {
        if (returningToMainMenu)
        {
            return;
        }

        menuIsOpen = false;
        pauseMenuPanel.SetActive(false);
        pauseButton.SetActive(true);

        if (hotbar != null)
        {
            hotbar.SetActive(true);
        }

        InputBlocked = false;
        ApplyTimeScale();
    }

    public async void ReturnToMainMenu()
    {
        if (returningToMainMenu)
        {
            return;
        }

        returningToMainMenu = true;
        InputBlocked = true;

        // Leave the pause freeze behind before unwinding the session: the
        // main menu must not inherit a zeroed timeScale.
        menuIsOpen = false;
        Time.timeScale = 1f;

        OnlineLobbyService lobbyService = OnlineLobbyService.Instance;
        bool leftManagedSession = false;

        if (lobbyService != null && lobbyService.IsInSession)
        {
            leftManagedSession = await lobbyService.LeaveLobbyAsync();
        }

        if (
            !leftManagedSession &&
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening
        )
        {
            NetworkManager.Singleton.Shutdown();
        }

        InputBlocked = false;
        SceneManager.LoadScene("MainMenu");
    }

    public void OpenSettings()
    {
        Debug.Log("Settings");
    }

    private void OnDestroy()
    {
        InputBlocked = false;

        // A scene change while paused would otherwise strand the whole game
        // at timeScale 0 with no menu left to unpause it.
        Time.timeScale = 1f;
    }
}
