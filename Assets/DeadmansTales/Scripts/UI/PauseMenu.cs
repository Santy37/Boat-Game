using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

public class PauseMenu : MonoBehaviour
{
    public GameObject pauseMenuPanel;
    public GameObject pauseButton;
    private bool menuIsOpen ;

    private void Start()
    {
        ResumeGame();
    }

    private void Update()
    {
        if(Keyboard.current.escapeKey.wasPressedThisFrame)
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
    }

    public void  ReturnToMainMenu()
    {
        SceneManager.LoadScene("MainMenu");
    }

    public void OpenSettings()
    {
        Debug.Log("Settings");
    }


    public void RestartLevel()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }


 
}
