using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Repairs the NGO identity data on scene instances that existed before their
/// source prefab received a NetworkObject component. NGO cannot register those
/// instances until each one has a stable, scene-specific GlobalObjectIdHash.
/// </summary>
[InitializeOnLoad]
internal static class NetworkSceneIdentityRepair
{
    private const int MigrationVersion = 1;
    private const string MenuPath =
        "Deadman's Tales/Networking/Repair Scene Network Identities";

    private static readonly MethodInfo NetworkObjectOnValidate =
        typeof(NetworkObject).GetMethod(
            "OnValidate",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

    private static string MigrationKey =>
        $"DeadmansTales.NetworkSceneIdentityRepair." +
        $"{Hash128.Compute(Application.dataPath)}.{MigrationVersion}";

    static NetworkSceneIdentityRepair()
    {
        EditorApplication.delayCall += RunPendingMigration;
    }

    [MenuItem(MenuPath)]
    private static void RepairFromMenu()
    {
        EditorPrefs.DeleteKey(MigrationKey);
        RunPendingMigration();
    }

    public static void RepairFromCommandLine()
    {
        RepairNow();
    }

    /// <summary>
    /// Immediately revalidates and persists every NetworkObject identity used
    /// by enabled build scenes. Explicit callers intentionally bypass the
    /// one-time migration key so scene builders can safely run this after they
    /// create a new scene asset.
    /// </summary>
    public static int RepairNow()
    {
        if (
            EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling ||
            EditorApplication.isUpdating
        )
        {
            throw new InvalidOperationException(
                "Network scene identities cannot be repaired while Unity is " +
                "playing, compiling, or updating assets."
            );
        }

        EnsureLoadedBuildScenesAreSafeToRepair();

        int repairedCount = RepairEnabledBuildScenes();
        AssetDatabase.SaveAssets();
        EditorPrefs.SetBool(MigrationKey, true);

        Debug.Log(
            $"[Network Scene Identity Repair] Complete. " +
            $"Repaired {repairedCount} scene NetworkObject instance(s)."
        );

        return repairedCount;
    }

    private static void RunPendingMigration()
    {
        if (EditorPrefs.GetBool(MigrationKey, false))
        {
            return;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling ||
            EditorApplication.isUpdating)
        {
            EditorApplication.delayCall -= RunPendingMigration;
            EditorApplication.delayCall += RunPendingMigration;
            return;
        }

        try
        {
            RepairNow();
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[Network Scene Identity Repair] Could not finish. " +
                exception
            );
        }
    }

    private static void EnsureLoadedBuildScenesAreSafeToRepair()
    {
        EditorBuildSettingsScene dirtyBuildScene =
            EditorBuildSettings.scenes.FirstOrDefault(buildScene =>
            {
                if (!buildScene.enabled)
                {
                    return false;
                }

                Scene loadedScene = SceneManager.GetSceneByPath(
                    buildScene.path
                );
                return loadedScene.isLoaded && loadedScene.isDirty;
            });

        if (dirtyBuildScene != null)
        {
            throw new InvalidOperationException(
                $"Scene '{dirtyBuildScene.path}' has unsaved edits. Save or " +
                $"discard them before running '{MenuPath}' so the repair " +
                "cannot overwrite unrelated work."
            );
        }
    }

    private static int RepairEnabledBuildScenes()
    {
        if (NetworkObjectOnValidate == null)
        {
            throw new MissingMethodException(
                "Unity.Netcode.NetworkObject.OnValidate was not found."
            );
        }

        int repairedCount = RepairNetworkPrefabAssets();

        foreach (EditorBuildSettingsScene buildScene in
                 EditorBuildSettings.scenes.Where(scene => scene.enabled))
        {
            Scene scene = SceneManager.GetSceneByPath(buildScene.path);
            bool wasAlreadyLoaded = scene.isLoaded;
            bool wasDirtyBeforeRepair = wasAlreadyLoaded && scene.isDirty;

            if (!wasAlreadyLoaded)
            {
                scene = EditorSceneManager.OpenScene(
                    buildScene.path,
                    OpenSceneMode.Additive
                );
            }

            try
            {
                NetworkObject[] networkObjects = scene
                    .GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<NetworkObject>(true))
                    .ToArray();

                int sceneRepairCount = 0;
                // Saving every build scene that contains a NetworkObject also
                // persists OnValidate changes that occurred during scene
                // deserialization before this repair method started.
                bool requiresSave = scene.isDirty || networkObjects.Length > 0;

                foreach (NetworkObject networkObject in networkObjects)
                {
                    uint previousHash = networkObject.PrefabIdHash;
                    NetworkObjectOnValidate.Invoke(networkObject, null);

                    bool isPrefabInstance =
                        PrefabUtility.IsPartOfAnyPrefab(networkObject);

                    if (isPrefabInstance)
                    {
                        // Record this even when opening the scene already caused
                        // OnValidate to refresh the in-memory value. That legacy
                        // case is exactly what leaves the scene file stale.
                        PrefabUtility.RecordPrefabInstancePropertyModifications(
                            networkObject
                        );
                        requiresSave = true;
                    }

                    if (networkObject.PrefabIdHash != previousHash)
                    {
                        requiresSave = true;
                    }

                    if (requiresSave)
                    {
                        EditorUtility.SetDirty(networkObject);
                    }

                    if (
                        isPrefabInstance ||
                        networkObject.PrefabIdHash != previousHash
                    )
                    {
                        sceneRepairCount++;
                    }
                }

                ValidateSceneIdentities(scene, networkObjects);

                if (!requiresSave)
                {
                    continue;
                }

                repairedCount += sceneRepairCount;
                EditorSceneManager.MarkSceneDirty(scene);

                if (wasDirtyBeforeRepair)
                {
                    throw new InvalidOperationException(
                        $"Scene '{scene.path}' already had unsaved edits. " +
                        "Save or discard those edits, then run '" + MenuPath +
                        "' again so the repair does not overwrite unrelated work."
                    );
                }

                if (!EditorSceneManager.SaveScene(scene))
                {
                    throw new InvalidOperationException(
                        $"Unity could not save repaired scene '{scene.path}'."
                    );
                }
            }
            finally
            {
                if (!wasAlreadyLoaded && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        return repairedCount;
    }

    private static int RepairNetworkPrefabAssets()
    {
        int repairedCount = 0;
        string[] prefabGuids = AssetDatabase.FindAssets(
            "t:Prefab",
            new[] { "Assets/DeadmansTales/Prefabs" }
        );

        foreach (string prefabGuid in prefabGuids)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                prefabPath
            );
            NetworkObject networkObject =
                prefab != null ? prefab.GetComponent<NetworkObject>() : null;

            if (networkObject == null)
            {
                continue;
            }

            uint previousHash = networkObject.PrefabIdHash;
            NetworkObjectOnValidate.Invoke(networkObject, null);

            if (networkObject.PrefabIdHash == 0)
            {
                throw new InvalidOperationException(
                    $"Network prefab '{prefabPath}' still has a zero " +
                    "GlobalObjectIdHash after validation."
                );
            }

            // Persist the imported value even when OnValidate ran before this
            // method and the in-memory hash therefore did not change here.
            EditorUtility.SetDirty(networkObject);
            PrefabUtility.SavePrefabAsset(prefab);

            if (previousHash == 0 || networkObject.PrefabIdHash != previousHash)
            {
                repairedCount++;
            }
        }

        return repairedCount;
    }

    private static void ValidateSceneIdentities(
        Scene scene,
        IReadOnlyCollection<NetworkObject> networkObjects
    )
    {
        NetworkObject zeroHashObject = networkObjects.FirstOrDefault(
            networkObject => networkObject.PrefabIdHash == 0
        );

        if (zeroHashObject != null)
        {
            throw new InvalidOperationException(
                $"Scene '{scene.path}' still contains NetworkObject " +
                $"'{zeroHashObject.name}' with a zero GlobalObjectIdHash."
            );
        }

        IGrouping<uint, NetworkObject> duplicate = networkObjects
            .GroupBy(networkObject => networkObject.PrefabIdHash)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate == null)
        {
            return;
        }

        string names = string.Join(
            ", ",
            duplicate.Select(networkObject => networkObject.name)
        );

        throw new InvalidOperationException(
            $"Scene '{scene.path}' contains duplicate NGO " +
            $"GlobalObjectIdHash {duplicate.Key}: {names}."
        );
    }
}
