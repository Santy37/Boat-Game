using UnityEngine;

/// <summary>
/// One per gameplay scene. Declares what this scene needs so the run manager
/// never has to know whether the scene is 2D or 3D.
///
/// A 3D island simply points at a 3D player prefab and 3D spawn points; the run
/// manager code is unchanged.
/// </summary>
public class SceneRunProfile : MonoBehaviour
{
    [Header("Scene")]
    public RunSceneKind kind = RunSceneKind.Island;

    [Header("Players")]
    [Tooltip("Uncheck for scenes that need no player bodies, such as the Map.")]
    public bool spawnPlayers = true;

    [Tooltip("The player prefab THIS scene needs: 2D sprite or 3D character.")]
    public GameObject playerPrefab;

    [Tooltip("Where players are placed, in player order.")]
    public Transform[] spawnPoints;

    public static SceneRunProfile Find()
    {
        return FindFirstObjectByType<SceneRunProfile>();
    }

    public Vector3 GetSpawnPosition(int playerIndex)
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            return Vector3.zero;
        }

        Transform point = spawnPoints[playerIndex % spawnPoints.Length];
        return point != null ? point.position : Vector3.zero;
    }
}
