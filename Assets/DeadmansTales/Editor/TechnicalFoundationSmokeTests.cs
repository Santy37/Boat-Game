using System;
using DeadmansTales.Configuration;
using DeadmansTales.Networking;
using UnityEditor;
using UnityEngine;

internal static class TechnicalFoundationSmokeTests
{
    private const string MenuPath =
        "Deadman's Tales/Run Technical Smoke Tests";

    [MenuItem(MenuPath)]
    public static void RunAll()
    {
        int passed = 0;
        int failed = 0;

        RunCase(
            "SeedUtility is deterministic",
            TestSeedUtilityDeterminism,
            ref passed,
            ref failed
        );

        RunCase(
            "Stage streams are isolated",
            TestStageStreamIsolation,
            ref passed,
            ref failed
        );

        RunCase(
            "BoatRunConfig validation clamps invalid values",
            TestConfigValidation,
            ref passed,
            ref failed
        );

        RunCase(
            "Network config round-trip preserves values",
            TestNetworkConfigRoundTrip,
            ref passed,
            ref failed
        );

        string summary =
            $"[Technical Smoke Tests] Passed: {passed}, Failed: {failed}";

        if (failed > 0)
        {
            Debug.LogError(summary);
            throw new InvalidOperationException(summary);
        }

        Debug.Log(summary + "\nAll technical foundation checks passed.");
    }

    private static void TestSeedUtilityDeterminism()
    {
        const int masterSeed = 12345;
        const string streamName = "EnemySpawns";

        int firstSeed = SeedUtility.DeriveSeed(masterSeed, streamName);
        int secondSeed = SeedUtility.DeriveSeed(masterSeed, streamName);

        Assert(
            firstSeed == secondSeed,
            "The same master seed and stream name produced different seeds."
        );

        System.Random firstRandom =
            SeedUtility.CreateRandom(masterSeed, streamName);
        System.Random secondRandom =
            SeedUtility.CreateRandom(masterSeed, streamName);

        for (int i = 0; i < 16; i++)
        {
            Assert(
                firstRandom.Next() == secondRandom.Next(),
                $"The deterministic random sequence diverged at value {i}."
            );
        }
    }

    private static void TestStageStreamIsolation()
    {
        StageSeedContext stageOne = new StageSeedContext(
            12345,
            1,
            "boat_default",
            1
        );

        StageSeedContext stageTwo = new StageSeedContext(
            12345,
            2,
            "boat_default",
            1
        );

        int stageOneEnemySeed = stageOne.DeriveSeed("EnemySpawns");
        int stageOneLootSeed = stageOne.DeriveSeed("Loot");
        int stageTwoEnemySeed = stageTwo.DeriveSeed("EnemySpawns");

        Assert(
            stageOneEnemySeed != stageOneLootSeed,
            "Enemy and loot streams unexpectedly share one derived seed."
        );

        Assert(
            stageOneEnemySeed != stageTwoEnemySeed,
            "Stage one and stage two unexpectedly share one enemy seed."
        );

        Assert(
            stageOne.BuildStreamName(" EnemySpawns ") ==
            "Stage_1_EnemySpawns",
            "Stage stream names were not normalized as expected."
        );
    }

    private static void TestConfigValidation()
    {
        BoatRunConfig config = new BoatRunConfig
        {
            configVersion = 0,
            id = " ",
            minimumObstacleCount = -5,
            maximumObstacleCount = -10,
            minimumObstacleSpacing = -2f,
            maximumPlacementAttemptsPerObstacle = 0,
            enemySpawnChance = 2f,
            lootSpawnChance = -1f
        };

        config.Validate();

        Assert(config.configVersion == 1, "Config version was not clamped.");
        Assert(config.id == "boat_default", "Blank config ID was not replaced.");
        Assert(config.minimumObstacleCount == 0, "Minimum count was not clamped.");
        Assert(
            config.maximumObstacleCount == config.minimumObstacleCount,
            "Maximum count was left below the minimum count."
        );
        Assert(config.minimumObstacleSpacing == 0f, "Spacing was not clamped.");
        Assert(
            config.maximumPlacementAttemptsPerObstacle == 1,
            "Placement attempts were not clamped."
        );
        Assert(config.enemySpawnChance == 1f, "Enemy chance was not clamped.");
        Assert(config.lootSpawnChance == 0f, "Loot chance was not clamped.");
    }

    private static void TestNetworkConfigRoundTrip()
    {
        BoatRunConfig original = new BoatRunConfig
        {
            configVersion = 7,
            id = "smoke_test",
            minimumObstacleCount = 2,
            maximumObstacleCount = 9,
            minimumObstacleSpacing = 3.5f,
            maximumPlacementAttemptsPerObstacle = 75,
            enemySpawnChance = 0.45f,
            lootSpawnChance = 0.3f
        };

        original.Validate();

        NetworkBoatRunConfig snapshot =
            NetworkBoatRunConfig.FromRuntimeConfig(original);

        BoatRunConfig roundTrip = snapshot.ToRuntimeConfig();
        NetworkBoatRunConfig rebuilt =
            NetworkBoatRunConfig.FromRuntimeConfig(roundTrip);

        Assert(snapshot.IsValid, "The synchronized snapshot is invalid.");
        Assert(
            snapshot.Equals(rebuilt),
            "Converting the synchronized config to runtime and back changed values."
        );
    }

    private static void RunCase(
        string name,
        Action test,
        ref int passed,
        ref int failed
    )
    {
        try
        {
            test();
            passed++;
            Debug.Log($"[Technical Smoke Tests] PASS: {name}");
        }
        catch (Exception exception)
        {
            failed++;
            Debug.LogError(
                $"[Technical Smoke Tests] FAIL: {name}\n{exception}"
            );
        }
    }

    private static void Assert(bool condition, string failureMessage)
    {
        if (!condition)
        {
            throw new InvalidOperationException(failureMessage);
        }
    }
}
