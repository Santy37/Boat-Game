using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// TESTING ONLY. Runs the kraken arena in PLAY MODE headlessly for ~14 seconds
/// so the in-scene KrakenArenaTestPlayer + ArenaSpawnDiagnostic can record what
/// actually happens to a spawned player (Logs/arena_diag.txt), then exits.
///
/// Usage (batch): -executeMethod ArenaPlaymodeProbe.RunFromCommandLine
/// WITHOUT -quit (play mode is asynchronous; this script calls
/// EditorApplication.Exit itself when done).
///
/// State lives in SessionState because entering/leaving play mode reloads the
/// script domain. The update hook is registered on every domain load but is a
/// no-op unless a probe run is active.
/// </summary>
public static class ArenaPlaymodeProbe
{
    private const string ActiveFlag = "ArenaProbe_Active";
    private const string PlayStartKey = "ArenaProbe_PlayStart";
    private const string LaunchKey = "ArenaProbe_Launch";
    private const string ArenaScenePath =
        "Assets/DeadmansTales/Scenes/Boat/Kraken_Arena_2D.unity";
    private const string DiagPath = "Logs/arena_diag.txt";
    private const string DoneMarkerPath = "Logs/arena_probe_done.txt";
    private const double PlaySeconds = 32.0;
    private const double StartTimeoutSeconds = 180.0;

    public static void RunFromCommandLine()
    {
        try
        {
            Directory.CreateDirectory("Logs");
            if (File.Exists(DiagPath))
            {
                File.Delete(DiagPath);
            }
            if (File.Exists(DoneMarkerPath))
            {
                File.Delete(DoneMarkerPath);
            }
        }
        catch
        {
        }

        EditorSceneManager.OpenScene(ArenaScenePath, OpenSceneMode.Single);

        SessionState.SetBool(ActiveFlag, true);
        SessionState.SetFloat(PlayStartKey, -1f);
        SessionState.SetFloat(
            LaunchKey, (float)EditorApplication.timeSinceStartup);

        Debug.Log("[ArenaProbe] Entering play mode...");
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    private static void Hook()
    {
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(ActiveFlag, false))
        {
            return;
        }

        if (EditorApplication.isPlaying)
        {
            float start = SessionState.GetFloat(PlayStartKey, -1f);
            if (start < 0f)
            {
                SessionState.SetFloat(
                    PlayStartKey, (float)EditorApplication.timeSinceStartup);
                Debug.Log("[ArenaProbe] Play mode entered; sampling...");
            }
            else if (EditorApplication.timeSinceStartup - start > PlaySeconds)
            {
                Debug.Log("[ArenaProbe] Sampling window over; exiting play.");
                EditorApplication.ExitPlaymode();
            }
            return;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return; // transitioning; wait.
        }

        float played = SessionState.GetFloat(PlayStartKey, -1f);
        if (played > 0f)
        {
            // Play mode has come and gone: the probe is complete.
            SessionState.SetBool(ActiveFlag, false);
            try
            {
                File.WriteAllText(
                    DoneMarkerPath, $"done {System.DateTime.Now:HH:mm:ss}");
            }
            catch
            {
            }
            Debug.Log("[ArenaProbe] Done; exiting editor.");
            EditorApplication.Exit(0);
            return;
        }

        // Never managed to enter play mode: bail out rather than hang forever.
        float launch = SessionState.GetFloat(LaunchKey, -1f);
        if (launch > 0f
            && EditorApplication.timeSinceStartup - launch
                > StartTimeoutSeconds)
        {
            SessionState.SetBool(ActiveFlag, false);
            Debug.LogError("[ArenaProbe] Play mode never started; aborting.");
            EditorApplication.Exit(2);
        }
    }
}
