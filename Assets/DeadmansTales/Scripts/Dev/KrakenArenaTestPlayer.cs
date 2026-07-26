using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// TESTING ONLY -- a solo playtest harness for the kraken arena experiment.
///
/// The arena is opened by hitting Play on the scene directly, with no main menu
/// to choose a mode. Two things then conspire to spawn no controllable player:
///   1. <see cref="GameMode.Current"/> defaults to Local, so the scene's
///      <c>AutoStartHost</c> bails out and never starts a host; and
///   2. the arena isn't registered in NetworkPlayerSpawnCoordinator's gameplay-
///      scene list, so even a running host would never position a player.
///
/// This helper forces the networked path for a single local tester: it flips the
/// mode to Multiplayer, starts a loopback host so NGO creates a PlayerObject with
/// proper ownership (movement, cannons and the helm all need it), then teleports
/// that player onto a spawn marker. Strictly a dev aid -- it lives on the
/// experiment branch and should never reach a real build.
/// </summary>
public class KrakenArenaTestPlayer : MonoBehaviour
{
    [Tooltip("How long to wait (seconds) for the NetworkManager / PlayerObject "
        + "before giving up.")]
    [SerializeField] private float startTimeout = 12f;

    private void Awake()
    {
        // Flip to the networked path BEFORE AutoStartHost's Start runs, so every
        // scene script (players, cannons, helm) uses networked ownership.
        GameMode.SetMultiplayer();
    }

    private IEnumerator Start()
    {
        NetworkManager nm = null;
        float t = 0f;
        while ((nm = NetworkManager.Singleton) == null && t < startTimeout)
        {
            t += Time.deltaTime;
            yield return null;
        }
        if (nm == null)
        {
            Debug.LogError(
                "[ArenaTest] No NetworkManager appeared; is the network "
                + "bootstrap in the scene?", this);
            yield break;
        }

        // Start a loopback host if AutoStartHost hasn't already.
        if (!nm.IsListening)
        {
            nm.StartHost();
        }

        // Wait for NGO to auto-create this client's PlayerObject.
        t = 0f;
        while ((nm.LocalClient == null || nm.LocalClient.PlayerObject == null)
            && t < startTimeout)
        {
            t += Time.deltaTime;
            yield return null;
        }

        NetworkObject playerObject = nm.LocalClient?.PlayerObject;
        if (playerObject == null)
        {
            Debug.LogError(
                "[ArenaTest] Host started but no PlayerObject was created. Check "
                + "the NetworkManager's Player Prefab.", this);
            yield break;
        }

        // Teleport onto a spawn marker (or fall back to this object's position).
        Vector2 target = transform.position;
        PlayerSpawnPoint2D[] markers =
            FindObjectsByType<PlayerSpawnPoint2D>(FindObjectsSortMode.None);
        if (markers.Length > 0)
        {
            target = markers[0].transform.position;
        }

        TopDownNetworkPlayer2D player =
            playerObject.GetComponent<TopDownNetworkPlayer2D>();
        if (player == null)
        {
            Debug.LogWarning(
                "[ArenaTest] PlayerObject has no TopDownNetworkPlayer2D.", this);
            yield break;
        }

        // NGO auto-creates the PlayerObject at the prefab ORIGIN (0,0), which
        // sits inside the ship's hull collider. Before we position it, physics
        // ejects it and the transform interpolates up from the origin -- the
        // "clips through and sprints up on spawn" the crew sees. A single
        // teleport lands after that has already begun, so re-assert the spawn
        // every frame for a moment: TeleportToSpawnServer snaps the position and
        // zeroes velocity, so re-calling it overwrites any ejection/interpolation
        // until the body settles cleanly on the deck.
        // Start recording BEFORE the teleport so the whole spawn sequence --
        // pin, release, and whatever moves the player afterwards -- is captured
        // (see ArenaSpawnDiagnostic; it writes Logs/arena_diag.txt).
        ArenaSpawnDiagnostic diag = GetComponent<ArenaSpawnDiagnostic>();
        if (diag == null)
        {
            diag = gameObject.AddComponent<ArenaSpawnDiagnostic>();
        }
        StartCoroutine(diag.Report(player));

        bool ok = player.TeleportToSpawnServer(target);
        float pinUntil = Time.time + 0.6f;
        while (Time.time < pinUntil)
        {
            player.TeleportToSpawnServer(target);
            yield return null;
        }

        if (ok)
        {
            Debug.Log(
                $"[ArenaTest] Solo test player ready at {target}. Take the helm "
                + "or a cannon.", this);
        }
        else
        {
            Debug.LogWarning(
                "[ArenaTest] PlayerObject spawned but could not be teleported to "
                + "a spawn marker; it may be at the prefab origin.", this);
        }
    }
}
