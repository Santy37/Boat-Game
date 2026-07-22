using System.Collections;
using DeadmansTales.Configuration;
using DeadmansTales.Networking;
using DeadmansTales.Telemetry;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Isolated runtime verification for the technical multiplayer foundation.
///
/// This component is only placed in the generated technical test scene. It
/// starts a local host, waits for the persistent network services, initializes
/// a deterministic run, and verifies the synchronized run/config/seed state.
/// It does not depend on the game UI, player mechanics, islands, or combat.
/// </summary>
public sealed class TechnicalRuntimeTestDriver : MonoBehaviour
{
    [SerializeField]
    private int testSeed = 24680;

    [SerializeField]
    [Min(1f)]
    private float timeoutSeconds = 10f;

    private string currentStatus = "Waiting to enter Play Mode...";
    private bool testFinished;
    private bool testPassed;

    private IEnumerator Start()
    {
        currentStatus = "Starting local technical host...";
        yield return null;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            Fail("No NetworkManager exists in the technical test scene.");
            yield break;
        }

        if (!networkManager.IsListening && !networkManager.StartHost())
        {
            Fail("NetworkManager.StartHost returned false.");
            yield break;
        }

        currentStatus = "Waiting for network services to spawn...";

        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (
            Time.realtimeSinceStartup < deadline &&
            !AreNetworkServicesReady()
        )
        {
            yield return null;
        }

        if (!AreNetworkServicesReady())
        {
            Fail(
                "Timed out waiting for NetworkRunState and " +
                "NetworkRunConfigAuthority."
            );
            yield break;
        }

        NetworkRunState runState = NetworkRunState.Instance;
        NetworkRunConfigAuthority configAuthority =
            NetworkRunConfigAuthority.Instance;

        runState.InitializeNewRunServer(
            testSeed,
            configAuthority.ConfigId,
            configAuthority.ConfigVersion,
            1
        );

        StageSeedProvider seedProvider =
            FindFirstObjectByType<StageSeedProvider>();

        if (seedProvider == null)
        {
            Fail("No StageSeedProvider exists in the technical test scene.");
            yield break;
        }

        currentStatus = "Waiting for stage seed context...";
        deadline = Time.realtimeSinceStartup + timeoutSeconds;

        while (
            Time.realtimeSinceStartup < deadline &&
            !seedProvider.IsReady
        )
        {
            yield return null;
        }

        if (!seedProvider.TryGetContext(out StageSeedContext context))
        {
            Fail("StageSeedProvider never produced a valid context.");
            yield break;
        }

        if (runState.MasterSeed.Value != testSeed)
        {
            Fail("The synchronized master seed does not match the test seed.");
            yield break;
        }

        if (runState.CurrentStage.Value != 1)
        {
            Fail("The synchronized stage index was not initialized to one.");
            yield break;
        }

        if (runState.Status.Value != NetworkRunStatus.Loading)
        {
            Fail("The synchronized run status was not set to Loading.");
            yield break;
        }

        if (context.MasterSeed != testSeed || context.StageIndex != 1)
        {
            Fail("The stage seed context does not match the run state.");
            yield break;
        }

        if (context.ConfigId != configAuthority.ConfigId)
        {
            Fail("The stage context config ID does not match the host config.");
            yield break;
        }

        PlaytestEventLogger logger = PlaytestEventLogger.Instance;
        if (logger == null || !logger.IsSessionOpen)
        {
            Fail("The playtest logger did not open a telemetry session.");
            yield break;
        }

        int enemySeed = seedProvider.DeriveSeed("EnemySpawns");
        int lootSeed = seedProvider.DeriveSeed("Loot");
        if (enemySeed == lootSeed)
        {
            Fail("Enemy and loot runtime streams unexpectedly share a seed.");
            yield break;
        }

        Pass(
            "Local host, synchronized config, persistent run state, " +
            "stage seed context, and telemetry logger are all ready."
        );
    }

    private static bool AreNetworkServicesReady()
    {
        return
            NetworkRunState.Instance != null &&
            NetworkRunState.Instance.IsSpawned &&
            NetworkRunState.Instance.IsServer &&
            NetworkRunConfigAuthority.Instance != null &&
            NetworkRunConfigAuthority.Instance.IsSpawned &&
            NetworkRunConfigAuthority.Instance.IsServer &&
            NetworkRunConfigAuthority.Instance.IsConfigReady;
    }

    private void Pass(string message)
    {
        testFinished = true;
        testPassed = true;
        currentStatus = "PASS: " + message;

        Debug.Log(
            "[Technical Runtime Test] PASS\n" + message,
            this
        );
    }

    private void Fail(string message)
    {
        testFinished = true;
        testPassed = false;
        currentStatus = "FAIL: " + message;

        Debug.LogError(
            "[Technical Runtime Test] FAIL\n" + message,
            this
        );
    }

    private void OnGUI()
    {
        const float width = 640f;
        const float height = 150f;

        Rect panel = new Rect(
            20f,
            20f,
            width,
            height
        );

        GUI.Box(panel, "Technical Network Runtime Test");
        GUI.Label(
            new Rect(40f, 55f, width - 40f, 55f),
            currentStatus
        );

        if (
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening &&
            GUI.Button(
                new Rect(40f, 115f, 180f, 30f),
                "Shutdown Test Host"
            )
        )
        {
            NetworkManager.Singleton.Shutdown();
            currentStatus = testFinished
                ? (testPassed ? "Test passed. Host stopped." : "Test failed. Host stopped.")
                : "Host stopped before the test finished.";
        }
    }
}
