using TMPro;
using UnityEngine;

/// <summary>
/// Displays the local player's nearby interaction prompt using the
/// pirate-themed Canvas panel instead of Unity's legacy OnGUI box.
/// </summary>
public sealed class InteractionPromptHUD : MonoBehaviour
{
    public static InteractionPromptHUD Instance
    {
        get;
        private set;
    }

    [SerializeField]
    private GameObject promptPanel;

    [SerializeField]
    private TextMeshProUGUI promptText;

    private void Awake()
    {
        Instance = this;

        if (promptPanel != null)
        {
            promptPanel.SetActive(false);
        }
    }

    public void Show(string message)
    {
        if ( promptPanel == null ||  promptText == null ||string.IsNullOrWhiteSpace(message) )
        {
            Hide();
            return;
        }

        promptText.text = message;
        promptPanel.SetActive(true);
    }

    public void Hide()
    {
        if (promptPanel != null)
        {
            promptPanel.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}