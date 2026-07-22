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

    private PlayerCharacter playerInRange;
    private PlayerCharacter operatorPlayer;
    private Rigidbody2D operatorBody;

    private Vector3 shipHome;
    private Vector2 steerOffset;

    private LocalCoopCamera coopCamera;
    private Camera2DFollow cameraFollow;
    private float defaultCameraZoom;

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
        }

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
        if (Manned)
        {
            Vector2 input = operatorPlayer.Bindings != null
                ? operatorPlayer.RawMoveInput
                : new Vector2(
                    Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));

            steerOffset += input * (moveSpeed * Time.deltaTime);
            steerOffset.x = Mathf.Clamp(steerOffset.x, -moveBounds.x, moveBounds.x);
            steerOffset.y = Mathf.Clamp(steerOffset.y, -moveBounds.y, moveBounds.y);
        }

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

        // Keep the operator glued to the wheel as the ship moves.
        if (Manned && standPoint != null)
        {
            operatorPlayer.transform.position = standPoint.position;

            if (operatorBody != null)
            {
                operatorBody.position = standPoint.position;
            }
        }
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

    private void OnGUI()
    {
        if (playerInRange == null && !Manned)
        {
            return;
        }

        const float width = 400f;
        const float height = 46f;

        Rect rect = new Rect(
            (Screen.width - width) * 0.5f,
            Screen.height - 150f,
            width,
            height);

        if (Manned)
        {
            KeyBindings keys = operatorPlayer.Bindings;
            string leave = keys != null
                ? keys.interact.ToString() : fallbackManKey.ToString();

            GUI.Box(rect, $"Move keys steer the ship   |   {leave}: Leave Helm");
        }
        else
        {
            KeyBindings keys = playerInRange.Bindings;
            string use = keys != null
                ? keys.interact.ToString() : fallbackManKey.ToString();

            GUI.Box(rect, $"Press {use} to Take Helm");
        }
    }
}
