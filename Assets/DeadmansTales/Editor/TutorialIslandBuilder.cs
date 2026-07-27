using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using DeadmansTales.Networking;

/// <summary>
/// LEVEL ONE -- "Crab Beach", the tutorial island that teaches the opening
/// mechanics: walk up and melee a crab, and open a chest.
///
/// The island itself is painted by IslandStageBuilder.BuildLevelOneIsland,
/// which reuses the post-Ocean island's shoreline/prop/collision painter with
/// level one's own wider silhouette and a crab-only enemy roster. An earlier
/// version of this builder just Save-As copied the lobby island, which gave
/// level one the lobby's exact outline and left no room to spread content out.
///
/// This class owns the rest of the level-one story: making the LOBBY a lobby
/// (its old enemy spawner belongs to level one now) and checking the result.
/// Idempotent, and runnable headless via BuildAllFromCommandLine.
/// </summary>
public static class TutorialIslandBuilder
{
    private const string MenuRoot = "Deadman's Tales/Level One/";

    private const string LobbyScenePath =
        "Assets/DeadmansTales/Scenes/Island/Lobby_Island_2D.unity";
    private const string LevelOneScenePath =
        "Assets/DeadmansTales/Scenes/Island/Level_1_Crab_Beach_2D.unity";

    /// <summary>
    /// Make the lobby island a LOBBY: no combat. The enemy spawner it used to
    /// carry is level one's job now.
    /// </summary>
    [MenuItem(MenuRoot + "2. Make The Lobby A Pure Lobby")]
    public static void StripLobbyCombat()
    {
        Scene scene = EditorSceneManager.OpenScene(
            LobbyScenePath, OpenSceneMode.Single);

        int removed = 0;
        foreach (NetworkSceneEnemySpawner2D spawner in Object
            .FindObjectsByType<NetworkSceneEnemySpawner2D>(
                FindObjectsSortMode.None))
        {
            Object.DestroyImmediate(spawner.gameObject);
            removed++;
        }

        if (removed == 0)
        {
            Debug.Log("[Level One] Lobby already has no enemy spawner.");
            return;
        }

        bool saved = EditorSceneManager.SaveScene(scene, LobbyScenePath);
        Debug.Log(saved
            ? $"[Level One] Lobby is now a pure lobby (removed {removed} "
                + "enemy spawner(s))."
            : "[Level One] FAILED to save the lobby scene.");
    }

    /// <summary>
    /// Reports what level one actually contains: island size, spawns, crab
    /// markers and chests. Measured from the built scene rather than assumed.
    /// </summary>
    [MenuItem(MenuRoot + "3. Report Level One")]
    public static void ReportLevelOne()
    {
        Scene scene = EditorSceneManager.OpenScene(
            LevelOneScenePath, OpenSceneMode.Single);

        GameObject[] roots = scene.GetRootGameObjects();

        int spawns = roots
            .SelectMany(r => r.GetComponentsInChildren<PlayerSpawnPoint2D>(true))
            .Count();

        var tilemaps = roots
            .SelectMany(r => r
                .GetComponentsInChildren<UnityEngine.Tilemaps.Tilemap>(true))
            .ToArray();

        UnityEngine.Tilemaps.Tilemap ground = tilemaps
            .FirstOrDefault(t => t.name == "Tilemap_Ground");

        if (ground != null)
        {
            ground.CompressBounds();
            BoundsInt b = ground.cellBounds;
            Debug.Log($"[Level One] Island ground spans {b.size.x} x "
                + $"{b.size.y} cells (x {b.xMin}..{b.xMax}, "
                + $"y {b.yMin}..{b.yMax}).");
        }

        int crabMarkers = roots.Sum(r => r
            .GetComponentsInChildren<Transform>(true)
            .Count(t => t.name.StartsWith("EnemyMarker")));
        int lootMarkers = roots.Sum(r => r
            .GetComponentsInChildren<Transform>(true)
            .Count(t => t.name.StartsWith("LootMarker")));

        Debug.Log($"[Level One] Player spawns: {spawns}, enemy markers: "
            + $"{crabMarkers}, loot markers: {lootMarkers}, tilemap layers: "
            + $"{tilemaps.Length}.");
    }

    /// <summary>
    /// Makes level one's crabs and chests actually appear.
    ///
    /// The island painter stamps every seeded marker with minimumStage = 2,
    /// because it was written for the post-Ocean island, which is stage two.
    /// Level one runs at stage ONE, so
    /// SeededSpawnMarker2D.IsEligibleForStage rejected every marker and the
    /// island generated completely empty. This drops them to stage one.
    ///
    /// Operates on the scene that is already open when possible, so a
    /// hand-authored level is never reloaded out from under unsaved edits.
    /// </summary>
    [MenuItem(MenuRoot + "4. Fix Marker Stages (crabs + chests not spawning)")]
    public static void FixMarkerStages()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != LevelOneScenePath)
        {
            if (!EditorSceneManager
                .SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[Level One] Cancelled.");
                return;
            }
            scene = EditorSceneManager.OpenScene(
                LevelOneScenePath, OpenSceneMode.Single);
        }

        int changed = 0;
        foreach (DeadmansTales.WorldGeneration.SeededSpawnMarker2D marker
            in Object.FindObjectsByType<
                DeadmansTales.WorldGeneration.SeededSpawnMarker2D>(
                FindObjectsSortMode.None))
        {
            SerializedObject so = new SerializedObject(marker);
            SerializedProperty minimumStage =
                so.FindProperty("minimumStage");
            if (minimumStage == null || minimumStage.intValue <= 1)
            {
                continue;
            }

            minimumStage.intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(marker);
            changed++;
        }

        if (changed == 0)
        {
            Debug.Log("[Level One] Every marker already spawns at stage one.");
            return;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Level One] {changed} marker(s) now spawn at stage one -- "
            + "crabs and chests will appear.");
    }

    [MenuItem(MenuRoot + "0. Build Everything")]
    public static void BuildAllFromCommandLine()
    {
        // Paint the island (its own silhouette, crab roster, chest markers)...
        IslandStageBuilder.BuildLevelOneIsland();
        // ...then make sure the lobby stayed a lobby.
        StripLobbyCombat();
        ReportLevelOne();
    }
}
