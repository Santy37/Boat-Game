using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Rigidbody2D))]
public class TopDownNetworkPlayer2D : NetworkBehaviour
{
    private const string LobbySceneName = "Lobby_Island_2D";
    private const int SupportedLobbyPlayers = 4;

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
        string chosenDescription;

        if (spawnPoints.Length == 0)
        {
            chosenPosition = emergencyFallbackSpawn;
            chosenDescription = "emergency fallback";

            Debug.LogError(
                "[Player Spawn] No PlayerSpawnPoint2D objects were found. " +
                $"Using emergency fallback position {chosenPosition}.",
                this
            );
        }
        else if (SceneManager.GetActiveScene().name == LobbySceneName)
        {
            chosenPosition = GetCompactLobbySpawn(
                spawnPoints,
                OwnerClientId
            );
            chosenDescription = "compact lobby formation";
        }
        else
        {
            int spawnIndex =
                (int)(OwnerClientId % (ulong)spawnPoints.Length);

            PlayerSpawnPoint2D chosenSpawnPoint =
                spawnPoints[spawnIndex];

            chosenPosition = chosenSpawnPoint.transform.position;
            chosenDescription = chosenSpawnPoint.name;
        }

        Debug.Log(
            $"[Player Spawn] Client {OwnerClientId} assigned to " +
            $"{chosenDescription} at {chosenPosition}.",
            this
        );

        TeleportToSpawn(chosenPosition);
    }

    private static Vector2 GetCompactLobbySpawn(
        PlayerSpawnPoint2D[] spawnPoints,
        ulong ownerClientId
    )
    {
        int slot = (int)(ownerClientId % SupportedLobbyPlayers);
        Vector2 firstPosition = spawnPoints[0].transform.position;

        if (spawnPoints.Length == 1)
        {
            // Keep all players close to the one known-safe marker instead of
            // falling back to arbitrary world positions.
            float centeredOffset = (slot - 1.5f) * 0.35f;
            return firstPosition + Vector2.right * centeredOffset;
        }

        Vector2 secondPosition = spawnPoints[1].transform.position;

        // The first two lobby markers are the known-good island positions.
        // Spread all four clients along the safe segment between them rather
        // than trusting the old outer markers that sit beyond the island.
        float interpolation = slot / (SupportedLobbyPlayers - 1f);
        return Vector2.Lerp(firstPosition, secondPosition, interpolation);
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
