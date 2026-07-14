using UnityEngine;
using TMPro;
public class MainMenuManager : MonoBehaviour
{
    public GameObject mainMenuPanel;
    public GameObject levelSelectPanel;
    public GameObject multiplayerPanel;
    public GameObject connectionOptions;
    public GameObject clientOptions;
    public GameObject hostOptions;
    public GameObject joinCodeOptions;
    public TMP_InputField lobbyCodeInput;

    private void Start()
    {
        ShowMainMenu();
    }

    public void ShowMainMenu()
    {
        mainMenuPanel.SetActive(true);
        levelSelectPanel.SetActive(false);
        multiplayerPanel.SetActive(false);
    }

    public void ShowLevelSelectMenu()
    {
        mainMenuPanel.SetActive(false);
        levelSelectPanel.SetActive(true);
        multiplayerPanel.SetActive(false);
    }

    public void ShowMultiplayerMenu()
    {
        mainMenuPanel.SetActive(false);
        levelSelectPanel.SetActive(false);
        multiplayerPanel.SetActive(true);
        ShowConnectionOptions();
    }

    public void ShowConnectionOptions()
    {
        connectionOptions.SetActive(true);
        joinCodeOptions.SetActive(false);
        clientOptions.SetActive(false);
        hostOptions.SetActive(false);

    }

    public void ShowJoinCodeOptions()
    {
        connectionOptions.SetActive(false);
        joinCodeOptions.SetActive(true);
        clientOptions.SetActive(false);
        hostOptions.SetActive(false);
    }

    // Temporary
    public void PreviewHostLobby()
    {
        connectionOptions.SetActive(false);
        joinCodeOptions.SetActive(false);
        clientOptions.SetActive(false);
        hostOptions.SetActive(true);
    }

    // Temporary 
    public void PreviewClientLobby()
    {
        connectionOptions.SetActive(false);
        joinCodeOptions.SetActive(false);
        clientOptions.SetActive(true);
        hostOptions.SetActive(false);
    }


    public void LeaveLobby()
    {
        ShowMultiplayerMenu();
        // Network disconnect needs to be added. 
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