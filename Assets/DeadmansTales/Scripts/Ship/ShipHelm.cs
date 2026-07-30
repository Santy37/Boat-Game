using System.Collections.Generic;
using DeadmansTales.Ship;
using UnityEngine;

/// <summary>
/// The ship's wheel. Walk into the trigger and press YOUR interact key to take
/// it: you are snapped to the wheel, faced, frozen, and the camera zooms out.
/// Your movement keys then move the whole ship within a bounded area centred on
/// its start position, so it never drifts off the water.
/// </summary>
public class ShipHelm : MonoBehaviour
{
    [Header("Player Placement")]
    [SerializeField] private Transform standPoint;
    [SerializeField] private Vector2 facing = Vector2.up;

    [Header("Ship Movement")]
    [Tooltip("The ship object to move (its Transform). Usually the Ship root.")]
    [SerializeField] private Transform ship;
    [Tooltip("Units per second the ship moves while steering.")]
    [SerializeField] private float moveSpeed = 3f;
    [Tooltip("How far (x, y) the ship may drift from its start before it stops.")]
    [SerializeField] private Vector2 moveBounds = new Vector2(5f, 3f);

    [Header("Camera")]
    [Tooltip("Camera size while steering (larger = zoomed out).")]
    [SerializeField] private float steeringCameraZoom = 20f;
    [Tooltip("Used by the networked camera only.")]
    [SerializeField] private float zoomLerpSpeed = 5f;

    [Header("Fallback Keys")]
    [Tooltip("Used only if the player has no key bindings (networked player).")]
    [SerializeField] private KeyCode fallbackManKey = KeyCode.E;

    [Header("Hull Separation")]
    [Tooltip(
        "Stop this ship from steering through enemy ships. Both hulls have " +
        "to stay triggers (cannonball hits and engagement rely on it), so " +
        "physics cannot block the move -- instead the overlap is undone " +
        "immediately after each move.")]
    [SerializeField] private bool pushOutOfEnemyHulls = true;

    [Tooltip(
        "Extra distance to open up beyond simply un-overlapping the hulls. " +
        "Leave at 0 to come to rest exactly touching; a small value backs " +
        "the ship off slightly further.")]
    [SerializeField] private float hullSeparationSkin = 0f;

    private PlayerCharacter playerInRange;
    private PlayerCharacter operatorPlayer;
    private Rigidbody2D operatorBody;

    // Resolved once: the ship's own networked steering component, if it has
    // one. Present only on ships that are actually multiplayer-networked
    // (see Boat_Gameplay_2D's Ship root); null on a purely local ship (or an
    // enemy ship's own copy of this helm). When set, a networked operator's
    // input is forwarded there instead of being applied to the Transform
    // directly -- see LateUpdate.
    private NetworkShipHelmSync networkSync;

    private Vector3 shipHome;
    private Vector2 steerOffset;

    private LocalCoopCamera coopCamera;
    private Camera2DFollow cameraFollow;
    private float defaultCameraZoom;

    // Resolved once: the hull collider of the ship this helm steers, and only
    // if that ship is the PLAYER's. The EnemyShip prefab carries a helm of its
    // own (it was built as a copy of the player's ship), and that one must
    // never start shoving ships around.
    private Collider2D steeredHull;

    private bool Manned => operatorPlayer != null;

    private void Awake()
    {
        // Every collider on the object, not just the first one Unity hands
        // back: the helm carries a solid box for the wheel and a trigger for
        // the spot the steersman stands on. GetComponent returned the solid
        // one, so this logged an error about a missing trigger that was
        // sitting right beside it.
        bool hasTriggerArea = false;

        foreach (Collider2D area in GetComponents<Collider2D>())
        {
            if (area != null && area.isTrigger)
            {
                hasTriggerArea = true;
                break;
            }
        }

        if (!hasTriggerArea)
        {
            Debug.LogError(
                $"[ShipHelm] '{name}' needs a Collider2D with Is Trigger " +
                "enabled on this same object to act as the interaction area.",
                this);
        }

        if (ship != null)
        {
            shipHome = ship.localPosition;
            networkSync = ship.GetComponent<NetworkShipHelmSync>();
        }

        ResolveSteeredHull();

        coopCamera = FindFirstObjectByType<LocalCoopCamera>();
        cameraFollow = FindFirstObjectByType<Camera2DFollow>();

        if (cameraFollow != null)
        {
            defaultCameraZoom = cameraFollow.OrthographicSize;
        }
    }

    private void Update()
    {
        if (Manned)
        {
            if (InteractPressed(operatorPlayer))
            {
                Leave();
            }
        }
        else if (playerInRange != null && InteractPressed(playerInRange))
        {
            Man(playerInRange);
        }

        UpdateNetworkCameraZoom();
    }

    private bool InteractPressed(PlayerCharacter player)
    {
        if (player == null)
        {
            return false;
        }

        return player.Bindings != null
            ? player.InteractDown
            : Input.GetKeyDown(fallbackManKey);
    }

    private void LateUpdate()
    {
        // True once this frame's input was handed to the server instead of
        // applied here -- a networked operator's ship. NetworkShipHelmSync
        // moves the ship and re-pins the seated player every server tick,
        // and NetworkTransform on both replicates the result to every peer,
        // so nothing below (local Transform writes, hull push-out, gluing
        // the operator to the seat) should also run: it would just fight
        // the incoming replication with a stale local guess.
        bool movedByServer = false;

        if (Manned)
        {
            Vector2 input = operatorPlayer.Bindings != null
                ? operatorPlayer.RawMoveInput
                : new Vector2(
                    Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));

            movedByServer = operatorPlayer.TrySteerShipNetworked(networkSync, input);

            if (!movedByServer)
            {
                steerOffset += input * (moveSpeed * Time.deltaTime);
                steerOffset.x = Mathf.Clamp(steerOffset.x, -moveBounds.x, moveBounds.x);
                steerOffset.y = Mathf.Clamp(steerOffset.y, -moveBounds.y, moveBounds.y);
            }
        }

        if (movedByServer)
        {
            UpdatePrompt();
            return;
        }

        // ---- Local / split-screen ship: unchanged from before. ----

        // Hold the steered spot, but only once there is one to hold.
        //
        // This used to write every frame unconditionally, to override any
        // other position writer on the ship. That meant BoatBob wrote the
        // ship's transform in Update and this wrote it again in LateUpdate,
        // every frame of the game, for no gain: BoatBob is configured with a
        // zero bob and zero rock, so both were writing the same value. Two
        // writes a frame to the transform every collider on the ship hangs
        // off is not free -- it re-syncs them all, which is enough to make a
        // trigger the player is only just inside report an exit.
        if (ship != null && (Manned || steerOffset != Vector2.zero))
        {
            ship.localPosition = shipHome + (Vector3)steerOffset;
        }

        // Undo any hull overlap the move just created, BEFORE the operator is
        // repositioned below -- otherwise the steersman gets glued to a wheel
        // that is about to be shoved somewhere else this same frame.
        PushOutOfEnemyHulls();

        // Keep the operator glued to the wheel as the ship moves.
        if (Manned && standPoint != null)
        {
            operatorPlayer.transform.position = standPoint.position;

            if (operatorBody != null)
            {
                operatorBody.position = standPoint.position;
            }
        }

        UpdatePrompt();
    }

    // Only the PLAYER's ship gets pushed out of things, so this resolves the
    // hull through PlayerShipMarker: no marker on the steered ship means this
    // helm belongs to an enemy ship and separation stays off entirely.
    private void ResolveSteeredHull()
    {
        if (ship == null)
        {
            return;
        }

        PlayerShipMarker marker = ship.GetComponentInParent<PlayerShipMarker>();

        if (marker == null)
        {
            return;
        }

        steeredHull = marker.Hitbox;

        if (steeredHull == null)
        {
            Debug.LogWarning(
                $"[ShipHelm] '{name}' steers the player's ship but its " +
                "PlayerShipMarker has no Hitbox wired, so it cannot be kept " +
                "out of enemy hulls. Assign the ship's hull collider " +
                "(e.g. ShipHitBox) on the marker.",
                this);
        }
    }

    /// <summary>
    /// Post-move overlap resolution: shoves the player's ship straight back
    /// out of any enemy hull it is currently inside.
    ///
    /// A collider type change is NOT an option here -- both the player's
    /// ShipHitBox and the enemy hull must stay triggers, because cannonball
    /// hit detection and <see cref="EnemyShipHullContact"/> engagement are
    /// both driven by trigger events. And even solid colliders would not help:
    /// both ships move by writing their Transform directly (this helm for the
    /// player, EnemyShipApproach for the enemy), which bypasses the physics
    /// solver, so there is nothing for it to block. The only thing left is to
    /// let the move happen and immediately correct it, which is what this does.
    ///
    /// The correction is applied to <c>steerOffset</c> rather than straight to
    /// the Transform, because LateUpdate rewrites the position from that offset
    /// every frame -- correcting only the Transform would be overwritten on the
    /// very next frame and the ship would keep grinding through the hull.
    /// </summary>
    private void PushOutOfEnemyHulls()
    {
        if (!pushOutOfEnemyHulls || ship == null || steeredHull == null)
        {
            return;
        }

        if (!steeredHull.enabled || !steeredHull.gameObject.activeInHierarchy)
        {
            return;
        }

        IReadOnlyList<EnemyShipHullContact> hulls = EnemyShipHullContact.Active;

        if (hulls.Count == 0)
        {
            return;
        }

        // The position write above went to the Transform, but Physics2D keeps
        // its own copy of collider positions and (with autoSyncTransforms off,
        // which is the modern default) only refreshes it at the next physics
        // step. Querying without this reads the collider's location from last
        // frame, so the overlap measured would be a frame stale.
        Physics2D.SyncTransforms();

        Vector2 correction = Vector2.zero;

        foreach (EnemyShipHullContact hull in hulls)
        {
            if (hull == null || hull.Hull == null)
            {
                continue;
            }

            Collider2D enemyHull = hull.Hull;

            // Skip our own hull, in the event a helm ever steers a ship that
            // carries this component, and anything switched off -- Distance
            // against a disabled collider returns an invalid result.
            if (enemyHull == steeredHull ||
                !enemyHull.enabled ||
                !enemyHull.gameObject.activeInHierarchy)
            {
                continue;
            }

            ColliderDistance2D separation =
                Physics2D.Distance(enemyHull, steeredHull);

            if (!separation.isValid || !separation.isOverlapped)
            {
                continue;
            }

            // pointA sits on the enemy hull, pointB on ours, so this vector is
            // the shortest move that lifts our hull back out of theirs. Taking
            // the largest single correction rather than the sum keeps the ship
            // from being flung when it overlaps two hulls at once.
            Vector2 push = separation.pointA - separation.pointB;

            if (hullSeparationSkin > 0f && push != Vector2.zero)
            {
                push += push.normalized * hullSeparationSkin;
            }

            if (push.sqrMagnitude > correction.sqrMagnitude)
            {
                correction = push;
            }
        }

        if (correction == Vector2.zero)
        {
            return;
        }

        // World-space push into the ship's own parent space, which is what
        // steerOffset and shipHome are expressed in.
        if (ship.parent != null)
        {
            correction = ship.parent.InverseTransformVector(correction);
        }

        steerOffset += correction;
        steerOffset.x = Mathf.Clamp(steerOffset.x, -moveBounds.x, moveBounds.x);
        steerOffset.y = Mathf.Clamp(steerOffset.y, -moveBounds.y, moveBounds.y);

        // moveBounds still wins: keeping the ship on the water matters more
        // than a clean separation, so an enemy parked on the boundary can still
        // overlap rather than shoving the player off the playable area.
        ship.localPosition = shipHome + (Vector3)steerOffset;
    }

    // Drive the shared pirate-themed prompt panel instead of a legacy OnGUI
    // box, so the helm reads identically to every other interaction prompt.
    // As a claimed owner it wins the panel over the network proximity prompt.
    private void UpdatePrompt()
    {
        InteractionPromptHUD hud = InteractionPromptHUD.Instance;
        if (hud == null)
        {
            return;
        }

        if (Manned)
        {
            KeyBindings keys = operatorPlayer.Bindings;
            string leave = keys != null
                ? keys.interact.ToString() : fallbackManKey.ToString();

            hud.Show(
                $"WASD TO STEER ",
                this, InteractionPromptHUD.StationPriority);
        }
        else if (playerInRange != null)
        {
            KeyBindings keys = playerInRange.Bindings;
            string use = keys != null
                ? keys.interact.ToString() : fallbackManKey.ToString();

            hud.Show(
                $"PRESS {use} TO USE",
                this, InteractionPromptHUD.StationPriority);
        }
        else
        {
            hud.Hide(this);
        }
    }

    private void OnDisable()
    {
        // Release the panel if we're torn down while still owning it.
        InteractionPromptHUD.Instance?.Hide(this);
    }

    private void Man(PlayerCharacter player)
    {
        operatorPlayer = player;
        operatorBody = player.GetComponent<Rigidbody2D>();

        Vector3 seat = standPoint != null
            ? standPoint.position
            : transform.position;

        player.EnterStation(seat, facing);

        if (coopCamera != null)
        {
            coopCamera.SetZoomOverride(steeringCameraZoom);
        }

        // Networked camera: hold a fixed view so ship movement is visible.
        if (cameraFollow != null)
        {
            cameraFollow.FollowLocalPlayer = false;
        }
    }

    private void Leave()
    {
        if (operatorPlayer != null)
        {
            operatorPlayer.TryStopSteeringShipNetworked(networkSync);
            operatorPlayer.ExitStation();
        }

        operatorPlayer = null;
        operatorBody = null;

        if (coopCamera != null)
        {
            coopCamera.ClearZoomOverride();
        }

        if (cameraFollow != null)
        {
            cameraFollow.FollowLocalPlayer = true;
        }
    }

    private void UpdateNetworkCameraZoom()
    {
        if (cameraFollow == null)
        {
            return;
        }

        float targetZoom = Manned ? steeringCameraZoom : defaultCameraZoom;

        cameraFollow.OrthographicSize = Mathf.Lerp(
            cameraFollow.OrthographicSize,
            targetZoom,
            zoomLerpSpeed * Time.deltaTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerCharacter player = other.GetComponentInParent<PlayerCharacter>();

        if (player != null && player.IsControlledHere)
        {
            playerInRange = player;
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        PlayerCharacter player = other.GetComponentInParent<PlayerCharacter>();

        // Don't drop the operator: seating teleports them out of the trigger.
        if (player != null && player == playerInRange && !Manned)
        {
            playerInRange = null;
        }
    }

}
