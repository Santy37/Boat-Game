using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkTransform))]
[RequireComponent(typeof(NetworkRigidbody2D))]
public class TopDownNetworkPlayer2D : NetworkBehaviour
{
    private const float SpawnMovementLockSeconds = 0.25f;

    [Header("Movement")]
    [SerializeField]
    private float moveSpeed = 5f;

    // Ready state belongs to the player owner. The server reads every player's
    // value when deciding whether the host may start the game.
    private readonly NetworkVariable<bool> lobbyReady =
        new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

    private Rigidbody2D rb;
    private NetworkTransform networkTransform;
    private Vector2 serverMoveInput;
    private float serverMovementUnlockTime;

    public bool IsLobbyReady => lobbyReady.Value;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        networkTransform = GetComponent<NetworkTransform>();

        rb.gravityScale = 0f;
        rb.freezeRotation = true;
    }

    public void RequestLobbyReady(bool ready)
    {
        if (!IsSpawned || !IsOwner)
        {
            return;
        }

        lobbyReady.Value = ready;
    }

    /// <summary>
    /// Moves this PlayerObject from the authoritative server after NGO has
    /// completed a synchronized scene load.
    /// </summary>
    public bool TeleportToSpawnServer(Vector2 spawnPosition)
    {
        if (!IsSpawned || !IsServer)
        {
            Debug.LogError(
                "[Player Spawn] Only the spawned server instance may " +
                "position a PlayerObject.",
                this
            );
            return false;
        }

        if (NetworkManager.DistributedAuthorityMode)
        {
            Debug.LogError(
                "[Player Spawn] Server-authoritative spawning requires the " +
                "NetworkManager ClientServer topology.",
                this
            );
            return false;
        }

        serverMoveInput = Vector2.zero;
        serverMovementUnlockTime =
            Time.realtimeSinceStartup + SpawnMovementLockSeconds;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;

        Vector3 targetPosition = new Vector3(
            spawnPosition.x,
            spawnPosition.y,
            0f
        );

        rb.position = spawnPosition;
        transform.position = targetPosition;

        networkTransform.Teleport(
            targetPosition,
            transform.rotation,
            transform.localScale
        );

        rb.WakeUp();
        return true;
    }

    private void Update()
    {
        if (!IsSpawned || !IsOwner)
        {
            return;
        }

        if (PauseMenu.InputBlocked)
        {
            SubmitMoveInputServerRpc(Vector2.zero);
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

        SubmitMoveInputServerRpc(Vector2.ClampMagnitude(input, 1f));
    }

    [ServerRpc]
    private void SubmitMoveInputServerRpc(Vector2 input)
    {
        if (Time.realtimeSinceStartup < serverMovementUnlockTime)
        {
            serverMoveInput = Vector2.zero;
            return;
        }

        serverMoveInput = Vector2.ClampMagnitude(input, 1f);
    }

    private void FixedUpdate()
    {
        if (!IsSpawned || !IsServer)
        {
            return;
        }

        if (Time.realtimeSinceStartup < serverMovementUnlockTime)
        {
            serverMoveInput = Vector2.zero;
            rb.linearVelocity = Vector2.zero;
            return;
        }

        Vector2 nextPosition =
            rb.position +
            serverMoveInput * moveSpeed * Time.fixedDeltaTime;

        rb.MovePosition(nextPosition);
    }
}
