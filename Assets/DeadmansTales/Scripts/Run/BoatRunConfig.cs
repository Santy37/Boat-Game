using System;
using UnityEngine;

/// <summary>
/// Represents the editable JSON configuration for one boat run.
///
/// This is NOT a MonoBehaviour.
/// Unity's JsonUtility creates this object from a JSON file.
/// </summary>
[Serializable]
public class BoatRunConfig
{
    [Header("Config Identity")]
    public int configVersion = 1;

    public string id = "boat_default";

    [Header("Obstacle Generation")]
    public int minimumObstacleCount = 3;

    public int maximumObstacleCount = 8;

    public float minimumObstacleSpacing = 2f;

    public int maximumPlacementAttemptsPerObstacle = 50;

    [Header("Future Gameplay Values")]
    [Range(0f, 1f)]
    public float enemySpawnChance = 0.25f;

    [Range(0f, 1f)]
    public float lootSpawnChance = 0.20f;

    /// <summary>
    /// Prevents broken JSON values from creating obviously invalid gameplay.
    /// </summary>
    public void Validate()
    {
        configVersion =
            Mathf.Max(
                1,
                configVersion
            );

        if (string.IsNullOrWhiteSpace(id))
        {
            id = "boat_default";
        }

        minimumObstacleCount =
            Mathf.Max(
                0,
                minimumObstacleCount
            );

        maximumObstacleCount =
            Mathf.Max(
                minimumObstacleCount,
                maximumObstacleCount
            );

        minimumObstacleSpacing =
            Mathf.Max(
                0f,
                minimumObstacleSpacing
            );

        maximumPlacementAttemptsPerObstacle =
            Mathf.Max(
                1,
                maximumPlacementAttemptsPerObstacle
            );

        enemySpawnChance =
            Mathf.Clamp01(
                enemySpawnChance
            );

        lootSpawnChance =
            Mathf.Clamp01(
                lootSpawnChance
            );
    }

    /// <summary>
    /// Creates a safe built-in fallback if the JSON file
    /// cannot be found or parsed.
    /// </summary>
    public static BoatRunConfig CreateFallback(
        string requestedId
    )
    {
        BoatRunConfig fallback =
            new BoatRunConfig();

        if (!string.IsNullOrWhiteSpace(requestedId))
        {
            fallback.id = requestedId;
        }

        fallback.Validate();

        return fallback;
    }
}