using UnityEngine;
using UnityEngine.SceneManagement;
public class UI : MonoBehaviour
{
    public void PlayGame()
    {
        SceneManager.LoadScene("Lobby_Island_2D");
    }

    public void OpenLevelSelect()
    {
        Debug.Log("Level Select");
    }

    public void OpenSettings()
    {
        Debug.Log("Settings");
    }

    public void ExitGame()
    {
        Debug.Log("Exit");
        Application.Quit();
    }
}
