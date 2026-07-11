using UnityEngine;

public class Camera2DFollow : MonoBehaviour
{
    [Header("Camera")]
    [SerializeField] private float zOffset = -10f;
    [SerializeField] private float orthographicSize = 12f;

    [Header("Pixel Snapping")]
    [SerializeField] private bool snapToPixels = true;
    [SerializeField] private float pixelsPerUnit = 32f;

    private Transform target;
    private Camera cam;

    private void Awake()
    {
        cam = GetComponent<Camera>();
    }

    private void Start()
    {
        ApplyCameraSettings();
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            TryFindLocalPlayer();
        }

        Vector3 desiredPosition;

        if (target == null)
        {
            desiredPosition = new Vector3(0f, 0f, zOffset);
        }
        else
        {
            desiredPosition = new Vector3(target.position.x, target.position.y, zOffset);
        }

        if (snapToPixels)
        {
            float unit = 1f / pixelsPerUnit;
            desiredPosition.x = Mathf.Round(desiredPosition.x / unit) * unit;
            desiredPosition.y = Mathf.Round(desiredPosition.y / unit) * unit;
        }

        transform.position = desiredPosition;
        transform.rotation = Quaternion.identity;

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
            FindObjectsByType<TopDownNetworkPlayer2D>(FindObjectsSortMode.None);

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