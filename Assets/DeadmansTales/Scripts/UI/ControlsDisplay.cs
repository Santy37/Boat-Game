using UnityEngine;

/// <summary>
/// Simple panel showing every seat's controls, and who has joined. Put this in
/// the starting area so players can see which keys are theirs.
/// </summary>
public class ControlsDisplay : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private string title = "Controls";
    [SerializeField] private bool show = true;
    [Tooltip("X shifts from screen centre (negative = left). " +
             "Y is how far down from the top.")]
    [SerializeField] private Vector2 offsetFromCenter = new Vector2(240f, 20f);
    [SerializeField] private float panelWidth = 380f;

    [Header("Fallback")]
    [Tooltip("Used when no run is active (e.g. testing this scene alone).")]
    [SerializeField] private KeyBindings[] fallbackBindings;

    private void OnGUI()
    {
        if (!show)
        {
            return;
        }

        const float rowHeight = 44f;

        if (!RunContext.HasActive)
        {
            DrawFallback(rowHeight);
            return;
        }

        int seats = RunContext.Active.MaxPlayers;
        float height = 30f + rowHeight * seats;

        // Anchored along the top, offset from the horizontal centre.
        Rect panel = new Rect(
            (Screen.width - panelWidth) * 0.5f + offsetFromCenter.x,
            offsetFromCenter.y,
            panelWidth,
            height);

        GUI.Box(panel, title);

        for (int i = 0; i < seats; i++)
        {
            KeyBindings binding = RunContext.Active.GetBindings(i);
            if (binding == null)
            {
                continue;
            }

            string state = RunContext.Active.IsJoined(i) ? "" : "  (not joined)";

            GUI.Label(
                new Rect(
                    panel.x + 12f,
                    panel.y + 26f + i * rowHeight,
                    panelWidth - 24f,
                    rowHeight),
                $"{binding.displayName}{state}   Move: {binding.MoveSummary}\n" +
                $"    {binding.ActionSummary}");
        }
    }

    private void DrawFallback(float rowHeight)
    {
        if (fallbackBindings == null || fallbackBindings.Length == 0)
        {
            return;
        }

        float height = 30f + rowHeight * fallbackBindings.Length;

        Rect panel = new Rect(
            (Screen.width - panelWidth) * 0.5f + offsetFromCenter.x,
            offsetFromCenter.y,
            panelWidth,
            height);

        GUI.Box(panel, title);

        for (int i = 0; i < fallbackBindings.Length; i++)
        {
            KeyBindings binding = fallbackBindings[i];
            if (binding == null)
            {
                continue;
            }

            GUI.Label(
                new Rect(
                    panel.x + 12f,
                    panel.y + 26f + i * rowHeight,
                    panelWidth - 24f,
                    rowHeight),
                $"{binding.displayName}   Move: {binding.MoveSummary}\n" +
                $"    {binding.ActionSummary}");
        }
    }
}
