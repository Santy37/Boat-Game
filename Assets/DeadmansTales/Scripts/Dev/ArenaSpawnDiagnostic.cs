using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// TESTING ONLY. Records what happens to the arena's test player after spawn
/// (file: Logs/arena_diag.txt + console) and, when the player is climbing with
/// zero velocity/input/contacts (a direct transform write), BISECTS the whole
/// running game to name the driver:
///
///   phase 1 -- disable every Behaviour ON the player, one at a time
///              (enumerated live, so even a runtime-attached stray is caught);
///   phase 2 -- deactivate every root GameObject in the scene, one at a time;
///   phase 3 -- deactivate the DontDestroyOnLoad roots (except the
///              NetworkManager, which would despawn the player).
///
/// The first switch-off that stops the climb prints "CULPRIT: ...".
/// </summary>
public class ArenaSpawnDiagnostic : MonoBehaviour
{
    [SerializeField] private float sampleInterval = 0.2f;

    private const string LogPath = "Logs/arena_diag.txt";
    private const float BaselineSeconds = 1.2f;
    private const float StepSeconds = 0.5f;
    private const float ClimbThreshold = 2f;

    private static readonly ContactPoint2D[] ContactBuffer =
        new ContactPoint2D[8];

    public IEnumerator Report(TopDownNetworkPlayer2D player)
    {
        if (player == null)
        {
            yield break;
        }

        FieldInfo smiField = typeof(TopDownNetworkPlayer2D).GetField(
            "serverMoveInput",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        Transform t = player.transform;

        try
        {
            Directory.CreateDirectory("Logs");
        }
        catch
        {
        }

        Log($"--- run start {System.DateTime.Now:HH:mm:ss} pos={F(t.position)} ---");

        // Baseline.
        Vector3 previous = t.position;
        float elapsed = 0f;
        float lastDpsY = 0f;
        while (elapsed < BaselineSeconds)
        {
            yield return new WaitForSeconds(sampleInterval);
            elapsed += sampleInterval;
            Vector3 now = t.position;
            Vector3 dps = (now - previous) / sampleInterval;
            previous = now;
            lastDpsY = dps.y;
            LogSample(elapsed, t, rb, dps, smiField, player);
        }

        if (Mathf.Abs(lastDpsY) < ClimbThreshold)
        {
            Log("No climb detected in baseline; nothing to bisect.");
            Log("--- run end ---");
            yield break;
        }

        Log($"CLIMB CONFIRMED at {lastDpsY:F1} u/s -- bisecting...");

        // Phase 1: every Behaviour on the player root, live-enumerated.
        foreach (Behaviour b in player.GetComponents<Behaviour>())
        {
            if (b == null || !b.enabled)
            {
                continue;
            }
            string stepName = $"player:{b.GetType().Name}";
            b.enabled = false;

            bool stopped = false;
            yield return MeasureStep(stepName, t, v => stopped = v);
            if (stopped)
            {
                yield break;
            }
        }
        if (rb != null)
        {
            rb.simulated = false;
            bool stopped = false;
            yield return MeasureStep("player:Rigidbody2D.simulated=false", t,
                v => stopped = v);
            if (stopped)
            {
                yield break;
            }
        }

        // Phase 2 + 3: roots of the active scene, then DontDestroyOnLoad.
        List<GameObject> roots = new List<GameObject>();
        roots.AddRange(SceneManager.GetActiveScene().GetRootGameObjects());
        GameObject ddolProbe = new GameObject("__ddolProbe");
        DontDestroyOnLoad(ddolProbe);
        roots.AddRange(ddolProbe.scene.GetRootGameObjects());

        foreach (GameObject root in roots)
        {
            if (root == null || !root.activeSelf)
            {
                continue;
            }
            if (root == gameObject || root == ddolProbe
                || root.transform == t
                || root.GetComponentInChildren<Unity.Netcode.NetworkManager>()
                    != null)
            {
                continue; // self, the player, or the network stack.
            }

            string stepName = $"root:{root.name}";
            root.SetActive(false);

            if (t == null)
            {
                Log($"disabled {stepName} -> PLAYER DESPAWNED; it was "
                    + "keeping the player alive, not moving it.");
                break;
            }

            bool stopped = false;
            yield return MeasureStep(stepName, t, v => stopped = v);
            if (stopped)
            {
                yield break;
            }
        }

        Log("CULPRIT: NOT FOUND -- climb survived every switch-off.");
        Log("--- run end ---");
    }

    private IEnumerator MeasureStep(
        string stepName, Transform t, System.Action<bool> setStopped)
    {
        Vector3 before = t != null ? t.position : Vector3.zero;
        yield return new WaitForSeconds(StepSeconds);
        if (t == null)
        {
            Log($"disabled {stepName} -> player transform GONE.");
            setStopped(true);
            yield break;
        }
        float dpsY = (t.position.y - before.y) / StepSeconds;
        Log($"disabled {stepName} -> dps.y={dpsY:F2}");
        if (Mathf.Abs(dpsY) < ClimbThreshold)
        {
            Log($"CULPRIT: {stepName} (climb stopped after disabling it)");
            Log("--- run end ---");
            setStopped(true);
        }
    }

    private void LogSample(
        float elapsed, Transform t, Rigidbody2D rb, Vector3 dps,
        FieldInfo smiField, TopDownNetworkPlayer2D player)
    {
        object smi = smiField?.GetValue(player);
        StringBuilder sb = new StringBuilder();
        sb.Append($"t={elapsed:F1} pos={F(t.position)} dps={F(dps)} ");
        sb.Append($"vel={F(rb != null ? rb.linearVelocity : Vector2.zero)} ");
        sb.Append($"rbPos={(rb != null ? F(rb.position) : "?")} ");
        sb.Append($"smi={smi} ");
        sb.Append($"parent={(t.parent != null ? t.parent.name : "none")}");

        int contactCount = rb != null ? rb.GetContacts(ContactBuffer) : 0;
        sb.Append($" contacts={contactCount}");
        Log(sb.ToString());
    }

    private static string F(Vector3 v)
    {
        return $"({v.x:F2},{v.y:F2})";
    }

    private static string F(Vector2 v)
    {
        return $"({v.x:F2},{v.y:F2})";
    }

    private void Log(string line)
    {
        Debug.Log("[ArenaDiag] " + line);
        try
        {
            File.AppendAllText(LogPath, line + "\n");
        }
        catch
        {
        }
    }
}
