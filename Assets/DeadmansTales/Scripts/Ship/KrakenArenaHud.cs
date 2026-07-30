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

    // Whether a kraken was ever present. Together with the reference having
    // gone null this is a second, independent way to know the fight is won --
    // see OnGUI for why the Defeated event alone is not enough.
    private bool sawKraken;

    private void Start()
    {
        kraken = Object.FindFirstObjectByType<KrakenHealth>();
        if (kraken != null)
        {
            sawKraken = true;
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
        // Belt-and-braces victory detection. The server zeroes the kraken's
        // health and despawns it in the same breath, and those are two
        // separate messages: if the despawn reaches a client first, that
        // client's CurrentHealth.OnValueChanged never runs, Defeated never
        // fires, and the banner would appear on the host only. A kraken that
        // was here and is now gone means the same thing, so treat it that way.
        if (!won && sawKraken && kraken == null)
        {
            won = true;
        }

        if (won)
        {
            DrawVictoryGuidance();
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

    /// <summary>
    /// What to do now the beast is dead.
    ///
    /// This is guidance, not a flourish, so it stays on screen until the run
    /// is actually finished rather than fading after a moment. The portal sits
    /// off to one side of the arena and the ship has to be STEERED to it
    /// before anyone can walk up and use it -- two steps, neither of which the
    /// arena signals on its own, which is why players just drifted after the
    /// kill wondering whether the game had ended.
    ///
    /// Deliberately says "sail", not "go": the wheel is the part people miss.
    /// Once the ship is close enough the portal's own proximity prompt takes
    /// over and tells them which key to press, so this only has to get them
    /// moving in the right direction.
    /// </summary>
    private void DrawVictoryGuidance()
    {
        float w = Mathf.Min(Screen.width * 0.7f, 720f);
        float h = 96f;
        float x = (Screen.width - w) * 0.5f;
        float y = Screen.height * 0.18f;

        // A backing box so the text stays legible over the water and wreckage.
        GUI.Box(new Rect(x, y, w, h), GUIContent.none);

        DrawCentered(
            "THE KRAKEN IS SLAIN",
            26,
            new Color(1f, 0.85f, 0.3f, 1f),
            y + 14f
        );

        DrawCentered(
            "SAIL TO THE PORTAL TO FINISH THE VOYAGE",
            16,
            Color.white,
            y + 54f
        );
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
