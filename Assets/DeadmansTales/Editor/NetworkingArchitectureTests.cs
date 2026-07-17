using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal sealed class NetworkingArchitectureTests
{
    private const string NetworkManagerPrefabPath =
        "Assets/DeadmansTales/Prefabs/Networking/NetworkManager.prefab";
    private const string MainMenuScenePath =
        "Assets/DeadmansTales/Scenes/MainMenu.unity";
    private const string LobbyScenePath =
        "Assets/DeadmansTales/Scenes/Lobby_Island_2D.unity";
    private const string BoatScenePath =
        "Assets/DeadmansTales/Scenes/Boat_Gameplay_2D.unity";

    [Test]
    public void NetworkPrefabsUseServerAuthoritativeClientServerSetup()
    {
        GameObject managerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            NetworkManagerPrefabPath
        );

        Assert.That(managerPrefab, Is.Not.Null);

        NetworkManager manager =
            managerPrefab.GetComponent<NetworkManager>();

        Assert.That(manager, Is.Not.Null);
        Assert.That(
            manager.NetworkConfig.NetworkTopology,
            Is.EqualTo(NetworkTopologyTypes.ClientServer)
        );
        Assert.That(manager.NetworkConfig.EnableSceneManagement, Is.True);
        Assert.That(manager.NetworkConfig.PlayerPrefab, Is.Not.Null);

        GameObject playerPrefab = manager.NetworkConfig.PlayerPrefab;

        Assert.That(
            playerPrefab.GetComponent<NetworkObject>(),
            Is.Not.Null
        );
        Assert.That(
            playerPrefab.GetComponent<TopDownNetworkPlayer2D>(),
            Is.Not.Null
        );

        NetworkTransform networkTransform =
            playerPrefab.GetComponent<NetworkTransform>();

        Assert.That(networkTransform, Is.Not.Null);
        Assert.That(
            networkTransform.AuthorityMode,
            Is.EqualTo(NetworkTransform.AuthorityModes.Server)
        );

        NetworkRigidbody2D networkRigidbody =
            playerPrefab.GetComponent<NetworkRigidbody2D>();

        Assert.That(networkRigidbody, Is.Not.Null);
        Assert.That(networkRigidbody.UseRigidBodyForMotion, Is.True);
        Assert.That(networkRigidbody.AutoUpdateKinematicState, Is.True);
    }

    [Test]
    public void OnlyBootstrapSceneContainsNetworkManager()
    {
        Assert.That(CountSceneComponents<NetworkManager>(MainMenuScenePath),
            Is.EqualTo(1));
        Assert.That(CountSceneComponents<NetworkManager>(LobbyScenePath),
            Is.Zero);
        Assert.That(CountSceneComponents<NetworkManager>(BoatScenePath),
            Is.Zero);
    }

    [TestCase(LobbyScenePath)]
    [TestCase(BoatScenePath)]
    public void GameplaySceneHasFourExplicitSpawnMarkers(string scenePath)
    {
        IReadOnlyList<string> markerNames = ReadSceneComponents<
            PlayerSpawnPoint2D,
            IReadOnlyList<string>
        >(
            scenePath,
            markers => markers.Select(marker => marker.name).ToArray()
        );

        Assert.That(markerNames.Count, Is.EqualTo(4));
        Assert.That(
            markerNames.OrderBy(name => name),
            Is.EqualTo(new[]
            {
                "PlayerSpawn_0",
                "PlayerSpawn_1",
                "PlayerSpawn_2",
                "PlayerSpawn_3",
            })
        );
    }

    private static int CountSceneComponents<T>(string scenePath)
        where T : Component
    {
        return ReadSceneComponents<T, int>(
            scenePath,
            components => components.Count()
        );
    }

    private static TResult ReadSceneComponents<T, TResult>(
        string scenePath,
        Func<IEnumerable<T>, TResult> read
    )
        where T : Component
    {
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool wasAlreadyLoaded = scene.isLoaded;

        if (!wasAlreadyLoaded)
        {
            scene = EditorSceneManager.OpenScene(
                scenePath,
                OpenSceneMode.Additive
            );
        }

        try
        {
            IEnumerable<T> components = scene
                .GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true));

            return read(components);
        }
        finally
        {
            if (!wasAlreadyLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
