using UnityEngine;

public class MainMenuManager : MonoBehaviour
{
    public GameObject mainMenuPanel;
    public GameObject playMenuPanel;
    public GameObject multiplayerMenuPanel;
    public GameObject levelSelectPanel;
    public GameObject lobbyRoomPanel;

    private void Start()
    {
        ShowMainMenu();
    }

    public void ShowMainMenu()
    {
        mainMenuPanel.SetActive(true);
        playMenuPanel.SetActive(false);
        multiplayerMenuPanel.SetActive(false);
        levelSelectPanel.SetActive(false);
        lobbyRoomPanel.SetActive(false);
    }

    public void ShowPlayMenu()
    {
        mainMenuPanel.SetActive(false);
        playMenuPanel.SetActive(true);
        multiplayerMenuPanel.SetActive(false);
        levelSelectPanel.SetActive(false);
        lobbyRoomPanel.SetActive(false);
    }

    public void ShowMultiplayerMenu()
    {
        mainMenuPanel.SetActive(false);
        playMenuPanel.SetActive(false);
        multiplayerMenuPanel.SetActive(true);
        levelSelectPanel.SetActive(false);
        lobbyRoomPanel.SetActive(false);
    }

    public void ShowLevelSelectMenu()
    {
        mainMenuPanel.SetActive(false);
        playMenuPanel.SetActive(false);
        multiplayerMenuPanel.SetActive(false);
        levelSelectPanel.SetActive(true);
        lobbyRoomPanel.SetActive(false);
    }

    public void ShowLobbyRoom()
    {
        mainMenuPanel.SetActive(false);
        playMenuPanel.SetActive(false);
        multiplayerMenuPanel.SetActive(false);
        levelSelectPanel.SetActive(false);
        lobbyRoomPanel.SetActive(true);
    }

    public void ExitGame()
    {
        Debug.Log("Exit");
        Application.Quit();
    }
    public void OpenSettings()
    {

        Debug.Log("Settings");

    }
}