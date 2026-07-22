using UnityEngine;

/// <summary>
/// The ship's wheel. Press the man key to take it (the player is snapped,
/// faced, frozen, and rides the ship). While manning, WASD/arrows move the
/// whole ship within a bounded area centered on its start position, so it never
/// drifts off the water. The camera holds a fixed, zoomed-out view while
/// steering so the movement is actually visible.
/// </summary>
public class ShipHelm : MonoBehaviour
{
    [Header("Interaction")]
    [SerializeField] private KeyCode manKey = KeyCode.E;

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
    [Tooltip("Camera orthographic size while steering (larger = zoomed out).")]
    [SerializeField] private float steeringCameraZoom = 16f;
    [Tooltip("How fast the camera eases between normal and steering zoom.")]
    [SerializeField] private float zoomLerpSpeed = 5f;

    private TopDownNetworkPlayer2D playerInRange;
    private Rigidbody2D playerBody;
    private bool manned;

    private Vector3 shipHome;
    private Vector2 steerOffset;

    private Camera2DFollow cameraFollow;
    private float defaultCameraZoom;

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
                this
            );
        }

        if (ship != null)
        {
            shipHome = ship.localPosition;
        }

        cameraFollow = Object.FindFirstObjectByType<Camera2DFollow>();
        if (cameraFollow != null)
        {
            defaultCameraZoom = cameraFollow.OrthographicSize;
        }
    }

    private void Update()
    {
        if (playerInRange != null && Input.GetKeyDown(manKey))
        {
            if (manned) Leave();
            else Man();
        }

        UpdateCameraZoom();
    }

    private void LateUpdate()
    {
        if (manned)
        {
            Vector2 input = new Vector2(
                Input.GetAxisRaw("Horizontal"),
                Input.GetAxisRaw("Vertical"));

            steerOffset += input * (moveSpeed * Time.deltaTime);
            steerOffset.x = Mathf.Clamp(steerOffset.x, -moveBounds.x, moveBounds.x);
            steerOffset.y = Mathf.Clamp(steerOffset.y, -moveBounds.y, moveBounds.y);
        }

        // Hold the steered spot, but only once there is one to hold.
        //
        // This used to write every frame unconditionally, to override any
        // other position writer on the ship. With the ship reference finally
        // filled in, that meant BoatBob wrote the ship's transform in Update
        // and this wrote it again in LateUpdate, every frame of the game,
        // for no gain: BoatBob is configured with a zero bob and zero rock,
        // so both were writing the same value. Two writes a frame to the
        // transform every collider on the ship hangs off is not free — it
        // re-syncs them all, which is enough to make a trigger the player is
        // only just inside report an exit.
        if (ship != null && (manned || steerOffset != Vector2.zero))
        {
            ship.localPosition = shipHome + (Vector3)steerOffset;
        }

        // Keep the seated player glued to the wheel as the ship moves.
        if (manned && playerInRange != null && standPoint != null)
        {
            playerInRange.transform.position = standPoint.position;
            if (playerBody != null)
            {
                playerBody.position = standPoint.position;
            }
        }
    }

    private void Man()
    {
        manned = true;
        playerBody = playerInRange.GetComponent<Rigidbody2D>();

        Vector2 seat = standPoint != null
            ? (Vector2)standPoint.position
            : (Vector2)transform.position;

        playerInRange.EnterStation(seat, facing);

        // Hold a fixed view so the ship's movement is visible on screen.
        if (cameraFollow != null)
        {
            cameraFollow.FollowLocalPlayer = false;
        }
    }

    private void Leave()
    {
        manned = false;
        playerInRange.ExitStation();

        if (cameraFollow != null)
        {
            cameraFollow.FollowLocalPlayer = true;
        }
    }

    // Ease the camera out to the steering zoom while manning, back when leaving.
    private void UpdateCameraZoom()
    {
        if (cameraFollow == null)
        {
            return;
        }

        float targetZoom = manned ? steeringCameraZoom : defaultCameraZoom;

        cameraFollow.OrthographicSize = Mathf.Lerp(
            cameraFollow.OrthographicSize,
            targetZoom,
            zoomLerpSpeed * Time.deltaTime
        );
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TopDownNetworkPlayer2D player =
            other.GetComponentInParent<TopDownNetworkPlayer2D>();

        if (player != null && player.IsOwner)
        {
            playerInRange = player;
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        TopDownNetworkPlayer2D player =
            other.GetComponentInParent<TopDownNetworkPlayer2D>();

        // Don't drop the player while they're manning: seating teleports them
        // (and the ship moves), which would otherwise fire an exit and un-man.
        if (player != null && player == playerInRange && !manned)
        {
            playerInRange = null;
        }
    }

    private void OnGUI()
    {
        if (playerInRange == null)
        {
            return;
        }

        const float width = 360f;
        const float height = 50f;

        Rect rect = new Rect(
            (Screen.width - width) * 0.5f,
            Screen.height - 100f,
            width,
            height
        );

        GUI.Box(
            rect,
            manned
                ? $"{manKey}: Leave Helm  —  WASD moves the ship"
                : $"Press {manKey} to Take Helm"
        );
    }
}
