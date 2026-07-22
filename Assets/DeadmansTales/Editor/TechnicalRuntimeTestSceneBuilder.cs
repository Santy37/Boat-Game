using System;
using System.IO;
using DeadmansTales.Networking;
using DeadmansTales.Telemetry;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Build.Reporting;
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
        CreateStageSeedProvider();
        CreatePlaytestLogger();
        CreatePlayerSpawnPoint();
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

    public static void BuildIslandRuntimeSmokePlayerFromCommandLine()
    {
        // Recreate the harness so it can never retain an old scene-placed
        // persistent NetworkObject. The normal bootstrap now spawns the
        // registered run-state/config prefab at runtime.
        CreateScene();

        string outputDirectory = Path.GetFullPath(
            Path.Combine("Logs", "IslandRuntimeSmoke")
        );
        Directory.CreateDirectory(outputDirectory);

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[]
            {
                TestScenePath,
                "Assets/DeadmansTales/Scenes/Island/Lobby_Island_2D.unity",
                "Assets/DeadmansTales/Scenes/Boat/Boat_Gameplay_2D.unity",
                "Assets/DeadmansTales/Scenes/Island_After_Ocean_01_2D.unity",
            },
            locationPathName = Path.Combine(
                outputDirectory,
                "IslandRuntimeSmoke.exe"
            ),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new InvalidOperationException(
                "Island runtime smoke player build failed: " +
                report.summary.result
            );
        }

        Debug.Log(
            "[Technical Runtime Test] Standalone island smoke player built " +
            $"at {options.locationPathName}."
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

    private static void CreatePlayerSpawnPoint()
    {
        GameObject spawnPoint = new GameObject(
            "Technical Player Spawn Point"
        );

        spawnPoint.transform.position = new Vector3(2f, 12f, 0f);
        spawnPoint.AddComponent<PlayerSpawnPoint2D>();
    }

    private static GameObject CreateRuntimeTestDriver()
    {
        GameObject driver = new GameObject(
            "Technical Runtime Test Driver"
        );

        driver.AddComponent<TechnicalRuntimeTestDriver>();
        return driver;
    }

}
