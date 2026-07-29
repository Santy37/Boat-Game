using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Local-play helper. Starts a local (loopback) host as soon as this scene
/// plays, so a working player spawns at the normal PlayerSpawnPoint2D spots —
/// no lobby, no online connection. A local host is used because the player's
/// movement and the cannon/helm interactions all rely on Netcode ownership.
///
/// Launching a scene directly leaves <see cref="GameMode.Current"/> at its
/// default (Local), which would make this bail before starting a host (so
/// nothing that needs a server - networked spawners, cannons, helm - works).
/// With <see cref="forceMultiplayer"/> on, this flips the session to
/// Multiplayer first, the same trick KrakenArenaTestPlayer uses for standalone
/// testing. Untick it for a genuine couch co-op scene so no host is started.
/// </summary>
public class AutoStartHost : MonoBehaviour
{
    [Tooltip(
        "Standalone testing: force Multiplayer so this actually starts a host " +
        "even when the scene is launched directly (GameMode defaults to " +
        "Local). Untick for a real couch co-op scene so no host is started.")]
    [SerializeField] private bool forceMultiplayer = true;

    private void Awake()
    {
        // Flip to the networked path BEFORE Start runs and checks the mode, so
        // a directly-launched scene actually starts a host instead of bailing.
        if (forceMultiplayer)
        {
            GameMode.SetMultiplayer();
        }
    }

    private IEnumerator Start()
    {
        // Local couch co-op never starts a host.
        if (GameMode.IsLocal)
        {
            yield break;
        }

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
