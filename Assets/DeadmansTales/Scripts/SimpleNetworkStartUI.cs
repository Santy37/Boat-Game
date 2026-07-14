using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SimpleNetworkStartUI : MonoBehaviour
{
    private bool hostStartRequested;

    private void OnGUI()
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        GUILayout.BeginArea(new Rect(10, 10, 260, 180));
        GUILayout.Label("Dead Man's Tale Alpha");

        bool networkNotStarted =
            !NetworkManager.Singleton.IsClient &&
            !NetworkManager.Singleton.IsServer;

        if (networkNotStarted)
        {
            GUI.enabled = !hostStartRequested;

            if (GUILayout.Button("Start Host"))
            {
                StartHostAndSynchronizeScene();
            }

            GUI.enabled = true;

            if (GUILayout.Button("Start Client"))
            {
                NetworkManager.Singleton.StartClient();
            }

            if (GUILayout.Button("Start Server"))
            {
                NetworkManager.Singleton.StartServer();
            }
        }
        else
        {
            string mode = "Client";

            if (NetworkManager.Singleton.IsHost)
            {
                mode = "Host";
            }
            else if (NetworkManager.Singleton.IsServer)
            {
                mode = "Server";
            }

            GUILayout.Label("Mode: " + mode);
            GUILayout.Label(
                "Connected Players: " +
                NetworkManager.Singleton.ConnectedClientsIds.Count
            );
        }

        GUILayout.EndArea();
    }

    private void StartHostAndSynchronizeScene()
    {
        if (hostStartRequested)
        {
            return;
        }

        hostStartRequested = true;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (!networkManager.StartHost())
        {
            hostStartRequested = false;
            Debug.LogError("[Network Start] Failed to start the host.");
            return;
        }

        if (!networkManager.NetworkConfig.EnableSceneManagement)
        {
            Debug.LogWarning(
                "[Network Start] Host started, but NGO scene management is " +
                "disabled. Joining clients will not receive the active scene."
            );
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || string.IsNullOrWhiteSpace(activeScene.name))
        {
            Debug.LogError(
                "[Network Start] Host started, but the active scene could not " +
                "be registered for client synchronization."
            );
            return;
        }

        // The temporary alpha starts networking from inside the lobby itself.
        // Loading the already-open scene through NGO registers it as the
        // authoritative network scene, so late-joining clients receive the
        // tilemaps, camera, spawn points, and every other scene object.
        networkManager.SceneManager.LoadScene(
            activeScene.name,
            LoadSceneMode.Single
        );

        Debug.Log(
            $"[Network Start] Host started and registered scene " +
            $"'{activeScene.name}' for client synchronization."
        );
    }
}
