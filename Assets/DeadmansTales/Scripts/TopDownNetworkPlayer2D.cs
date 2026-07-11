using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class TopDownNetworkPlayer2D : NetworkBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;

    private Rigidbody2D rb;
    private Vector2 serverMoveInput;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

        rb.gravityScale = 0f;
        rb.freezeRotation = true;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            Vector3[] spawnPositions =
            {
                new Vector3(-3f, -2f, 0f),
                new Vector3( 3f, -2f, 0f),
                new Vector3(-3f,  2f, 0f),
                new Vector3( 3f,  2f, 0f)
            };

            int spawnIndex = (int)(OwnerClientId % 4);
            transform.position = spawnPositions[spawnIndex];
        }
    }

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        Vector2 input = Vector2.zero;

        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
        {
            input.x -= 1f;
        }

        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
        {
            input.x += 1f;
        }

        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
        {
            input.y -= 1f;
        }

        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
        {
            input.y += 1f;
        }

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
        if (!IsServer)
        {
            return;
        }

        Vector2 nextPosition =
            rb.position + serverMoveInput * moveSpeed * Time.fixedDeltaTime;

        rb.MovePosition(nextPosition);
    }
}