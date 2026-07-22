using UnityEngine;

/// <summary>
/// Moves a 2D local player from its <see cref="PlayerCharacter"/> input.
/// The 3D scenes get their own motor (LocalPlayerMotor3D) later; nothing else
/// in the run has to change.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PlayerCharacter))]
public class LocalPlayerMotor2D : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;

    private Rigidbody2D body;
    private PlayerCharacter character;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        character = GetComponent<PlayerCharacter>();

        body.gravityScale = 0f;
        body.freezeRotation = true;
    }

    private void FixedUpdate()
    {
        Vector2 next = body.position +
            character.MoveInput * (moveSpeed * Time.fixedDeltaTime);

        body.MovePosition(next);
    }
}
