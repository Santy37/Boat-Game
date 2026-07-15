using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Rigidbody2D))]
public class TopDownNetworkPlayer2D : NetworkBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;

    [Header("Emergency Spawn")]
    [Tooltip(
        "Used only if the scene contains no PlayerSpawnPoint2D objects."
    )]
    [SerializeField]
    private Vector2 emergencyFallbackSpawn =
        new Vector2(2f, 12f);

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
        base.OnNetworkSpawn();

        if (!IsServer)
        {
            return;
        }

        SceneManager.sceneLoaded += HandleSceneLoaded;
        TryMoveToSpawnPoint();
    }

    public override void OnNetworkDespawn()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        base.OnNetworkDespawn();
    }

    private void HandleSceneLoaded(
        Scene scene,
        LoadSceneMode loadSceneMode
    )
    {
        if (IsServer)
        {
            TryMoveToSpawnPoint();
        }
    }

    private void TryMoveToSpawnPoint()
    {
        PlayerSpawnPoint2D spawnPoint =
            FindFirstObjectByType<PlayerSpawnPoint2D>();

        if (spawnPoint == null)
        {
            return;
        }

        MoveToSpawnPoint();
    }


    private void MoveToSpawnPoint()
    {
        PlayerSpawnPoint2D[] spawnPoints =
            FindObjectsByType<PlayerSpawnPoint2D>(
                FindObjectsSortMode.None
            );

        // FindObjectsByType with None does not guarantee a useful order.
        // Sorting by object name keeps PlayerSpawn_0, _1, _2, _3 ordered.
        Array.Sort(
            spawnPoints,
            (a, b) => string.CompareOrdinal(a.name, b.name)
        );

        Vector2 chosenPosition;

        if (spawnPoints.Length == 0)
        {
            chosenPosition = emergencyFallbackSpawn;

            Debug.LogError(
                "[Player Spawn] No PlayerSpawnPoint2D objects were found. " +
                $"Using emergency fallback position {chosenPosition}.",
                this
            );
        }
        else
        {
            int spawnIndex =
                (int)(OwnerClientId % (ulong)spawnPoints.Length);

            PlayerSpawnPoint2D chosenSpawnPoint =
                spawnPoints[spawnIndex];

            chosenPosition =
                chosenSpawnPoint.transform.position;

            Debug.Log(
                $"[Player Spawn] Client {OwnerClientId} assigned to " +
                $"{chosenSpawnPoint.name} at {chosenPosition}.",
                this
            );
        }

        TeleportToSpawn(chosenPosition);
    }

    private void TeleportToSpawn(Vector2 spawnPosition)
    {
        // Prevent any previous or accidental input from moving the player
        // immediately after it spawns.
        serverMoveInput = Vector2.zero;

        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;

        // Set the Rigidbody2D and Transform immediately at spawn.
        rb.position = spawnPosition;

        transform.position = new Vector3(
            spawnPosition.x,
            spawnPosition.y,
            0f
        );

        rb.WakeUp();
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

        if (
            Input.GetKey(KeyCode.A) ||
            Input.GetKey(KeyCode.LeftArrow)
        )
        {
            input.x -= 1f;
        }

        if (
            Input.GetKey(KeyCode.D) ||
            Input.GetKey(KeyCode.RightArrow)
        )
        {
            input.x += 1f;
        }

        if (
            Input.GetKey(KeyCode.S) ||
            Input.GetKey(KeyCode.DownArrow)
        )
        {
            input.y -= 1f;
        }

        if (
            Input.GetKey(KeyCode.W) ||
            Input.GetKey(KeyCode.UpArrow)
        )
        {
            input.y += 1f;
        }

        input = Vector2.ClampMagnitude(input, 1f);

        SubmitMoveInputServerRpc(input);
    }

    [ServerRpc]
    private void SubmitMoveInputServerRpc(Vector2 input)
    {
        serverMoveInput =
            Vector2.ClampMagnitude(input, 1f);
    }

    private void FixedUpdate()
    {
        if (!IsSpawned || !IsServer)
        {
            return;
        }

        Vector2 nextPosition =
            rb.position +
            serverMoveInput *
            moveSpeed *
            Time.fixedDeltaTime;

        rb.MovePosition(nextPosition);
    }
}