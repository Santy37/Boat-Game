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

    // Several systems share this one panel. Higher priority wins so they don't
    // flicker fighting over it: a resting status message (a low-priority
    // arrival line) yields to a proximity prompt ("Press E to Continue"), which
    // yields to a manned station's controls -- but a freshly-raised banner
    // ("Attack the pirates!") outranks everything for a few seconds so the
    // player can't miss it.
    public const int StatusPriority = 0;
    public const int PromptPriority = 10;
    public const int StationPriority = 20;
    public const int BannerPriority = 30;

    // Who currently owns the panel and at what priority.
    private Object owner;
    private int ownerPriority;

    // The panel rests near the bottom of the screen (its authored position).
    // Centered callers lift it so its text lands on the screen's middle.
    private RectTransform panelRect;
    private Vector2 panelRestPosition;
    private Vector2 panelCenterPosition;

    private void Awake()
    {
        Instance = this;

        if (promptPanel != null)
        {
            promptPanel.SetActive(false);

            panelRect = promptPanel.GetComponent<RectTransform>();
            if (panelRect != null)
            {
                panelRestPosition = panelRect.anchoredPosition;

                // The text rests low by design; lifting the panel by that same
                // offset brings the text to the vertical centre. No magic
                // numbers -- it's read from the authored layout.
                float lift = promptText != null
                    ? -promptText.rectTransform.anchoredPosition.y
                    : 0f;
                panelCenterPosition =
                    panelRestPosition + new Vector2(0f, lift);
            }
        }
    }

    public void Show(
        string message, Object claimant, int priority, bool centered = false)
    {
        if (promptPanel == null || promptText == null ||
            string.IsNullOrWhiteSpace(message))
        {
            Hide(claimant);
            return;
        }

        // A lower-priority source can't steal the panel from a different owner
        // that currently holds it. The owner may always refresh its own text.
        if (owner != null && owner != claimant && priority < ownerPriority)
        {
            return;
        }

        owner = claimant;
        ownerPriority = priority;
        promptText.text = message;

        // Position follows whoever currently owns the panel this frame.
        if (panelRect != null)
        {
            panelRect.anchoredPosition =
                centered ? panelCenterPosition : panelRestPosition;
        }

        promptPanel.SetActive(true);
    }

    public void Hide(Object claimant)
    {
        // Only the current owner (or an already-clear panel) may hide it, so a
        // lower-priority source can't blank a panel someone else is using.
        if (owner != null && owner != claimant)
        {
            return;
        }

        owner = null;

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