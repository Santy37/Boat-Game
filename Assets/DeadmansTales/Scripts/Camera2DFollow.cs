using System.Collections;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

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
    private bool cameraOwnershipApplied;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        TakeCameraOwnership();
        ApplyCameraSettings();
    }

    private void OnEnable()
    {
        StartCoroutine(LogRenderStateAfterSceneSettles());
    }

    private void LateUpdate()
    {
        if (!cameraOwnershipApplied)
        {
            TakeCameraOwnership();
        }

        Vector3 desiredPosition;

        if (!followLocalPlayer)
        {
            desiredPosition = new Vector3(
                islandCenter.x,
                islandCenter.y,
                zOffset
            );
        }
        else
        {
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

    private void TakeCameraOwnership()
    {
        if (cam == null)
        {
            return;
        }

        Camera[] cameras = FindObjectsByType<Camera>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (Camera otherCamera in cameras)
        {
            if (
                otherCamera == null ||
                otherCamera == cam ||
                otherCamera.targetDisplay != cam.targetDisplay
            )
            {
                continue;
            }

            otherCamera.enabled = false;
        }

        cam.enabled = true;
        cam.targetTexture = null;
        cam.targetDisplay = 0;
        cam.depth = 100f;
        cam.cullingMask = ~0;

        if (!cam.CompareTag("MainCamera"))
        {
            cam.tag = "MainCamera";
        }

        cameraOwnershipApplied = true;
    }

    private void ApplyCameraSettings()
    {
        if (cam == null)
        {
            return;
        }

        cam.orthographic = true;
        cam.orthographicSize = orthographicSize;
        cam.cullingMask = ~0;
        cam.depth = 100f;
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

    private IEnumerator LogRenderStateAfterSceneSettles()
    {
        yield return null;
        yield return new WaitForSecondsRealtime(0.5f);

        TilemapRenderer[] tilemaps =
            FindObjectsByType<TilemapRenderer>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

        SpriteRenderer[] sprites =
            FindObjectsByType<SpriteRenderer>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

        StringBuilder loadedScenes = new StringBuilder();
        int totalRoots = 0;

        for (int index = 0; index < SceneManager.sceneCount; index++)
        {
            Scene scene = SceneManager.GetSceneAt(index);
            if (index > 0)
            {
                loadedScenes.Append(", ");
            }

            loadedScenes.Append(scene.name);
            totalRoots += scene.rootCount;
        }

        string networkMode = "Offline";
        if (NetworkManager.Singleton != null)
        {
            if (NetworkManager.Singleton.IsHost)
            {
                networkMode = "Host";
            }
            else if (NetworkManager.Singleton.IsClient)
            {
                networkMode = "Client";
            }
            else if (NetworkManager.Singleton.IsServer)
            {
                networkMode = "Server";
            }
        }

        string message =
            $"[2D Camera] Mode={networkMode}, Scenes=[{loadedScenes}], " +
            $"Roots={totalRoots}, Tilemaps={tilemaps.Length}, " +
            $"Sprites={sprites.Length}, Position={transform.position}, " +
            $"CullingMask={cam.cullingMask}.";

        if (tilemaps.Length == 0 && sprites.Length == 0)
        {
            Debug.LogError(
                message + " No 2D world renderers are loaded in this process.",
                this
            );
        }
        else
        {
            Debug.Log(message, this);
        }
    }
}
