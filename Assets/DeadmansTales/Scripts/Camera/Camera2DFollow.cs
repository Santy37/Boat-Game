using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class Camera2DFollow : MonoBehaviour
{
    [Header("Camera Mode")]
    [Tooltip("Leave this disabled to keep the camera centered on the island.")]
    [SerializeField] private bool followLocalPlayer = false;

    [Tooltip("The world-space center of the island.")]
    [SerializeField] private Vector2 islandCenter = new Vector2(2f, 12f);

    [Header("Large Island Bounds")]
    [SerializeField]
    [Tooltip("Clamp the camera so it never reveals the edge of the painted ocean.")]
    private bool clampToBounds;

    [SerializeField]
    [Tooltip("Trigger collider describing the complete painted world bounds.")]
    private BoxCollider2D movementBounds;

    [Header("Camera")]
    [SerializeField] private float zOffset = -10f;
    [SerializeField] private float orthographicSize = 11f;

    [Header("Pixel Snapping")]
    [SerializeField] private bool snapToPixels = true;
    [SerializeField] private float pixelsPerUnit = 32f;

    private Transform target;
    private Camera cam;

    /// <summary>
    /// Lets other systems (e.g. the ship's helm) read or drive the camera zoom.
    /// Camera2DFollow keeps applying this value to the camera each frame.
    /// </summary>
    public float OrthographicSize
    {
        get => orthographicSize;
        set => orthographicSize = value;
    }

    /// <summary>
    /// Lets other systems (e.g. the helm) switch the camera between following
    /// the player and holding the fixed island/water view.
    /// </summary>
    public bool FollowLocalPlayer
    {
        get => followLocalPlayer;
        set => followLocalPlayer = value;
    }

    private void Awake()
    {
        cam = GetComponent<Camera>();
        ApplyCameraSettings();
    }

    private void LateUpdate()
    {
        Vector3 desiredPosition;

        if (!followLocalPlayer)
        {
            // Fixed island view.
            desiredPosition = new Vector3(
                islandCenter.x,
                islandCenter.y,
                zOffset
            );
        }
        else
        {
            // Only search for a player when player-following is enabled.
            if (target == null)
            {
                TryFindLocalPlayer();
            }

            if (target != null)
            {
                desiredPosition = new Vector3(
                    target.position.x,
                    target.position.y,
                    zOffset
                );
            }
            else
            {
                // Before the host/client starts, stay on the island.
                desiredPosition = new Vector3(
                    islandCenter.x,
                    islandCenter.y,
                    zOffset
                );
            }
        }

        if (snapToPixels && pixelsPerUnit > 0f)
        {
            float worldUnitsPerPixel = 1f / pixelsPerUnit;

            desiredPosition.x =
                Mathf.Round(desiredPosition.x / worldUnitsPerPixel)
                * worldUnitsPerPixel;

            desiredPosition.y =
                Mathf.Round(desiredPosition.y / worldUnitsPerPixel)
                * worldUnitsPerPixel;
        }

        if (clampToBounds && movementBounds != null && cam != null)
        {
            Bounds bounds = movementBounds.bounds;
            float verticalExtent = cam.orthographicSize;
            float horizontalExtent = verticalExtent * cam.aspect;

            desiredPosition.x = ClampCameraAxis(
                desiredPosition.x,
                bounds.min.x,
                bounds.max.x,
                horizontalExtent
            );

            desiredPosition.y = ClampCameraAxis(
                desiredPosition.y,
                bounds.min.y,
                bounds.max.y,
                verticalExtent
            );
        }

        transform.SetPositionAndRotation(
            desiredPosition,
            Quaternion.identity
        );

        ApplyCameraSettings();
    }

    private void ApplyCameraSettings()
    {
        if (cam == null)
        {
            return;
        }

        cam.orthographic = true;
        cam.orthographicSize = orthographicSize;
    }

    private void TryFindLocalPlayer()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && manager.IsListening && manager.IsClient)
        {
            NetworkObject localPlayer = manager.LocalClient?.PlayerObject;
            if (localPlayer != null)
            {
                target = localPlayer.transform;
                return;
            }
        }

        // Direct scene play and lightweight test scenes can exist without a
        // listening NetworkManager. Keep an owner-only fallback for those.
        TopDownNetworkPlayer2D[] players =
            FindObjectsByType<TopDownNetworkPlayer2D>(
                FindObjectsSortMode.None
            );

        foreach (TopDownNetworkPlayer2D player in players)
        {
            if (player.IsOwner)
            {
                target = player.transform;
                return;
            }
        }
    }

    private static float ClampCameraAxis(
        float value,
        float minimum,
        float maximum,
        float cameraExtent
    )
    {
        float availableSize = maximum - minimum;
        float requiredSize = cameraExtent * 2f;

        if (availableSize <= requiredSize)
        {
            return (minimum + maximum) * 0.5f;
        }

        return Mathf.Clamp(
            value,
            minimum + cameraExtent,
            maximum - cameraExtent
        );
    }
}
