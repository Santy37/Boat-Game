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
        if (!TryOpenLevelOne(out Scene scene))
        {
            return;
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

    // Teaching beats along the west -> east walk. Placed where the mechanic is
    // first needed rather than dumped at spawn.
    // Positions read from the hand-authored scene, not from the generated
    // layout: the crew spawns south-centre (~0,-9), the chests sit at the
    // loot markers, and the rowboat was moved east to ~(29.6, 2.5).
    private static readonly (Vector2 Position, Vector2 Size, string Message,
        bool Once)[] TutorialPrompts =
    {
        // Right on top of the spawn point -- the first thing anyone reads.
        (new Vector2(0f, -9f), new Vector2(13f, 7f),
            "WASD  /  Arrow Keys  to move", true),

        // Between spawn and the first crabs at (-2,-3) and (5,-4).
        (new Vector2(1f, -4.5f), new Vector2(16f, 6f),
            "Left Click to attack  -  you swing toward your cursor", true),

        // The southern chest (LootMarker_02) is the closest to spawn, so it
        // is where opening and eating get taught.
        (new Vector2(6f, -7f), new Vector2(5.5f, 5.5f),
            "Press  E  to open the chest  -  eat the food it drops to heal",
            false),

        // The eastern chest (LootMarker_03), on the way out.
        (new Vector2(15.7f, 2f), new Vector2(5.5f, 5.5f),
            "Press  E  to open the chest", false),

        // The relocated exit rowboat.
        (new Vector2(28f, 2.5f), new Vector2(9f, 9f),
            "Step onto the rowboat to sail on", false),
    };

    private const string PromptParentName = "Level1_TutorialPrompts";

    /// <summary>
    /// Adds the tutorial hint zones to level one, in place.
    ///
    /// ADDITIVE ON PURPOSE: level one is hand-authored, so this only replaces
    /// its own prompt group and never repaints the island or disturbs anything
    /// else in the scene.
    /// </summary>
    [MenuItem(MenuRoot + "5. Add Tutorial Prompts")]
    public static void AddTutorialPrompts()
    {
        if (!TryOpenLevelOne(out Scene scene))
        {
            return;
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == PromptParentName)
            {
                Object.DestroyImmediate(root);
            }
        }

        GameObject parent = new GameObject(PromptParentName);
        for (int i = 0; i < TutorialPrompts.Length; i++)
        {
            var prompt = TutorialPrompts[i];

            GameObject zone = new GameObject($"Prompt_{i:D2}");
            zone.transform.SetParent(parent.transform, true);
            zone.transform.position = new Vector3(
                prompt.Position.x, prompt.Position.y, 0f);

            BoxCollider2D area = zone.AddComponent<BoxCollider2D>();
            area.isTrigger = true;
            area.size = prompt.Size;

            TutorialPrompt2D hint = zone.AddComponent<TutorialPrompt2D>();
            SerializedObject so = new SerializedObject(hint);
            so.FindProperty("message").stringValue = prompt.Message;
            so.FindProperty("showOnlyOnce").boolValue = prompt.Once;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Level One] Added {TutorialPrompts.Length} tutorial "
            + "prompt zone(s).");
    }

    /// <summary>
    /// Makes reward chests spill food when opened, so level one teaches the
    /// eat-to-heal loop. Edits the chest PREFABS, so it applies wherever
    /// chests appear.
    /// </summary>
    [MenuItem(MenuRoot + "6. Make Chests Drop Food")]
    public static void MakeChestsDropFood()
    {
        string[] chestPaths =
        {
            "Assets/DeadmansTales/Prefabs/Gameplay/NetworkRewardChest.prefab",
            "Assets/DeadmansTales/Prefabs/Gameplay/"
                + "NetworkRewardChest_Weapon.prefab",
            "Assets/DeadmansTales/Prefabs/Gameplay/"
                + "NetworkRewardChest_Upgrade.prefab",
        };
        string[] foodPaths =
        {
            "Assets/DeadmansTales/Prefabs/Gameplay/"
                + "NetworkFoodPickup_Apple.prefab",
            "Assets/DeadmansTales/Prefabs/Gameplay/"
                + "NetworkFoodPickup_Meat.prefab",
            "Assets/DeadmansTales/Prefabs/Gameplay/"
                + "NetworkFoodPickup_Coconut.prefab",
        };

        GameObject[] food = foodPaths
            .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
            .Where(prefab => prefab != null)
            .ToArray();

        if (food.Length == 0)
        {
            Debug.LogError("[Level One] No food pickup prefabs found.");
            return;
        }

        int updated = 0;
        foreach (string path in chestPaths)
        {
            GameObject chest = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (chest == null)
            {
                continue;
            }

            NetworkRewardChest reward =
                chest.GetComponent<NetworkRewardChest>();
            if (reward == null)
            {
                continue;
            }

            SerializedObject so = new SerializedObject(reward);
            SerializedProperty prefabs =
                so.FindProperty("foodRewardPrefabs");
            SerializedProperty count = so.FindProperty("foodRewardCount");
            if (prefabs == null || count == null)
            {
                continue;
            }

            prefabs.arraySize = food.Length;
            for (int i = 0; i < food.Length; i++)
            {
                prefabs.GetArrayElementAtIndex(i).objectReferenceValue =
                    food[i];
            }
            count.intValue = 2;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(chest);
            updated++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Level One] {updated} chest prefab(s) now spill "
            + $"{food.Length} kinds of food (2 per chest).");
    }

    /// <summary>
    /// Gets level one open for editing. Uses the already-open scene when it is
    /// the right one, so a hand-authored level is never reloaded out from
    /// under unsaved edits, and never shows a save prompt in batch mode.
    /// </summary>
    private static bool TryOpenLevelOne(out Scene scene)
    {
        scene = SceneManager.GetActiveScene();
        if (scene.path == LevelOneScenePath)
        {
            return true;
        }

        if (!Application.isBatchMode
            && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.Log("[Level One] Cancelled.");
            return false;
        }

        scene = EditorSceneManager.OpenScene(
            LevelOneScenePath, OpenSceneMode.Single);
        return scene.IsValid();
    }

    /// <summary>
    /// Applies both level-one content passes in one headless run.
    /// </summary>
    public static void ApplyContentFromCommandLine()
    {
        MakeChestsDropFood();
        AddTutorialPrompts();
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
