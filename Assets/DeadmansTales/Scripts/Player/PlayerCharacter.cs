using UnityEngine;

/// <summary>
/// The shared "face" of a player, present on every player prefab (2D and 3D,
/// local and — later — network).
///
/// Gameplay (stations, triggers, cameras) talks to THIS, never to a specific
/// movement driver. That is what lets one run manager serve both a 2D sprite
/// scene and a 3D island scene.
/// </summary>
public class PlayerCharacter : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private int playerIndex;
    [SerializeField] private KeyBindings bindings;

    public int PlayerIndex => playerIndex;

    public KeyBindings Bindings => bindings;

    /// <summary>Run data owned by the run manager and handed to us on spawn.</summary>
    public PlayerRunData Data { get; private set; }

    /// <summary>
    /// True when this machine drives this character: a local player with key
    /// bindings, or a networked player owned by this client.
    /// </summary>
    public bool IsControlledHere
    {
        get
        {
            if (bindings != null)
            {
                return true;
            }

            return networkPlayer != null && networkPlayer.IsOwner;
        }
    }

    /// <summary>True while seated at a station (cannon/helm).</summary>
    public bool ControlLocked { get; private set; }

    /// <summary>Movement input, zeroed while seated at a station.</summary>
    public Vector2 MoveInput { get; private set; }

    /// <summary>
    /// Movement keys as pressed, even while seated. Stations use this so the
    /// player manning a cannon can aim, or a helm can steer.
    /// </summary>
    public Vector2 RawMoveInput { get; private set; }

    public bool InteractDown { get; private set; }
    public bool FireDown { get; private set; }

    private PlayerAnimation2D playerAnimation;
    private TopDownNetworkPlayer2D networkPlayer;

    private void Awake()
    {
        playerAnimation = GetComponentInChildren<PlayerAnimation2D>();
        networkPlayer = GetComponent<TopDownNetworkPlayer2D>();

        if (Data == null)
        {
            Data = new PlayerRunData { playerIndex = playerIndex };
        }
    }

    /// <summary>
    /// Called by the run manager immediately after spawning this player into a
    /// scene: assigns which player this is, their keys, and their saved data.
    /// </summary>
    public void Configure(int index, KeyBindings keyBindings, PlayerRunData data)
    {
        playerIndex = index;
        bindings = keyBindings;
        Data = data ?? new PlayerRunData { playerIndex = index };

        gameObject.name = $"Player_{index + 1}";
    }

    private void Update()
    {
        if (bindings == null)
        {
            MoveInput = Vector2.zero;
            RawMoveInput = Vector2.zero;
            InteractDown = false;
            FireDown = false;
            return;
        }

        // Action keys still work while seated, so you can fire and stand up.
        InteractDown = bindings.InteractDown();
        FireDown = bindings.FireDown();

        RawMoveInput = bindings.ReadMove();
        MoveInput = ControlLocked ? Vector2.zero : RawMoveInput;
    }

    /// <summary>Snap to a station seat, face a direction, and stop moving.</summary>
    public void EnterStation(Vector3 seatPosition, Vector2 facing)
    {
        ControlLocked = true;
        MoveInput = Vector2.zero;

        // Networked players are server-authoritative: their own script does the
        // seating through a ServerRpc, so route it there instead of moving the
        // transform locally (which the server would immediately overwrite).
        if (networkPlayer != null)
        {
            networkPlayer.EnterStation(seatPosition, facing);
            return;
        }

        transform.position = seatPosition;

        if (TryGetComponent(out Rigidbody2D body))
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.position = seatPosition;
        }

        if (playerAnimation != null)
        {
            playerAnimation.LockFacing(facing);
        }
    }

    /// <summary>Release from a station and restore movement.</summary>
    public void ExitStation()
    {
        ControlLocked = false;

        if (networkPlayer != null)
        {
            networkPlayer.ExitStation();
            return;
        }

        if (playerAnimation != null)
        {
            playerAnimation.UnlockFacing();
        }
    }
}
