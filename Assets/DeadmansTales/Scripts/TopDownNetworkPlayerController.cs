using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class TopDownNetworkPlayerController : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float turnSpeed = 12f;

    private CharacterController characterController;

    // This input value lives on the server.
    // The owner sends input to the server, then the server moves the object.
    private Vector2 serverMoveInput;

    // Small gravity value so the CharacterController stays grounded.
    private float verticalVelocity;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            Vector3[] spawnPositions =
            {
                new Vector3(-2f, 1f, -2f),
                new Vector3( 2f, 1f, -2f),
                new Vector3(-2f, 1f,  2f),
                new Vector3( 2f, 1f,  2f)
            };

            int spawnIndex = (int)(OwnerClientId % 4);
            transform.position = spawnPositions[spawnIndex];
        }
    }

    private void Update()
    {
        // Only the local owner should read keyboard input.
        if (!IsOwner)
        {
            return;
        }

        float horizontal = 0f;
        float vertical = 0f;

        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
        {
            horizontal -= 1f;
        }

        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
        {
            horizontal += 1f;
        }

        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
        {
            vertical -= 1f;
        }

        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
        {
            vertical += 1f;
        }

        Vector2 input = new Vector2(horizontal, vertical);
        input = Vector2.ClampMagnitude(input, 1f);

        SubmitMoveInputServerRpc(input);
    }

    [ServerRpc]
    private void SubmitMoveInputServerRpc(Vector2 input)
    {
        serverMoveInput = Vector2.ClampMagnitude(input, 1f);
    }

    private void FixedUpdate()
    {
        // The server is the only machine that actually moves the player object.
        if (!IsServer)
        {
            return;
        }

        Vector3 moveDirection = new Vector3(serverMoveInput.x, 0f, serverMoveInput.y);

        if (characterController.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -1f;
        }

        verticalVelocity += Physics.gravity.y * Time.fixedDeltaTime;

        Vector3 movement = moveDirection * moveSpeed;
        movement.y = verticalVelocity;

        characterController.Move(movement * Time.fixedDeltaTime);

        if (moveDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                turnSpeed * Time.fixedDeltaTime
            );
        }
    }
}