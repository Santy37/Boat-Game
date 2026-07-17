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

    public static bool InputBlocked { get; private set; }

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
        InputBlocked = true;
    }

    public void OpenLevelSelect()
    {
        Debug.Log("Level Select");
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
        InputBlocked = false;
    }

    public async void ReturnToMainMenu()
    {
        if (returningToMainMenu)
        {
            return;
        }

        returningToMainMenu = true;
        InputBlocked = true;

        OnlineLobbyService lobbyService = OnlineLobbyService.Instance;

        if (lobbyService != null && lobbyService.IsInSession)
        {
            await lobbyService.LeaveLobbyAsync();
        }

        if (
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
    }
}
