using System;
using System.Collections.Generic;
using DeadmansTales.Networking;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class LegacyTemplateCleanupValidator
{
    private const string MenuPath =
        "Deadman's Tales/Validate Legacy Template Cleanup";

    private const string SettingsAssetPath =
        "Assets/DeadmansTales/Resources/Networking/" +
        "DeadmansNetworkBootstrapSettings.asset";

    private static readonly string[] LegacyPaths =
    {
        "Assets/Blocks",
        "Assets/Core",
        "Assets/Platformer",
        "Assets/Shooter",
        "Assets/TutorialInfo",
        "Assets/Readme.asset",
        "Assets/DefaultNetworkPrefabs.asset"
    };

    [MenuItem(MenuPath)]
    public static void ValidateCleanup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning(
                "Exit Play Mode before validating the template cleanup."
            );
            return;
        }

        int passed = 0;
        List<string> failures = new List<string>();

        RunCheck(
            "Legacy sample assets are removed",
            ValidateLegacyPaths,
            ref passed,
            failures
        );

        RunCheck(
            "Project-owned network bootstrap is configured",
            ValidateNetworkBootstrap,
            ref passed,
            failures
        );

        RunCheck(
            "Deadman's Tales prefabs have no missing scripts or references",
            ValidatePrefabs,
            ref passed,
            failures
        );

        RunCheck(
            "Deadman's Tales scenes have no missing scripts or references",
            ValidateScenes,
            ref passed,
            failures
        );

        RunCheck(
            "Build Settings contain only valid retained scenes",
            ValidateBuildSettings,
            ref passed,
            failures
        );

        if (failures.Count > 0)
        {
            Debug.LogError(
                "[Legacy Cleanup Validation] FAILED\n" +
                string.Join("\n", failures)
            );
            return;
        }

        Debug.Log(
            $"[Legacy Cleanup Validation] PASS: {passed}/5 checks passed.\n" +
            "The deleted 3D template folders are no longer required by the " +
            "Deadman's Tales assets."
        );
    }

    private static void ValidateLegacyPaths()
    {
        foreach (string path in LegacyPaths)
        {
            if (
                AssetDatabase.IsValidFolder(path) ||
                AssetDatabase.LoadMainAssetAtPath(path) != null
            )
            {
                throw new InvalidOperationException(
                    $"Legacy sample path still exists: {path}"
                );
            }
        }
    }

    private static void ValidateNetworkBootstrap()
    {
        DeadmansNetworkBootstrapSettings settings =
            AssetDatabase.LoadAssetAtPath<DeadmansNetworkBootstrapSettings>(
                SettingsAssetPath
            );

        if (settings == null)
        {
            throw new InvalidOperationException(
                $"Missing bootstrap settings asset: {SettingsAssetPath}"
            );
        }

        GameObject playerPrefab = settings.PlayerPrefab;
        if (playerPrefab == null)
        {
            throw new InvalidOperationException(
                "The bootstrap settings do not reference a player prefab."
            );
        }

        string playerPath = AssetDatabase.GetAssetPath(playerPrefab);
        if (!playerPath.StartsWith("Assets/DeadmansTales/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The player prefab is outside DeadmansTales: {playerPath}"
            );
        }

        if (playerPrefab.GetComponent<NetworkObject>() == null)
        {
            throw new InvalidOperationException(
                $"The player prefab has no NetworkObject: {playerPath}"
            );
        }

        if (playerPrefab.GetComponent<TopDownNetworkPlayer2D>() == null)
        {
            throw new InvalidOperationException(
                $"The bootstrap player is not the custom 2D player: {playerPath}"
            );
        }

        foreach (GameObject additionalPrefab in settings.AdditionalNetworkPrefabs)
        {
            if (additionalPrefab == null)
            {
                throw new InvalidOperationException(
                    "The additional network-prefab list contains an empty slot."
                );
            }

            if (additionalPrefab.GetComponent<NetworkObject>() == null)
            {
                throw new InvalidOperationException(
                    $"Additional prefab '{additionalPrefab.name}' has no " +
                    "NetworkObject component."
                );
            }
        }
    }

    private static void ValidatePrefabs()
    {
        string[] prefabGuids = AssetDatabase.FindAssets(
            "t:Prefab",
            new[] { "Assets/DeadmansTales" }
        );

        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);

            try
            {
                ValidateHierarchy(root, path);
                ValidateDependencies(path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }

    private static void ValidateScenes()
    {
        SceneSetup[] previousSetup =
            EditorSceneManager.GetSceneManagerSetup();

        try
        {
            string[] sceneGuids = AssetDatabase.FindAssets(
                "t:Scene",
                new[] { "Assets/DeadmansTales" }
            );

            foreach (string guid in sceneGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Scene scene = EditorSceneManager.OpenScene(
                    path,
                    OpenSceneMode.Single
                );

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    ValidateHierarchy(root, path);
                }

                ValidateDependencies(path);
            }
        }
        finally
        {
            EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
        }
    }

    private static void ValidateBuildSettings()
    {
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (string.IsNullOrWhiteSpace(scene.path))
            {
                throw new InvalidOperationException(
                    "Build Settings contain an empty scene path."
                );
            }

            if (IsLegacyPath(scene.path))
            {
                throw new InvalidOperationException(
                    $"Build Settings still reference a deleted sample scene: " +
                    scene.path
                );
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path) == null)
            {
                throw new InvalidOperationException(
                    $"Build Settings reference a missing scene: {scene.path}"
                );
            }
        }
    }

    private static void ValidateHierarchy(
        GameObject root,
        string assetPath
    )
    {
        foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
        {
            GameObject gameObject = transform.gameObject;
            int missingScriptCount =
                GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                    gameObject
                );

            if (missingScriptCount > 0)
            {
                throw new InvalidOperationException(
                    $"{assetPath} contains {missingScriptCount} missing " +
                    $"script(s) on '{GetHierarchyPath(transform)}'."
                );
            }

            foreach (Component component in gameObject.GetComponents<Component>())
            {
                if (component == null)
                {
                    continue;
                }

                ValidateSerializedReferences(
                    component,
                    assetPath,
                    GetHierarchyPath(transform)
                );
            }
        }
    }

    private static void ValidateSerializedReferences(
        Component component,
        string assetPath,
        string hierarchyPath
    )
    {
        SerializedObject serializedObject;
        try
        {
            serializedObject = new SerializedObject(component);
        }
        catch
        {
            return;
        }

        SerializedProperty property = serializedObject.GetIterator();
        bool enterChildren = true;

        while (property.NextVisible(enterChildren))
        {
            enterChildren = false;

            if (
                property.propertyType == SerializedPropertyType.ObjectReference &&
                property.objectReferenceValue == null &&
                property.objectReferenceInstanceIDValue != 0
            )
            {
                throw new InvalidOperationException(
                    $"{assetPath} has a missing reference at " +
                    $"'{hierarchyPath}/{component.GetType().Name}." +
                    $"{property.propertyPath}'."
                );
            }
        }
    }

    private static void ValidateDependencies(string assetPath)
    {
        foreach (string dependency in AssetDatabase.GetDependencies(assetPath, true))
        {
            if (IsLegacyPath(dependency))
            {
                throw new InvalidOperationException(
                    $"{assetPath} still depends on deleted sample content: " +
                    dependency
                );
            }
        }
    }

    private static bool IsLegacyPath(string path)
    {
        foreach (string legacyPath in LegacyPaths)
        {
            if (
                path.Equals(legacyPath, StringComparison.Ordinal) ||
                path.StartsWith(legacyPath + "/", StringComparison.Ordinal)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static string GetHierarchyPath(Transform transform)
    {
        string path = transform.name;
        Transform current = transform.parent;

        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    private static void RunCheck(
        string name,
        Action check,
        ref int passed,
        List<string> failures
    )
    {
        try
        {
            check();
            passed++;
            Debug.Log($"[Legacy Cleanup Validation] PASS: {name}");
        }
        catch (Exception exception)
        {
            failures.Add($"FAIL: {name}\n{exception.Message}");
            Debug.LogError(
                $"[Legacy Cleanup Validation] FAIL: {name}\n{exception}"
            );
        }
    }
}
