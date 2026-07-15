using UnityEngine;

[RequireComponent(typeof(Camera))]
public class Camera2DFollow : MonoBehaviour
{
    [Header("Camera Mode")]
    [Tooltip("Leave this disabled to keep the camera centered on the island.")]
    [SerializeField] private bool followLocalPlayer = false;

    [Tooltip("The world-space center of the island.")]
    [SerializeField] private Vector2 islandCenter = new Vector2(2f, 12f);

    [Header("Camera")]
    [SerializeField] private float zOffset = -10f;
    [SerializeField] private float orthographicSize = 11f;

    [Header("Pixel Snapping")]
    [SerializeField] private bool snapToPixels = true;
    [SerializeField] private float pixelsPerUnit = 32f;

    private Transform target;
    private Camera cam;

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
}
