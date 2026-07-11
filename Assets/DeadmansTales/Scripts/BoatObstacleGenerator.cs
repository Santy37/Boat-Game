using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Example server-authoritative procedural obstacle generator.
///
/// The server:
/// 1. Waits for BoatRunDirector.
/// 2. Reads obstacle settings from the JSON config.
/// 3. Creates a deterministic random stream named "ShipObstacles".
/// 4. Chooses obstacle count, prefab choices, and positions.
/// 5. Spawns NetworkObjects so all clients receive them.
///
/// Teammates can replace the obstacle prefabs and generation rules
/// without changing the run-seed system.
/// </summary>
public class BoatObstacleGenerator : MonoBehaviour
{
    private const string RandomStreamName =
        "ShipObstacles";

    [Header("Generation Area")]
    [Tooltip(
        "Assign a BoxCollider2D that represents the area " +
        "where obstacles are allowed to spawn."
    )]
    [SerializeField]
    private BoxCollider2D generationArea;

    [Header("Obstacle Prefabs")]
    [Tooltip(
        "Every prefab in this array should have a NetworkObject " +
        "and should be registered as a Network Prefab."
    )]
    [SerializeField]
    private GameObject[] obstaclePrefabs;

    [Header("Debug")]
    [SerializeField]
    private bool logGeneratedObstacles =
        true;

    private readonly List<Vector2>
        generatedPositions =
            new List<Vector2>();

    private bool generationComplete;

    private IEnumerator Start()
    {
        // Wait until the scene's BoatRunDirector exists.
        while (
            BoatRunDirector.Instance == null
        )
        {
            yield return null;
        }

        // Wait until:
        // - the synchronized run seed is ready
        // - the JSON configuration is loaded
        while (
            !BoatRunDirector.Instance.IsRunReady
        )
        {
            yield return null;
        }

        BoatRunDirector runDirector =
            BoatRunDirector.Instance;

        // Interactive NetworkObjects should only be
        // instantiated and spawned by the server.
        if (!runDirector.IsServer)
        {
            yield break;
        }

        GenerateObstacles(
            runDirector
        );
    }

    private void GenerateObstacles(
        BoatRunDirector runDirector
    )
    {
        if (generationComplete)
        {
            return;
        }

        if (generationArea == null)
        {
            Debug.LogError(
                "[Obstacle Generator] No Generation Area assigned.",
                this
            );

            return;
        }

        if (
            obstaclePrefabs == null ||
            obstaclePrefabs.Length == 0
        )
        {
            Debug.LogWarning(
                "[Obstacle Generator] No obstacle prefabs assigned. " +
                "Generation will do nothing.",
                this
            );

            return;
        }

        BoatRunConfig config =
            runDirector.Config;

        System.Random rng =
            runDirector.CreateRandom(
                RandomStreamName
            );

        int obstacleCount =
            rng.Next(
                config.minimumObstacleCount,
                config.maximumObstacleCount + 1
            );

        generatedPositions.Clear();

        int successfullySpawned =
            0;

        for (
            int obstacleIndex = 0;
            obstacleIndex < obstacleCount;
            obstacleIndex++
        )
        {
            bool foundValidPosition =
                TryFindSpawnPosition(
                    rng,
                    config.minimumObstacleSpacing,
                    config.maximumPlacementAttemptsPerObstacle,
                    out Vector2 spawnPosition
                );

            if (!foundValidPosition)
            {
                Debug.LogWarning(
                    $"[Obstacle Generator] Could not find a valid " +
                    $"position for obstacle {obstacleIndex + 1}. " +
                    "Stopping generation early.",
                    this
                );

                break;
            }

            int prefabIndex =
                rng.Next(
                    0,
                    obstaclePrefabs.Length
                );

            GameObject selectedPrefab =
                obstaclePrefabs[prefabIndex];

            if (selectedPrefab == null)
            {
                Debug.LogWarning(
                    $"[Obstacle Generator] Prefab slot {prefabIndex} " +
                    "is empty. Skipping this obstacle.",
                    this
                );

                continue;
            }

            GameObject spawnedObject =
                Instantiate(
                    selectedPrefab,
                    new Vector3(
                        spawnPosition.x,
                        spawnPosition.y,
                        0f
                    ),
                    Quaternion.identity
                );

            NetworkObject networkObject =
                spawnedObject.GetComponent<NetworkObject>();

            if (networkObject == null)
            {
                Debug.LogError(
                    $"[Obstacle Generator] Prefab " +
                    $"'{selectedPrefab.name}' does not have a " +
                    "NetworkObject component. Destroying the object.",
                    spawnedObject
                );

                Destroy(
                    spawnedObject
                );

                continue;
            }

            networkObject.Spawn();

            generatedPositions.Add(
                spawnPosition
            );

            successfullySpawned++;

            if (logGeneratedObstacles)
            {
                Debug.Log(
                    $"[Obstacle Generator] Spawned " +
                    $"'{selectedPrefab.name}' " +
                    $"at {spawnPosition}.",
                    spawnedObject
                );
            }
        }

        generationComplete =
            true;

        Debug.Log(
            $"[Obstacle Generator] Generation complete.\n" +
            $"Run Seed: {runDirector.CurrentSeed}\n" +
            $"Random Stream: {RandomStreamName}\n" +
            $"Requested Obstacles: {obstacleCount}\n" +
            $"Successfully Spawned: {successfullySpawned}",
            this
        );
    }

    private bool TryFindSpawnPosition(
        System.Random rng,
        float minimumSpacing,
        int maximumAttempts,
        out Vector2 validPosition
    )
    {
        Bounds bounds =
            generationArea.bounds;

        for (
            int attempt = 0;
            attempt < maximumAttempts;
            attempt++
        )
        {
            float normalizedX =
                (float)rng.NextDouble();

            float normalizedY =
                (float)rng.NextDouble();

            float x =
                Mathf.Lerp(
                    bounds.min.x,
                    bounds.max.x,
                    normalizedX
                );

            float y =
                Mathf.Lerp(
                    bounds.min.y,
                    bounds.max.y,
                    normalizedY
                );

            Vector2 candidatePosition =
                new Vector2(
                    x,
                    y
                );

            if (
                IsFarEnoughFromExistingObstacles(
                    candidatePosition,
                    minimumSpacing
                )
            )
            {
                validPosition =
                    candidatePosition;

                return true;
            }
        }

        validPosition =
            Vector2.zero;

        return false;
    }

    private bool IsFarEnoughFromExistingObstacles(
        Vector2 candidatePosition,
        float minimumSpacing
    )
    {
        float minimumSpacingSquared =
            minimumSpacing *
            minimumSpacing;

        foreach (
            Vector2 existingPosition
            in generatedPositions
        )
        {
            float distanceSquared =
                (
                    candidatePosition -
                    existingPosition
                ).sqrMagnitude;

            if (
                distanceSquared <
                minimumSpacingSquared
            )
            {
                return false;
            }
        }

        return true;
    }
}