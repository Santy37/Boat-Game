using UnityEngine;

public class TopDownCameraFollow : MonoBehaviour
{
    [Header("Camera Position")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 12f, -10f);

    [Header("Camera Rotation")]
    [SerializeField] private Vector3 rotationEuler = new Vector3(50f, 0f, 0f);

    [Header("Follow Settings")]
    [SerializeField] private float followSpeed = 12f;

    private Transform target;

    private void Start()
    {
        Camera cam = GetComponent<Camera>();

        if (cam != null)
        {
            cam.orthographic = true;
            cam.orthographicSize = 8f;
        }

        transform.rotation = Quaternion.Euler(rotationEuler);
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            TryFindLocalPlayer();
        }

        if (target == null)
        {
            return;
        }

        Vector3 desiredPosition = target.position + offset;

        transform.position = Vector3.Lerp(
            transform.position,
            desiredPosition,
            followSpeed * Time.deltaTime
        );

        transform.rotation = Quaternion.Euler(rotationEuler);
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