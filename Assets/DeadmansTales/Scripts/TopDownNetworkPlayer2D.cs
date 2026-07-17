using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Rigidbody2D))]
public class TopDownNetworkPlayer2D : NetworkBehaviour
{
    private const string LobbySceneName = "Lobby_Island_2D";
    private const string SafeLobbySpawnName = "PlayerSpawn_0";
    private const int SupportedLobbyPlayers = 4;

    private static readonly Vector2[] LobbySpawnOffsets =
    {
        new Vector2(-0.6f, 0.45f),
        new Vector2(0.6f, 0.45f),
        new Vector2(-0.6f, -0.45f),
        new Vector2(0.6f, -0.45f)
    };

    [Header("Movement")]
    [SerializeField]
    private float moveSpeed = 5f;

    [Header("Emergency Spawn")]
    [Tooltip(
        "Used only if the scene contains no PlayerSpawnPoint2D objects."
    )]
    [SerializeField]
    private Vector2 emergencyFallbackSpawn =
        new Vector2(2f, 12f);

    // Ready is player-owned: each client may only write the value on its own
    // PlayerObject. Everyone can read it so the host can enforce readiness.
    private NetworkVariable<bool> lobbyReady =
        new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

    private Rigidbody2D rb;
    private Vector2 serverMoveInput;

    public bool IsLobbyReady => lobbyReady.Value;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

        rb.gravityScale = 0f;
        rb.freezeRotation = true;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Never initialize another player's owner-written ready variable here.
        // New player objects already begin false; the host is skipped by the
        // host-side readiness check and therefore may start alone.
        if (!IsServer)
        {
            return;
        }

        SceneManager.sceneLoaded += HandleSceneLoaded;
        TryMoveToSpawnPoint(SceneManager.GetActiveScene().name);
    }

    public override void OnNetworkDespawn()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        base.OnNetworkDespawn();
    }

    public void RequestLobbyReady(bool ready)
    {
        if (!IsSpawned || !IsOwner)
        {
            return;
        }

        // Owner-write permission means the local client updates only its own
        // PlayerObject. NGO then synchronizes the value to the host and peers.
        lobbyReady.Value = ready;
    }

    private void HandleSceneLoaded(
        Scene scene,
        LoadSceneMode loadSceneMode
    )
    {
        if (IsServer)
        {
            // Use the exact scene passed by Unity. During NGO scene sync,
            // GetActiveScene() can still report MainMenu for this callback frame.
            TryMoveToSpawnPoint(scene.name);
        }
    }

    private void TryMoveToSpawnPoint(string loadedSceneName)
    {
        PlayerSpawnPoint2D spawnPoint =
            FindFirstObjectByType<PlayerSpawnPoint2D>();

        if (spawnPoint == null)
        {
            return;
        }

        MoveToSpawnPoint(loadedSceneName);
    }

    private void MoveToSpawnPoint(string loadedSceneName)
    {
        PlayerSpawnPoint2D[] spawnPoints =
            FindObjectsByType<PlayerSpawnPoint2D>(
                FindObjectsSortMode.None
            );

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
        else if (loadedSceneName == LobbySceneName)
        {
            PlayerSpawnPoint2D safeAnchor =
                FindSafeLobbyAnchor(spawnPoints);

            int slot =
                (int)(OwnerClientId % SupportedLobbyPlayers);

            chosenPosition =
                (Vector2)safeAnchor.transform.position +
                LobbySpawnOffsets[slot];

            chosenDescription =
                $"{safeAnchor.name} compact island slot {slot}";
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

    private static PlayerSpawnPoint2D FindSafeLobbyAnchor(
        PlayerSpawnPoint2D[] spawnPoints
    )
    {
        foreach (PlayerSpawnPoint2D spawnPoint in spawnPoints)
        {
            if (spawnPoint.name == SafeLobbySpawnName)
            {
                return spawnPoint;
            }
        }

        Debug.LogWarning(
            $"[Player Spawn] '{SafeLobbySpawnName}' was not found. " +
            $"Using '{spawnPoints[0].name}' as the lobby anchor."
        );

        return spawnPoints[0];
    }

    private void TeleportToSpawn(Vector2 spawnPosition)
    {
        serverMoveInput = Vector2.zero;

        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;

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
