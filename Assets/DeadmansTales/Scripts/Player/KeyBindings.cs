using UnityEngine;

/// <summary>
/// One local player's keyboard layout. Create one asset per player so three
/// people can share a single keyboard.
///
/// Create via: Assets > Create > Deadmans Tales > Key Bindings
/// </summary>
[CreateAssetMenu(fileName = "KeyBindings_P1", menuName = "Deadmans Tales/Key Bindings")]
public class KeyBindings : ScriptableObject
{
    [Header("Identity")]
    public string displayName = "Player 1";
    public Color color = Color.white;

    [Header("Movement")]
    public KeyCode up = KeyCode.W;
    public KeyCode down = KeyCode.S;
    public KeyCode left = KeyCode.A;
    public KeyCode right = KeyCode.D;

    [Header("Actions")]
    public KeyCode interact = KeyCode.E;
    public KeyCode fire = KeyCode.Space;

    public Vector2 ReadMove()
    {
        Vector2 move = Vector2.zero;

        if (Input.GetKey(left)) move.x -= 1f;
        if (Input.GetKey(right)) move.x += 1f;
        if (Input.GetKey(down)) move.y -= 1f;
        if (Input.GetKey(up)) move.y += 1f;

        return Vector2.ClampMagnitude(move, 1f);
    }

    public bool InteractDown() => Input.GetKeyDown(interact);

    public bool FireDown() => Input.GetKeyDown(fire);

    public bool LeftDown() => Input.GetKeyDown(left);

    public bool RightDown() => Input.GetKeyDown(right);

    public string MoveSummary => $"{up} {left} {down} {right}";

    public string ActionSummary => $"Interact: {interact}    Fire: {fire}";
}
