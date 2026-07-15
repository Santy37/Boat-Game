using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using Unity.Netcode;
public class PauseMenu : MonoBehaviour
{
    public GameObject pauseMenuPanel;
    public GameObject pauseButton;
    private bool menuIsOpen ;
    public static bool InputBlocked { get; private set; }

    private void Start()
    {
        ResumeGame();
    }

    private void Update()
    {
        if (Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            TogglePauseMenu();
        }
    }

    public void TogglePauseMenu()
    {
        if(menuIsOpen)
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
        menuIsOpen = false;
        pauseMenuPanel.SetActive(false);
        pauseButton.SetActive(true);
        InputBlocked = false;
    }

    public void ReturnToMainMenu()
    {
        InputBlocked = false;

        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

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
