using Unity.Netcode;
using UnityEngine;

public class SimpleNetworkStartUI : MonoBehaviour
{
    [Header("Legacy Debug Overlay")]
    [Tooltip(
        "Disabled by default because the MainMenu now owns host, join, and " +
        "player-count UI. Enable only for isolated networking debugging."
    )]
    [SerializeField]
    private bool showDebugOverlay;

    private void OnGUI()
    {
        if (!showDebugOverlay || NetworkManager.Singleton == null)
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
            if (GUILayout.Button("Start Host"))
            {
                NetworkManager.Singleton.StartHost();
            }

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
}
