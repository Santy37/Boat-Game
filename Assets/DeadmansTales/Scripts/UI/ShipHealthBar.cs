using UnityEngine;

/// <summary>
/// Shows the ship's hull bar, and optionally each crew member's health.
///
/// By default the bar stays hidden until the ship takes its first hit, then
/// remains visible for the rest of the run. It flashes on each hit.
///
/// All values are read from the run manager, which is the single source of
/// truth for both the ship and the crew.
/// </summary>
public class ShipHealthBar : MonoBehaviour
{
    [Header("Placement")]
    [Tooltip("X shifts from screen centre. Y is how far down from the top.")]
    [SerializeField] private Vector2 offsetFromTopCenter = new Vector2(0f, 20f);
    [SerializeField] private Vector2 barSize = new Vector2(420f, 26f);

    [Header("Behaviour")]
    [Tooltip("Show the bar even at full health.")]
    [SerializeField] private bool alwaysShow = false;
    [SerializeField] private bool showCrewHealth = true;

    [Header("Colours")]
    [SerializeField] private Color healthyColor = new Color(0.25f, 0.75f, 0.35f);
    [SerializeField] private Color hurtColor = new Color(0.85f, 0.65f, 0.15f);
    [SerializeField] private Color criticalColor = new Color(0.85f, 0.25f, 0.20f);
    [SerializeField] private Color flashColor = Color.white;
    [SerializeField] private Color backdropColor = new Color(0f, 0f, 0f, 0.55f);

    private Texture2D pixel;
    private int lastShipHealth = -1;
    private float flashUntil;

    private void Awake()
    {
        pixel = new Texture2D(1, 1);
        pixel.SetPixel(0, 0, Color.white);
        pixel.Apply();
    }

    private void OnDestroy()
    {
        if (pixel != null)
        {
            Destroy(pixel);
        }
    }

    private void Update()
    {
        if (!RunContext.HasActive)
        {
            return;
        }

        int health = RunContext.Active.ShipHealth;

        if (lastShipHealth >= 0 && health < lastShipHealth)
        {
            // Unscaled so the flash still plays if the game pauses.
            flashUntil = Time.unscaledTime + 0.35f;
        }

        lastShipHealth = health;
    }

    private void OnGUI()
    {
        if (!RunContext.HasActive)
        {
            return;
        }

        int health = RunContext.Active.ShipHealth;
        int max = Mathf.Max(1, RunContext.Active.MaxShipHealth);

        if (!alwaysShow && health >= max)
        {
            return;
        }

        float x = (Screen.width - barSize.x) * 0.5f + offsetFromTopCenter.x;
        float y = offsetFromTopCenter.y;

        DrawBar(
            new Rect(x, y, barSize.x, barSize.y),
            health,
            max,
            $"Ship   {health} / {max}",
            Time.unscaledTime < flashUntil);

        if (!showCrewHealth)
        {
            return;
        }

        float rowY = y + barSize.y + 6f;
        float rowHeight = 20f;

        for (int i = 0; i < RunContext.Active.MaxPlayers; i++)
        {
            if (!RunContext.Active.IsJoined(i))
            {
                continue;
            }

            PlayerRunData data = RunContext.Active.GetPlayerData(i);
            if (data == null)
            {
                continue;
            }

            KeyBindings binding = RunContext.Active.GetBindings(i);
            string who = binding != null ? binding.displayName : $"Player {i + 1}";

            DrawBar(
                new Rect(x, rowY, barSize.x, rowHeight),
                data.health,
                Mathf.Max(1, data.maxHealth),
                $"{who}   {data.health} / {data.maxHealth}",
                false);

            rowY += rowHeight + 4f;
        }
    }

    private void DrawBar(Rect rect, int value, int max, string label, bool flash)
    {
        float fraction = Mathf.Clamp01(value / (float)max);

        Color saved = GUI.color;

        // Backdrop
        GUI.color = backdropColor;
        GUI.DrawTexture(rect, pixel);

        // Fill
        Color fill = fraction > 0.5f
            ? healthyColor
            : fraction > 0.25f ? hurtColor : criticalColor;

        if (flash)
        {
            fill = flashColor;
        }

        GUI.color = fill;
        GUI.DrawTexture(
            new Rect(rect.x, rect.y, rect.width * fraction, rect.height), pixel);

        GUI.color = saved;

        GUI.Label(new Rect(rect.x + 8f, rect.y + 2f, rect.width - 16f, rect.height), label);
    }
}
