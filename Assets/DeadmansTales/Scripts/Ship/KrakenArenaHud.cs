using UnityEngine;

/// <summary>
/// Minimal prototype HUD for the kraken arena: a boss health bar across the top
/// and a banner when the beast is slain. IMGUI, to match the ship stations'
/// own OnGUI prompts; a proper uGUI bar can replace it later.
/// </summary>
public class KrakenArenaHud : MonoBehaviour
{
    private KrakenHealth kraken;
    private bool won;

    private void Start()
    {
        kraken = Object.FindFirstObjectByType<KrakenHealth>();
        if (kraken != null)
        {
            kraken.Defeated += HandleDefeated;
        }
    }

    private void OnDestroy()
    {
        if (kraken != null)
        {
            kraken.Defeated -= HandleDefeated;
        }
    }

    private void HandleDefeated()
    {
        won = true;
    }

    private void OnGUI()
    {
        if (won)
        {
            DrawCentered("THE KRAKEN IS SLAIN", 34, Color.white, -1f);
            return;
        }

        if (kraken == null)
        {
            return;
        }

        float w = Screen.width * 0.6f;
        float x = (Screen.width - w) * 0.5f;
        float y = 24f;

        GUI.Box(new Rect(x, y, w, 22f), GUIContent.none);

        Rect fill = new Rect(
            x + 2f, y + 2f, (w - 4f) * kraken.HealthFraction, 18f);
        Color prev = GUI.color;
        GUI.color = new Color(0.7f, 0.15f, 0.8f, 1f);
        GUI.DrawTexture(fill, Texture2D.whiteTexture);
        GUI.color = prev;

        DrawCentered("KRAKEN", 14, Color.white, y + 2f);
    }

    private void DrawCentered(string text, int size, Color color, float top)
    {
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = size,
            fontStyle = FontStyle.Bold,
        };
        style.normal.textColor = color;

        float y = top >= 0f ? top : Screen.height * 0.4f;
        GUI.Label(new Rect(0f, y, Screen.width, size + 10f), text, style);
    }
}
