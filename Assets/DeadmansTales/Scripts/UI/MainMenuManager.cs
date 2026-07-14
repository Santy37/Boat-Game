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
    public TMP_Text lobbyCodeText;
    public TMP_Text createOrJoinText;
    public TMP_Text playerListText;
    public TMP_Text enterCodeText;

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

        createOrJoinText.gameObject.SetActive(true);
        playerListText.gameObject.SetActive(false);
        enterCodeText.gameObject.SetActive(false);

        lobbyCodeText.text = "LOBBY";
        lobbyCodeText.gameObject.SetActive(true);
    }

    public void ShowCreatedLobby()
    {
        connectionOptions.SetActive(false);
        joinCodeOptions.SetActive(false);
        clientOptions.SetActive(false);
        hostOptions.SetActive(true);

        createOrJoinText.gameObject.SetActive(false);
        playerListText.gameObject.SetActive(true);
        enterCodeText.gameObject.SetActive(false);

        // Temporary until real networking is added.
        lobbyCodeText.text = "LOBBY CODE: ABCD12";
        lobbyCodeText.gameObject.SetActive(true);
    }
    public void ShowJoinCodeOptions()
    {
        connectionOptions.SetActive(false);
        joinCodeOptions.SetActive(true);
        clientOptions.SetActive(false);
        hostOptions.SetActive(false);

        createOrJoinText.gameObject.SetActive(false);
        playerListText.gameObject.SetActive(false);
        enterCodeText.gameObject.SetActive(true);

        lobbyCodeText.text = "LOBBY";
        lobbyCodeText.gameObject.SetActive(true);
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