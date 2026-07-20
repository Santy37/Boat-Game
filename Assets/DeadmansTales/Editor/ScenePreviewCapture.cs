using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Renders a top-down camera snapshot of a scene to Logs/ for headless
/// visual verification after content builders run.
/// </summary>
public static class ScenePreviewCapture
{
    public static void CaptureBoatScene()
    {
        Capture(
            "Assets/DeadmansTales/Scenes/Boat_Gameplay_2D.unity",
            "codex-boat-preview.png",
            new Vector3(2f, 12f, -10f),
            18f
        );
    }

    public static void CaptureIslandScene()
    {
        Capture(
            "Assets/DeadmansTales/Scenes/Island_After_Ocean_01_2D.unity",
            "codex-island-full-preview.png",
            new Vector3(0f, 0.5f, -10f),
            24f
        );
    }

    private static void Capture(
        string scenePath,
        string outputFileName,
        Vector3 cameraPosition,
        float orthographicSize
    )
    {
        Scene scene = EditorSceneManager.OpenScene(
            scenePath,
            OpenSceneMode.Single
        );

        Camera camera = scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Camera>(true))
            .FirstOrDefault();

        if (camera == null)
        {
            Debug.LogError($"No camera found in {scenePath}.");
            return;
        }

        MonoBehaviour followScript = camera
            .GetComponents<MonoBehaviour>()
            .FirstOrDefault(component =>
                component.GetType().Name.Contains("Follow"));

        bool originalFollowEnabled =
            followScript != null && followScript.enabled;

        if (followScript != null)
        {
            followScript.enabled = false;
        }

        bool originalOrthographic = camera.orthographic;
        float originalSize = camera.orthographicSize;
        Vector3 originalPosition = camera.transform.position;
        Quaternion originalRotation = camera.transform.rotation;

        try
        {
            camera.orthographic = true;
            camera.orthographicSize = orthographicSize;
            camera.transform.SetPositionAndRotation(
                cameraPosition,
                Quaternion.identity
            );

            int width = 1600;
            int height = 900;

            RenderTexture renderTexture = new RenderTexture(
                width,
                height,
                24,
                RenderTextureFormat.ARGB32
            );
            Texture2D screenshot = new Texture2D(
                width,
                height,
                TextureFormat.RGBA32,
                false
            );
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;

            try
            {
                camera.targetTexture = renderTexture;
                RenderTexture.active = renderTexture;
                camera.Render();
                screenshot.ReadPixels(
                    new Rect(0f, 0f, width, height),
                    0,
                    0
                );
                screenshot.Apply();

                string outputFolder = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "Logs"
                );
                Directory.CreateDirectory(outputFolder);

                string outputPath =
                    Path.Combine(outputFolder, outputFileName);
                File.WriteAllBytes(outputPath, screenshot.EncodeToPNG());
                Debug.Log($"[Preview] Wrote {outputPath}");
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(screenshot);
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }
        }
        finally
        {
            camera.transform.SetPositionAndRotation(
                originalPosition,
                originalRotation
            );
            camera.orthographic = originalOrthographic;
            camera.orthographicSize = originalSize;

            if (followScript != null)
            {
                followScript.enabled = originalFollowEnabled;
            }
        }
    }

    public static void CaptureBothFromCommandLine()
    {
        CaptureBoatScene();
        CaptureIslandScene();
    }
}
