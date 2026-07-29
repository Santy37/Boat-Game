using TMPro;
using UnityEngine;

public sealed class TutorialPromptHUD : MonoBehaviour
{
    public static TutorialPromptHUD Instance { get; private set; }

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
        if (promptPanel == null || promptText == null ||string.IsNullOrWhiteSpace(message))
        {
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