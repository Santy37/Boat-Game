using UnityEngine;

/// <summary>
/// Owns the boat leg's completion state.
///
/// The plan is: a progress bar fills during the leg, and once it is full a
/// "land on island" button appears. Clicking it ends the leg and sails the run
/// to the island the players chose on the map.
///
/// The progress bar itself is not built yet, so for now the button can simply
/// be shown (see <see cref="showButtonAlways"/>). When you add the bar, have it
/// call <see cref="SetProgress"/> each frame - the button then appears on its
/// own and nothing else has to change.
/// </summary>
public class BoatLegProgress : MonoBehaviour
{
    [Header("Progress")]
    [Tooltip("0 = leg just started, 1 = leg complete and the button appears.")]
    [SerializeField, Range(0f, 1f)] private float progress01;

    [Tooltip("Show the button right away, before the progress bar exists.")]
    [SerializeField] private bool showButtonAlways = true;

    [Header("Button")]
    [SerializeField] private string buttonLabel = "Land on Island";
    [SerializeField] private Vector2 buttonSize = new Vector2(260f, 56f);
    [Tooltip("Distance in from the RIGHT and BOTTOM edges of the screen.")]
    [SerializeField] private Vector2 cornerMargin = new Vector2(24f, 24f);

    [Header("Debug")]
    [Tooltip("Test button that damages the ship, left of the landing button.")]
    [SerializeField] private bool showDamageButton = true;
    [SerializeField] private int debugDamageAmount = 10;

    private bool sailed;

    /// <summary>Current leg progress, 0 to 1.</summary>
    public float Progress01 => progress01;

    /// <summary>True once the leg is finished and the button is available.</summary>
    public bool IsComplete => progress01 >= 1f;

    /// <summary>
    /// Called by the progress bar / leg timer once it exists.
    /// </summary>
    public void SetProgress(float value01)
    {
        progress01 = Mathf.Clamp01(value01);
    }

    /// <summary>Fill the bar immediately (debug, or an instant-win pickup).</summary>
    public void CompleteLeg()
    {
        progress01 = 1f;
    }

    /// <summary>
    /// Ends the boat leg and moves the run on to the chosen island. Safe to
    /// call from a UI Button's OnClick as well as from the built-in button.
    /// </summary>
    public void LandOnIsland()
    {
        if (sailed)
        {
            return;
        }

        if (!RunContext.HasActive)
        {
            Debug.LogWarning(
                "[Boat Leg] No active run, so there is nowhere to sail to. " +
                "Start from the menu so a run manager exists.",
                this);
            return;
        }

        sailed = true;
        RunContext.Active.OnBoatArrived();
    }

    private void OnGUI()
    {
        if (sailed)
        {
            return;
        }

        // Bottom-right corner, clear of the centred station prompts.
        Rect landRect = new Rect(
            Screen.width - buttonSize.x - cornerMargin.x,
            Screen.height - buttonSize.y - cornerMargin.y,
            buttonSize.x,
            buttonSize.y);

        // Test button, sitting immediately to the LEFT of the landing button.
        if (showDamageButton)
        {
            Rect damageRect = new Rect(
                landRect.x - buttonSize.x - 12f,
                landRect.y,
                buttonSize.x,
                buttonSize.y);

            if (GUI.Button(damageRect, $"Damage Ship  -{debugDamageAmount}"))
            {
                if (RunContext.HasActive)
                {
                    RunContext.Active.DamageShip(debugDamageAmount);
                }
            }
        }

        if (!IsComplete && !showButtonAlways)
        {
            return;
        }

        if (GUI.Button(landRect, buttonLabel))
        {
            LandOnIsland();
        }
    }
}
