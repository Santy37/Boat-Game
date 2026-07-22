using UnityEngine;

/// <summary>
/// Lobby panel for adding players to a local run.
///
/// Each seat shows its controls and a Join button. A player can either click
/// their Join button or simply press their own Interact key. Joined players
/// appear in the world immediately.
/// </summary>
public class LocalPlayerJoinPanel : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private string title = "Players";
    [Tooltip("X shifts from screen centre (negative = left). " +
             "Y is how far down from the top.")]
    [SerializeField] private Vector2 offsetFromCenter = new Vector2(-240f, 20f);
    [SerializeField] private float panelWidth = 420f;
    [SerializeField] private float rowHeight = 58f;

    [Header("Behaviour")]
    [Tooltip("Let a player join by pressing their own Interact key.")]
    [SerializeField] private bool joinWithInteractKey = true;
    [Tooltip("Show a button that rerolls the map before setting sail.")]
    [SerializeField] private bool showRerollButton = true;

    private LocalRunManager runManager;

    private void Awake()
    {
        runManager = FindFirstObjectByType<LocalRunManager>();
    }

    private void Update()
    {
        if (!joinWithInteractKey || runManager == null)
        {
            return;
        }

        for (int i = 0; i < runManager.MaxPlayers; i++)
        {
            if (runManager.IsJoined(i))
            {
                continue;
            }

            KeyBindings binding = runManager.GetBindings(i);

            if (binding != null && binding.InteractDown())
            {
                runManager.JoinPlayer(i);
            }
        }
    }

    private void OnGUI()
    {
        if (runManager == null)
        {
            return;
        }

        int seats = runManager.MaxPlayers;
        float height = 56f + rowHeight * seats;

        // Anchored along the top, offset from the horizontal centre.
        Rect panel = new Rect(
            (Screen.width - panelWidth) * 0.5f + offsetFromCenter.x,
            offsetFromCenter.y,
            panelWidth,
            height);

        GUI.Box(panel, title);

        for (int i = 0; i < seats; i++)
        {
            KeyBindings binding = runManager.GetBindings(i);
            bool isJoined = runManager.IsJoined(i);

            float y = panel.y + 26f + i * rowHeight;

            string who = binding != null
                ? binding.displayName
                : $"Player {i + 1}";

            string keys = binding != null
                ? $"Move {binding.MoveSummary}   Interact {binding.interact}"
                : "no bindings assigned";

            GUI.Label(
                new Rect(panel.x + 12f, y, panelWidth - 130f, rowHeight),
                $"{who}\n{keys}");

            Rect button = new Rect(
                panel.x + panelWidth - 112f, y + 8f, 100f, 30f);

            if (isJoined)
            {
                if (GUI.Button(button, "Leave"))
                {
                    runManager.LeavePlayer(i);
                }
            }
            else if (GUI.Button(button, "Join"))
            {
                runManager.JoinPlayer(i);
            }
        }

        GUI.Label(
            new Rect(panel.x + 12f, panel.yMax - 26f, panelWidth - 130f, 22f),
            $"{runManager.JoinedCount} joined - head to the rowboat to set sail.");

        if (showRerollButton &&
            GUI.Button(
                new Rect(panel.x + panelWidth - 118f, panel.yMax - 30f, 106f, 26f),
                "Reroll Map"))
        {
            runManager.RandomizeSeed();
        }
    }
}
