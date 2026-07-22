using UnityEngine;

/// <summary>
/// Receiver for the animation events on the four directional swing clips.
///
/// Javier's Attack_Up/Down/Left/Right clips each fire EnableHitbox and
/// DisableHitbox. Unity dispatches animation events only to components on the
/// GameObject that owns the Animator — here, "Visual" — and that object had no
/// components at all, so every swing logged:
///
///     AnimationEvent 'EnableHitbox' on animation 'Attack_Down' has no
///     receiver! Are you missing a component?
///
/// That is not a cosmetic warning. It is logged as an error, and with the
/// console's Error Pause enabled it halts play mode the instant you attack,
/// which reads exactly like the game freezing.
///
/// These methods deliberately do not deal damage. Combat in this project is
/// server-authoritative: <see cref="PlayerAttack"/> resolves hits once, on the
/// server, at the moment of the swing. Opening a damage window on a locally
/// played animation would let a client's framerate decide how much damage it
/// dealt. The window is tracked here only so the clips have a home and so
/// anything later that wants swing timing has somewhere honest to read it.
/// </summary>
public sealed class PlayerAttackAnimationEvents : MonoBehaviour
{
    /// <summary>
    /// True between EnableHitbox and DisableHitbox on the current swing.
    /// Presentation-only; never consult this to decide damage.
    /// </summary>
    public bool IsHitboxOpen { get; private set; }

    // Invoked by AnimationEvent. Names must match the clips exactly.
    public void EnableHitbox()
    {
        IsHitboxOpen = true;
    }

    public void DisableHitbox()
    {
        IsHitboxOpen = false;
    }
}
