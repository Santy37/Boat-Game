using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal static class LegacyTemplateCleanupFinalizer
{
    private const string MenuPath =
        "Deadman's Tales/Finalize Legacy Template Cleanup";

    private static readonly string[] LegacyPrefixes =
    {
        "Assets/Blocks",
        "Assets/Core",
        "Assets/Platformer",
        "Assets/Shooter",
        "Assets/TutorialInfo"
    };

    [MenuItem(MenuPath)]
    public static void FinalizeCleanup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning(
                "Exit Play Mode before finalizing the template cleanup."
            );
            return;
        }

        int removedScenes = RemoveInvalidBuildScenes();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        Debug.Log(
            "[Legacy Cleanup Finalizer] Complete.\n" +
            $"Removed invalid Build Settings scenes: {removedScenes}\n" +
            "Unity's generated DefaultNetworkPrefabs registry was retained " +
            "and will be validated for legacy references.\n" +
            "Now run Deadman's Tales > Validate Legacy Template Cleanup."
        );
    }

    private static int RemoveInvalidBuildScenes()
    {
        EditorBuildSettingsScene[] existingScenes =
            EditorBuildSettings.scenes;

        List<EditorBuildSettingsScene> retainedScenes =
            new List<EditorBuildSettingsScene>(existingScenes.Length);

        int removedCount = 0;

        foreach (EditorBuildSettingsScene scene in existingScenes)
        {
            if (
                string.IsNullOrWhiteSpace(scene.path) ||
                IsLegacyPath(scene.path) ||
                AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path) == null
            )
            {
                Debug.Log(
                    $"[Legacy Cleanup Finalizer] Removed Build Settings entry: " +
                    scene.path
                );
                removedCount++;
                continue;
            }

            retainedScenes.Add(scene);
        }

        EditorBuildSettings.scenes = retainedScenes.ToArray();
        return removedCount;
    }

    private static bool IsLegacyPath(string path)
    {
        foreach (string prefix in LegacyPrefixes)
        {
            if (
                path.Equals(prefix, StringComparison.Ordinal) ||
                path.StartsWith(prefix + "/", StringComparison.Ordinal)
            )
            {
                return true;
            }
        }

        return false;
    }
}
