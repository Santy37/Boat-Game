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
    private const float MoveInputHeartbeatSeconds = 0.05f;
    private const float ServerInputStaleSeconds = 0.5f;

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
    private NetworkPlayerLoadout loadout;
    private Vector2 serverMoveInput;
    private float serverMovementUnlockTime;
    private float serverInputExpiryTime;
    // The server's own copy of "this player is sitting at a station".
    // ControlLocked is the client's copy; the server cannot read it, and
    // without this it will happily keep walking a seated player. See
    // SeatServerRpc for the full story.
    private bool serverStationLocked;
    private Vector2 lastSubmittedMoveInput;
    private float nextMoveInputHeartbeatTime;

    public bool IsLobbyReady => lobbyReady.Value;

    /// <summary>True while the player is locked to a station (cannon/helm).</summary>
    public bool ControlLocked { get; private set; }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        networkTransform = GetComponent<NetworkTransform>();
        loadout = GetComponent<NetworkPlayerLoadout>();

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

        // An authoritative reposition outranks any station: a player carried
        // to a spawn point by a scene change is not sitting at a cannon in
        // the scene they just left, and leaving the lock set would strand
        // them frozen on arrival.
        serverStationLocked = false;

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

        // Frozen by the pause menu, or seated at a cannon or the helm:
        // either way, stop the character and hold still. These two arrived
        // from different branches and are not alternatives — a player can
        // open the menu while sitting at the helm.
        //
        // Sent through the throttle rather than straight down the RPC: a
        // seated player holds one input for as long as they man the station,
        // and the direct call would push an unreliable RPC every frame of it.
        if (PauseMenu.InputBlocked || ControlLocked)
        {
            SubmitMoveInputIfNeeded(Vector2.zero);
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

        SubmitMoveInputIfNeeded(Vector2.ClampMagnitude(input, 1f));
    }

    private void SubmitMoveInputIfNeeded(Vector2 input)
    {
        bool inputChanged = input != lastSubmittedMoveInput;
        if (!inputChanged && Time.unscaledTime < nextMoveInputHeartbeatTime)
        {
            return;
        }

        lastSubmittedMoveInput = input;
        nextMoveInputHeartbeatTime =
            Time.unscaledTime + MoveInputHeartbeatSeconds;
        SubmitMoveInputServerRpc(input);
    }

    [ServerRpc(Delivery = RpcDelivery.Unreliable)]
    private void SubmitMoveInputServerRpc(Vector2 input)
    {
        serverInputExpiryTime =
            Time.realtimeSinceStartup + ServerInputStaleSeconds;

        // A seated player does not walk, whatever this packet says. It may
        // well be older than the seat that locked them: this RPC is
        // unreliable and SeatServerRpc is reliable, so they travel on
        // different channels and arrive in whatever order they please.
        if (serverStationLocked)
        {
            serverMoveInput = Vector2.zero;
            return;
        }

        if (Time.realtimeSinceStartup < serverMovementUnlockTime)
        {
            serverMoveInput = Vector2.zero;
            return;
        }

        serverMoveInput = Vector2.ClampMagnitude(input, 1f);
    }

    private void OnDisable()
    {
        // Death (or any other control lockout) disables this script. Clear
        // pending input so the server never keeps applying a stale vector.
        serverMoveInput = Vector2.zero;
        lastSubmittedMoveInput = Vector2.zero;
        serverStationLocked = false;
    }

    /// <summary>
    /// Snap this player to a station (cannon/helm), face a direction, and lock
    /// movement. Called on the owning client by a station interactable.
    /// </summary>
    public void EnterStation(Vector2 seatPosition, Vector2 facing)
    {
        ControlLocked = true;
        SeatServerRpc(seatPosition);

        PlayerAnimation2D animation =
            GetComponentInChildren<PlayerAnimation2D>();

        if (animation != null)
        {
            animation.LockFacing(facing);
        }
    }

    /// <summary>Release the player from a station and restore movement.</summary>
    public void ExitStation()
    {
        ControlLocked = false;
        ReleaseStationServerRpc();

        PlayerAnimation2D animation =
            GetComponentInChildren<PlayerAnimation2D>();

        if (animation != null)
        {
            animation.UnlockFacing();
        }
    }

    /// <summary>
    /// Plant the player on a station seat, and stop the server walking them
    /// off it again.
    ///
    /// The lock is the point. Seating used to zero serverMoveInput once and
    /// trust that it stayed zero — but the owner streams movement down an
    /// UNRELIABLE channel while this RPC is reliable, so the last packet from
    /// the walk up to the cannon can land AFTER the seat that ended it. The
    /// server then resumed walking, and kept walking until the stale-input
    /// timer expired: moveSpeed * ServerInputStaleSeconds, up to 2.5 units of
    /// drift in whatever direction you happened to approach from. It read as
    /// the cannon dumping you a couple of paces to one side (or, from further
    /// out, clean off the ship).
    ///
    /// The helm never showed this because it re-pins its steersman to the
    /// stand point every LateUpdate, which hid the same bug behind a
    /// per-frame correction. The cannon seats you once, so the drift stuck.
    /// </summary>
    [ServerRpc]
    private void SeatServerRpc(Vector2 seatPosition)
    {
        serverStationLocked = true;
        serverMoveInput = Vector2.zero;

        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.position = seatPosition;

        transform.position = new Vector3(
            seatPosition.x,
            seatPosition.y,
            0f
        );

        rb.WakeUp();
    }

    /// <summary>Tell the server the player has stood up and may walk again.</summary>
    [ServerRpc]
    private void ReleaseStationServerRpc()
    {
        serverStationLocked = false;

        // Start them still: the input that reaches the server next should be
        // a fresh one, not whatever was in flight when they sat down.
        serverMoveInput = Vector2.zero;
    }

    private void FixedUpdate()
    {
        if (!IsSpawned || !IsServer)
        {
            return;
        }

        // Seated: hold still. The station owns this player's position now,
        // and integrating input here is what used to slide them off the gun.
        if (serverStationLocked)
        {
            serverMoveInput = Vector2.zero;
            rb.linearVelocity = Vector2.zero;
            return;
        }

        if (Time.realtimeSinceStartup < serverMovementUnlockTime)
        {
            serverMoveInput = Vector2.zero;
            rb.linearVelocity = Vector2.zero;
            return;
        }

        // Dead-man switch: the owner streams input at least every 0.05 s
        // while alive. If the stream stops (death, disconnect, freeze),
        // the server must not keep moving the player forever.
        if (
            serverMoveInput != Vector2.zero &&
            Time.realtimeSinceStartup >= serverInputExpiryTime
        )
        {
            serverMoveInput = Vector2.zero;
        }

        float effectiveSpeed = moveSpeed *
            (loadout != null ? loadout.MoveSpeedMultiplier : 1f);

        Vector2 nextPosition =
            rb.position +
            serverMoveInput * effectiveSpeed * Time.fixedDeltaTime;

        rb.MovePosition(nextPosition);
    }
}
