using DeadmansTales.Configuration;
using DeadmansTales.Networking;
using DeadmansTales.Telemetry;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class TechnicalRuntimeTestSceneBuilder
{
    private const string MenuPath =
        "Deadman's Tales/Create Technical Runtime Test Scene";

    private const string TestSceneFolder =
        "Assets/DeadmansTales/Scenes/Tests";

    private const string TestScenePath =
        TestSceneFolder + "/Technical_Runtime_Test.unity";

    [MenuItem(MenuPath)]
    public static void CreateScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning(
                "Exit Play Mode before creating the technical runtime test scene."
            );
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        EnsureTestSceneFolder();

        Scene scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Single
        );

        CreateNetworkManager();
        CreatePersistentNetworkServices();
        CreateStageSeedProvider();
        CreatePlaytestLogger();
        GameObject driver = CreateRuntimeTestDriver();

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene, TestScenePath);

        if (!saved)
        {
            Debug.LogError(
                $"Failed to save the technical runtime test scene at " +
                $"'{TestScenePath}'."
            );
            return;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeGameObject = driver;

        Debug.Log(
            "[Technical Runtime Test] Scene created successfully.\n" +
            $"Path: {TestScenePath}\n" +
            "Press Play. The scene will start a local host and run the " +
            "technical runtime checks automatically."
        );
    }

    private static void EnsureTestSceneFolder()
    {
        const string scenesFolder = "Assets/DeadmansTales/Scenes";

        if (!AssetDatabase.IsValidFolder(scenesFolder))
        {
            AssetDatabase.CreateFolder(
                "Assets/DeadmansTales",
                "Scenes"
            );
        }

        if (!AssetDatabase.IsValidFolder(TestSceneFolder))
        {
            AssetDatabase.CreateFolder(
                scenesFolder,
                "Tests"
            );
        }
    }

    private static void CreateNetworkManager()
    {
        GameObject managerObject = new GameObject(
            "Technical NetworkManager"
        );

        NetworkManager networkManager =
            managerObject.AddComponent<NetworkManager>();

        UnityTransport transport =
            managerObject.AddComponent<UnityTransport>();

        networkManager.NetworkConfig.NetworkTransport = transport;
        networkManager.NetworkConfig.EnableSceneManagement = true;
    }

    private static void CreatePersistentNetworkServices()
    {
        GameObject services = new GameObject(
            "Persistent Network Services"
        );

        services.AddComponent<NetworkObject>();

        NetworkRunState runState =
            services.AddComponent<NetworkRunState>();

        NetworkRunConfigAuthority configAuthority =
            services.AddComponent<NetworkRunConfigAuthority>();

        // Keep the generated test object in this scene while the host starts.
        // The production integration will use a dedicated bootstrap strategy.
        SetSerializedBool(runState, "persistAcrossScenes", false);
        SetSerializedBool(configAuthority, "persistAcrossScenes", false);
    }

    private static void CreateStageSeedProvider()
    {
        GameObject seedProvider = new GameObject(
            "Stage Seed Provider"
        );

        seedProvider.AddComponent<StageSeedProvider>();
    }

    private static void CreatePlaytestLogger()
    {
        GameObject logger = new GameObject(
            "Playtest Event Logger"
        );

        logger.AddComponent<PlaytestEventLogger>();
    }

    private static GameObject CreateRuntimeTestDriver()
    {
        GameObject driver = new GameObject(
            "Technical Runtime Test Driver"
        );

        driver.AddComponent<TechnicalRuntimeTestDriver>();
        return driver;
    }

    private static void SetSerializedBool(
        Object target,
        string propertyName,
        bool value
    )
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property =
            serializedObject.FindProperty(propertyName);

        if (property == null)
        {
            Debug.LogWarning(
                $"Could not find serialized property '{propertyName}' on " +
                $"{target.GetType().Name}."
            );
            return;
        }

        property.boolValue = value;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }
}
