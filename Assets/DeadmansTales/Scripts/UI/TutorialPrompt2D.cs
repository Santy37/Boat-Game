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
/// The trigger sends its message to TutorialPromptHUD, which displays the
/// text using the shared pirate-themed Canvas panel. Only the local player's
/// trigger matters -- prompts are pure client-side UI and never networked.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class TutorialPrompt2D : MonoBehaviour
{
    [Header("Hint")]
    [TextArea]
    [SerializeField]
    private string message = "WASD TO MOVE";

    [Tooltip(
        "Hide this hint permanently once the player has left the zone."
    )]
    [SerializeField]
    private bool showOnlyOnce;

    // Only one hint is ever on screen: overlapping zones would otherwise
    // compete to display different messages on the shared prompt panel.
    private static TutorialPrompt2D active;

    private bool consumed;

    private void OnDisable()
    {
        if (active == this)
        {
            active = null;

            TutorialPromptHUD.Instance?.Hide();
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (consumed || !IsLocalPlayer(other))
        {
            return;
        }

        active = this;
        ShowPrompt();
    }

    /// <summary>
    /// Also claim the hint while the player simply STANDS in the zone.
    ///
    /// OnTriggerEnter2D never fires for a collider you are already inside, and
    /// the crew spawns directly on top of the "WASD to move" zone -- so the
    /// very first hint, the one that matters most, could otherwise silently
    /// fail to appear.
    /// </summary>
    private void OnTriggerStay2D(Collider2D other)
    {
        if (consumed || !IsLocalPlayer(other))
        {
            return;
        }

        active = this;
        ShowPrompt();
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

            TutorialPromptHUD.Instance?.Hide();
        }

        if (showOnlyOnce)
        {
            consumed = true;
        }
    }

    /// <summary>
    /// Sends this zone's message to the shared tutorial prompt Canvas.
    /// </summary>
    private void ShowPrompt()
    {
        if (active != this)
        {
            return;
        }

        TutorialPromptHUD.Instance?.Show(message);
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

    /// <summary>
    /// Draws the tutorial trigger area in the Scene view so designers can
    /// see where each teaching hint begins and ends.
    /// </summary>
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(
            1f,
            0.85f,
            0.2f,
            0.35f
        );

        Collider2D area = GetComponent<Collider2D>();

        if (area != null)
        {
            Bounds bounds = area.bounds;
            Gizmos.DrawCube(bounds.center, bounds.size);
        }
    }
}