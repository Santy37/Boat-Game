using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Local-play helper. Starts a local (loopback) host as soon as this scene
/// plays, so a working player spawns at the normal PlayerSpawnPoint2D spots —
/// no lobby, no online connection. A local host is used because the player's
/// movement and the cannon/helm interactions all rely on Netcode ownership.
/// </summary>
public class AutoStartHost : MonoBehaviour
{
    private IEnumerator Start()
    {
        // Wait for the NetworkManager that DeadmansNetworkBootstrap builds on load.
        while (NetworkManager.Singleton == null)
        {
            yield return null;
        }

        if (!NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.StartHost();
        }
    }
}
