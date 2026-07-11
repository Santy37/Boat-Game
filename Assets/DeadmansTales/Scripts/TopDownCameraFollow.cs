using UnityEngine;

public class TopDownCameraFollow : MonoBehaviour
{
    [Header("Camera Position")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 24f, 0f);

    [Header("Camera Rotation")]
    [SerializeField] private Vector3 rotationEuler = new Vector3(90f, 0f, 0f);

    [Header("Camera Zoom")]
    [SerializeField] private float orthographicSize = 14f;

    [Header("Follow Settings")]
    [SerializeField] private float followSpeed = 12f;

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

        if (target == null)
        {
            // Before the player spawns, keep the camera centered on the island.
            transform.position = offset;
            ApplyCameraSettings();
            return;
        }

        Vector3 targetFlatPosition = new Vector3(target.position.x, 0f, target.position.z);
        Vector3 desiredPosition = targetFlatPosition + offset;

        transform.position = Vector3.Lerp(
            transform.position,
            desiredPosition,
            followSpeed * Time.deltaTime
        );

        ApplyCameraSettings();
    }

    private void ApplyCameraSettings()
    {
        transform.rotation = Quaternion.Euler(rotationEuler);

        if (cam != null)
        {
            cam.orthographic = true;
            cam.orthographicSize = orthographicSize;
        }
    }

    private void TryFindLocalPlayer()
    {
        TopDownNetworkPlayerController[] players =
            FindObjectsByType<TopDownNetworkPlayerController>(FindObjectsSortMode.None);

        foreach (TopDownNetworkPlayerController player in players)
        {
            if (player.IsOwner)
            {
                target = player.transform;
                return;
            }
        }
    }
}