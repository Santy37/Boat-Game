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

    private const string GeneratedNetworkPrefabsPath =
        "Assets/DefaultNetworkPrefabs.asset";

    private static readonly string[] LegacyPaths =
    {
        "Assets/Blocks",
        "Assets/Core",
        "Assets/Platformer",
        "Assets/Shooter",
        "Assets/TutorialInfo",
        "Assets/Readme.asset"
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

        ValidateNetworkPrefab(
            settings.PlayerPrefab,
            "bootstrap player",
            requireCustomPlayer: true
        );

        foreach (GameObject additionalPrefab in settings.AdditionalNetworkPrefabs)
        {
            ValidateNetworkPrefab(
                additionalPrefab,
                "additional network prefab",
                requireCustomPlayer: false
            );
        }

        ValidateGeneratedNetworkPrefabRegistry();
    }

    private static void ValidateNetworkPrefab(
        GameObject prefab,
        string description,
        bool requireCustomPlayer
    )
    {
        if (prefab == null)
        {
            throw new InvalidOperationException(
                $"The {description} reference is empty."
            );
        }

        string prefabPath = AssetDatabase.GetAssetPath(prefab);
        if (!prefabPath.StartsWith("Assets/DeadmansTales/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The {description} is outside DeadmansTales: {prefabPath}"
            );
        }

        if (prefab.GetComponent<NetworkObject>() == null)
        {
            throw new InvalidOperationException(
                $"The {description} has no NetworkObject: {prefabPath}"
            );
        }

        if (
            requireCustomPlayer &&
            prefab.GetComponent<TopDownNetworkPlayer2D>() == null
        )
        {
            throw new InvalidOperationException(
                $"The bootstrap player is not the custom 2D player: {prefabPath}"
            );
        }
    }

    private static void ValidateGeneratedNetworkPrefabRegistry()
    {
        UnityEngine.Object registry =
            AssetDatabase.LoadMainAssetAtPath(GeneratedNetworkPrefabsPath);

        // NGO may create this registry automatically. The runtime bootstrap does
        // not require the file, so its absence is also valid.
        if (registry == null)
        {
            return;
        }

        ValidateSerializedObjectReferences(
            registry,
            GeneratedNetworkPrefabsPath,
            registry.name
        );

        foreach (
            string dependency in
            AssetDatabase.GetDependencies(GeneratedNetworkPrefabsPath, true)
        )
        {
            if (dependency.Equals(GeneratedNetworkPrefabsPath, StringComparison.Ordinal))
            {
                continue;
            }

            if (IsLegacyPath(dependency))
            {
                throw new InvalidOperationException(
                    $"The generated NGO prefab registry still references " +
                    $"deleted sample content: {dependency}"
                );
            }

            if (
                dependency.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) &&
                !dependency.StartsWith(
                    "Assets/DeadmansTales/",
                    StringComparison.Ordinal
                )
            )
            {
                throw new InvalidOperationException(
                    $"The generated NGO prefab registry references a prefab " +
                    $"outside DeadmansTales: {dependency}"
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

                ValidateSerializedObjectReferences(
                    component,
                    assetPath,
                    GetHierarchyPath(transform) + "/" + component.GetType().Name
                );
            }
        }
    }

    private static void ValidateSerializedObjectReferences(
        UnityEngine.Object target,
        string assetPath,
        string context
    )
    {
        SerializedObject serializedObject;
        try
        {
            serializedObject = new SerializedObject(target);
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
                    $"'{context}.{property.propertyPath}'."
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
