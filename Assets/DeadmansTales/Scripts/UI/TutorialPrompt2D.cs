using UnityEngine;

/// <summary>
/// A one-line teaching hint that appears while a player stands in this
/// trigger, e.g. "WASD to move" near the landing beach or "Left Click to
/// attack" just before the first crab.
///
/// Deliberately zone-based rather than a wall of text at spawn: level one
/// teaches a mechanic at the moment the player needs it, and the hint goes
/// away once they have walked on. A hint can also be marked one-shot, so it
/// stops reappearing after the crew has clearly learned it.
///
/// Drawn with OnGUI to match ControlsDisplay, so it needs no canvas wiring
/// and works the same in the editor and a build. Only the local player's
/// trigger matters -- prompts are pure client-side UI and never networked.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class TutorialPrompt2D : MonoBehaviour
{
    [Header("Hint")]
    [TextArea]
    [SerializeField] private string message = "WASD to move";

    [Tooltip("Hide this hint permanently once the player has left the zone.")]
    [SerializeField] private bool showOnlyOnce;

    [Header("Placement")]
    [Tooltip("How far up the screen the hint sits (0 = bottom, 1 = top).")]
    [Range(0f, 1f)]
    [SerializeField] private float screenHeight = 0.16f;

    [SerializeField] private int fontSize = 26;

    // Only one hint is ever on screen: overlapping zones would otherwise
    // stack panels on top of each other.
    private static TutorialPrompt2D active;

    private bool consumed;

    private void OnDisable()
    {
        if (active == this)
        {
            active = null;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (consumed || !IsLocalPlayer(other))
        {
            return;
        }

        active = this;
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!IsLocalPlayer(other))
        {
            return;
        }

        if (active == this)
        {
            active = null;
        }

        if (showOnlyOnce)
        {
            consumed = true;
        }
    }

    /// <summary>
    /// True for the player this client actually controls. In a networked run
    /// every client runs this trigger, so without the ownership check a hint
    /// would pop up on your screen when a teammate walked past it.
    /// </summary>
    private static bool IsLocalPlayer(Collider2D other)
    {
        if (other == null)
        {
            return false;
        }

        TopDownNetworkPlayer2D networked =
            other.GetComponentInParent<TopDownNetworkPlayer2D>();

        if (networked != null)
        {
            return networked.IsOwner;
        }

        // Local co-op / solo scene testing has no network ownership.
        return other.GetComponentInParent<PlayerCharacter>() != null;
    }

    private void OnGUI()
    {
        if (active != this || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = fontSize,
            wordWrap = true,
        };
        style.normal.textColor = Color.white;

        float width = Mathf.Min(760f, Screen.width - 80f);
        float height = 78f;
        Rect panel = new Rect(
            (Screen.width - width) * 0.5f,
            Screen.height * (1f - screenHeight) - height * 0.5f,
            width,
            height
        );

        Color previous = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.62f);
        GUI.Box(panel, GUIContent.none);
        GUI.color = previous;

        GUI.Label(panel, message, style);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.35f);
        Collider2D area = GetComponent<Collider2D>();
        if (area != null)
        {
            Bounds bounds = area.bounds;
            Gizmos.DrawCube(bounds.center, bounds.size);
        }
    }
}
