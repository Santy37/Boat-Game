using UnityEngine;

/// <summary>
/// Spins a whirlpool sprite so the arena hazard reads as moving water rather
/// than a static decal. Prototype-simple: pure rotation, no physics pull yet
/// (a gentle tug on a manned ship is a natural next step once the fight is
/// tuned in a playtest).
/// </summary>
public class WhirlpoolSpin : MonoBehaviour
{
    [Tooltip("Degrees per second. Negative spins clockwise. Kept slow so a big " +
             "maelstrom churns rather than looking like a spinning decal.")]
    [SerializeField] private float spinDegreesPerSecond = -12f;

    private void Update()
    {
        transform.Rotate(0f, 0f, spinDegreesPerSecond * Time.deltaTime);
    }
}
