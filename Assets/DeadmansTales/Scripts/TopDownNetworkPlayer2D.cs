using System;
using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Rigidbody2D))]
public class TopDownNetworkPlayer2D : NetworkBehaviour
{
    private const string LobbySceneName = "Lobby_Island_2D";
    private const string SafeLobbySpawnName = "PlayerSpawn_0";
    private const int SupportedLobbyPlayers = 4;

    // Player 0 is the host. Every additional player is placed beside or above
    // the host; none of these offsets move a client toward the lower shoreline.
    private static readonly Vector2[] LobbyOffsetsFromHost =
    {
        Vector2.zero,
        new Vector2(1.25f, 0f),
        new Vector2(-1.25f, 0f),
        new Vector2(0f, 1.25f)
    };

    [Header("Movement")]
    [SerializeField]
    private float moveSpeed = 5f;

    [Header("Emergency Spawn")]
    [Tooltip(
        "Used only if a gameplay scene contains no PlayerSpawnPoint2D objects."
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
    private NetworkTransform networkTransform;
    private Vector2 serverMoveInput;
    private Coroutine lobbySpawnRoutine;

    public bool IsLobbyReady => lobbyReady.Value;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        networkTransform = GetComponent<NetworkTransform>();

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
        HandleSpawnForScene(SceneManager.GetActiveScene().name);
    }

    public override void OnNetworkDespawn()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;

        if (lobbySpawnRoutine != null)
        {
            StopCoroutine(lobbySpawnRoutine);
            lobbySpawnRoutine = null;
        }

        base.OnNetworkDespawn();
    }

    public void RequestLobbyReady(bool ready)
    {
        if (!IsSpawned || !IsOwner)
        {
            return;
        }

        lobbyReady.Value = ready;
    }

    private void HandleSceneLoaded(
        Scene scene,
        LoadSceneMode loadSceneMode
    )
    {
        if (IsServer)
        {
            HandleSpawnForScene(scene.name);
        }
    }

    private void HandleSpawnForScene(string loadedSceneName)
    {
        if (loadedSceneName == LobbySceneName)
        {
            ScheduleLobbySpawnCorrection();
            return;
        }

        // MainMenu intentionally has no gameplay spawn markers. PlayerObjects
        // exist there for Relay and ready-state synchronization, but must not be
        // teleported while a lobby is being created or joined.
        if (FindFirstObjectByType<PlayerSpawnPoint2D>() == null)
        {
            return;
        }

        TryMoveToSceneSpawnPoint();
    }

    private void ScheduleLobbySpawnCorrection()
    {
        if (lobbySpawnRoutine != null)
        {
            StopCoroutine(lobbySpawnRoutine);
        }

        lobbySpawnRoutine = StartCoroutine(
            CorrectLobbySpawnAfterSceneSettles()
        );
    }

    private IEnumerator CorrectLobbySpawnAfterSceneSettles()
    {
        // NGO migrates persistent PlayerObjects while Unity is finishing the
        // scene switch. Wait until both the scene and one physics tick settle.
        yield return null;
        yield return new WaitForFixedUpdate();
        yield return null;

        if (!IsServer || !IsSpawned)
        {
            lobbySpawnRoutine = null;
            yield break;
        }

        if (OwnerClientId == NetworkManager.ServerClientId)
        {
            Vector2 hostSpawn = FindSafeLobbyAnchorPosition();
            HardTeleportToSpawn(hostSpawn);

            Debug.Log(
                $"[Player Spawn] Host anchored at {hostSpawn}.",
                this
            );

            lobbySpawnRoutine = null;
            yield break;
        }

        // Give the host's correction one additional frame to commit before
        // using its final position as the only anchor for joining players.
        yield return null;

        Vector2 hostPosition = Vector2.zero;
        bool foundHost = false;

        for (int attempt = 0; attempt < 30; attempt++)
        {
            if (TryGetHostPlayerPosition(out hostPosition))
            {
                foundHost = true;
                break;
            }

            yield return null;
        }

        if (!foundHost)
        {
            hostPosition = FindSafeLobbyAnchorPosition();

            Debug.LogWarning(
                "[Player Spawn] Host PlayerObject was unavailable; using the " +
                $"safe lobby anchor at {hostPosition}.",
                this
            );
        }

        int slot = (int)(OwnerClientId % SupportedLobbyPlayers);
        Vector2 targetPosition =
            hostPosition + LobbyOffsetsFromHost[slot];

        // Repeat the hard teleport briefly so scene migration/interpolation
        // cannot restore the client's pre-load position afterward.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            HardTeleportToSpawn(targetPosition);
            yield return new WaitForSeconds(0.1f);
        }

        Debug.Log(
            $"[Player Spawn] Client {OwnerClientId} anchored beside host at " +
            $"{targetPosition} (host {hostPosition}, slot {slot}).",
            this
        );

        lobbySpawnRoutine = null;
    }

    private bool TryGetHostPlayerPosition(out Vector2 hostPosition)
    {
        hostPosition = Vector2.zero;

        NetworkManager manager = NetworkManager.Singleton;

        if (
            manager == null ||
            !manager.ConnectedClients.TryGetValue(
                NetworkManager.ServerClientId,
                out NetworkClient hostClient
            ) ||
            hostClient.PlayerObject == null
        )
        {
            return false;
        }

        hostPosition = hostClient.PlayerObject.transform.position;
        return true;
    }

    private Vector2 FindSafeLobbyAnchorPosition()
    {
        PlayerSpawnPoint2D[] spawnPoints =
            FindObjectsByType<PlayerSpawnPoint2D>(
                FindObjectsSortMode.None
            );

        foreach (PlayerSpawnPoint2D spawnPoint in spawnPoints)
        {
            if (spawnPoint.name == SafeLobbySpawnName)
            {
                return spawnPoint.transform.position;
            }
        }

        if (spawnPoints.Length > 0)
        {
            Debug.LogWarning(
                $"[Player Spawn] '{SafeLobbySpawnName}' was not found. " +
                $"Using '{spawnPoints[0].name}' as the lobby anchor.",
                this
            );

            return spawnPoints[0].transform.position;
        }

        Debug.LogError(
            "[Player Spawn] No lobby spawn point exists. Using emergency " +
            $"fallback {emergencyFallbackSpawn}.",
            this
        );

        return emergencyFallbackSpawn;
    }

    private void TryMoveToSceneSpawnPoint()
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

        HardTeleportToSpawn(chosenPosition);
    }

    private void HardTeleportToSpawn(Vector2 spawnPosition)
    {
        serverMoveInput = Vector2.zero;

        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;

        Vector3 targetPosition = new Vector3(
            spawnPosition.x,
            spawnPosition.y,
            0f
        );

        rb.position = spawnPosition;
        transform.position = targetPosition;

        if (networkTransform != null)
        {
            networkTransform.Teleport(
                targetPosition,
                transform.rotation,
                transform.localScale
            );
        }

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
