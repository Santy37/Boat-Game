using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using DeadmansTales.Networking;

/// <summary>
/// LEVEL ONE -- "Crab Beach", the tutorial island that teaches the opening
/// mechanics: move, melee, and open a chest.
///
/// Shaped as a SHORELINE LOOP. The crew lands on the south sand and walks
/// anticlockwise round the island, meeting one teaching hint per leg, and the
/// only crabs on the beach wait at the eastern dock -- so the walk teaches and
/// the dock tests. The exit rowboat already refuses to sail while an enemy
/// lives, which turns those two crabs into the level's one gate.
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

    // Teaching beats along the SHORELINE LOOP: the crew spawns south-centre,
    // walks anticlockwise round the island -- south shore, west lobe, north
    // shore -- and arrives at the dock in the east having been taught one
    // mechanic per leg. Each of these is a "star" on the level-one sketch.
    //
    // The loop replaced a straight west -> east march whose hints all bunched
    // into the first few metres: three of the four fired before the player had
    // walked twenty units, and the last one sat alone at the far dock.
    //
    // Positions match the enlarged island the builder paints (64x34 cells):
    // the crew spawns on the south beach (~0,-12), the reward chest sits on
    // the north-shore walk at (1,11), and the exit rowboat is east at
    // ~(32.5, 2).
    private static readonly (Vector2 Position, Vector2 Size, string Message,
        bool Once)[] TutorialPrompts =
    {
        // STAR ONE -- south shore, stretched WEST from the spawn point.
        //
        // Deliberately wide enough to still cover the spawn itself: a "how to
        // move" hint the player has to walk to in order to read is no hint at
        // all. TutorialPrompt2D.OnTriggerStay2D fires it the instant the crew
        // lands, and it stays up for the first leg of the walk.
        (new Vector2(-6f, -12f), new Vector2(20f, 8f),
            "WASD  /  Arrow Keys  to move", true),

        // STAR TWO -- the west lobe, at the turn north. Taught here, well
        // clear of anything hostile, because the only crabs on the island now
        // wait at the dock; the walk teaches, the dock tests.
        (new Vector2(-20f, 0f), new Vector2(12f, 14f),
            "Left Click to attack  -  you swing toward your cursor", true),

        // STAR THREE -- north shore, just west of the reward chest, so the
        // hint is already on screen when the chest comes into view.
        (new Vector2(-5f, 11f), new Vector2(13f, 7f),
            "Press  E  to open the chest  -  eat the food it drops to heal",
            false),

        // THE DOCK -- covers the two guard crabs AND the rowboat behind them,
        // so the gate reads as a fight to win rather than a portal that
        // silently refuses to work.
        (new Vector2(28f, 3f), new Vector2(16f, 12f),
            "Defeat the crabs, then step onto the rowboat to sail on", false),
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
        // Coconut is deliberately excluded: its prefab points at a sprite cut
        // from the BEACH TILESET (tf_beach_tileB) rather than the food art, so
        // it renders as the top of a grass tuft lying on the sand. Apple and
        // meat both come from island_food_items and read correctly.
        string[] foodPaths =
        {
            "Assets/DeadmansTales/Prefabs/Gameplay/"
                + "NetworkFoodPickup_Apple.prefab",
            "Assets/DeadmansTales/Prefabs/Gameplay/"
                + "NetworkFoodPickup_Meat.prefab",
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
    /// Leaves level one with exactly ONE chest: the guaranteed reward.
    ///
    /// The seeded generator draws chests from two budgets -- the Loot category
    /// (which scattered 2-3 around the loot markers) and the Reward category
    /// (the single guaranteed chest). Zeroing the Loot budget leaves that one
    /// as the level's only prize, which reads much clearer in a tutorial.
    ///
    /// MoveChestOntoRoute then parks it on the shoreline walk; it is no longer
    /// at the island centre, where the old west -> east march used to pass.
    /// </summary>
    [MenuItem(MenuRoot + "7. Single Centre Chest")]
    public static void UseSingleCentreChest()
    {
        if (!TryOpenLevelOne(out Scene scene))
        {
            return;
        }

        int updated = 0;
        foreach (DeadmansTales.WorldGeneration.SeededIslandContentGenerator
            generator in Object.FindObjectsByType<
                DeadmansTales.WorldGeneration.SeededIslandContentGenerator>(
                FindObjectsSortMode.None))
        {
            SerializedObject so = new SerializedObject(generator);
            SerializedProperty budgets = so.FindProperty("contentBudgets");
            if (budgets == null)
            {
                continue;
            }

            for (int i = 0; i < budgets.arraySize; i++)
            {
                SerializedProperty entry = budgets.GetArrayElementAtIndex(i);
                SerializedProperty category =
                    entry.FindPropertyRelative("category");
                if (category == null)
                {
                    continue;
                }

                // 1 == SeededContentCategory.Loot
                if (category.enumValueIndex != 1)
                {
                    continue;
                }

                entry.FindPropertyRelative("minimumCount").intValue = 0;
                entry.FindPropertyRelative("maximumCount").intValue = 0;
                updated++;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(generator);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log(updated > 0
            ? $"[Level One] Loot budget zeroed ({updated} entry) -- only the "
                + "centre reward chest remains."
            : "[Level One] No Loot budget entry found to zero.");
    }

    private const string DeathScreenPrefabPath =
        "Assets/DeadmansTales/Prefabs/UI/DeathScreenUI.prefab";

    /// <summary>
    /// Adds Shay's damage/death screen UI (the red border that flashes when
    /// you get hit, plus the death screen) to level one.
    ///
    /// That prefab was hand-placed into the other combat scenes (post-Ocean
    /// island, kraken arena) rather than placed by the island painter, so the
    /// generated level one never received it -- players took crab hits with no
    /// screen feedback at all. The component finds the local player's health
    /// at runtime, so an instance at the scene root is all it needs.
    /// </summary>
    [MenuItem(MenuRoot + "8. Add Damage/Death Screen UI")]
    public static void EnsureDeathScreenUi()
    {
        if (!TryOpenLevelOne(out Scene scene))
        {
            return;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            DeathScreenPrefabPath);
        if (prefab == null)
        {
            Debug.LogError(
                $"[Level One] Missing {DeathScreenPrefabPath}; cannot add "
                + "damage feedback.");
            return;
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (PrefabUtility.GetCorrespondingObjectFromOriginalSource(root)
                == prefab)
            {
                Debug.Log("[Level One] Damage/death screen UI already "
                    + "present.");
                return;
            }
        }

        PrefabUtility.InstantiatePrefab(prefab, scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Level One] Added the damage/death screen UI.");
    }

    // Where the guard crabs stand: the last stretch of sand between the north
    // shore and the dock, so the crew meets them on the way to the rowboat.
    //
    // Mirrors LevelOneEnemyMarkers in IslandStageBuilder. That array is what a
    // full repaint produces; this is the in-place edit for the hand-authored
    // scene, which a repaint would otherwise flatten.
    private static readonly Vector2[] DockGuardCrabs =
    {
        new Vector2(24f, 5f),
        new Vector2(28f, 2f),
    };

    /// <summary>
    /// Moves level one's crabs to the dock and deletes the surplus.
    ///
    /// The island shipped with eight crab markers strewn from x=-9 to x=13.
    /// The exit rowboat refuses to sail until every enemy is dead, so one crab
    /// missed behind a palm sent players back across the whole beach with no
    /// idea what the portal wanted. Two crabs, both standing between the
    /// shoreline walk and the rowboat, make that gate self-evident.
    ///
    /// Both are guaranteed to show up: the generator clamps a category's
    /// minimum to the number of markers that exist and then backfills up to it,
    /// so trimming the roster to two also retires the 72% per-marker roll.
    ///
    /// ADDITIVE, like AddTutorialPrompts -- touches only the enemy markers.
    /// </summary>
    [MenuItem(MenuRoot + "9. Move The Crabs To The Dock")]
    public static void MoveCrabsToDock()
    {
        if (!TryOpenLevelOne(out Scene scene))
        {
            return;
        }

        DeadmansTales.WorldGeneration.SeededSpawnMarker2D[] crabs = Object
            .FindObjectsByType<
                DeadmansTales.WorldGeneration.SeededSpawnMarker2D>(
                FindObjectsSortMode.None)
            .Where(marker => marker.Category
                == DeadmansTales.WorldGeneration.SeededContentCategory.Enemy)
            .OrderBy(marker => marker.name)
            .ToArray();

        if (crabs.Length == 0)
        {
            Debug.LogError("[Level One] No enemy markers found to move.");
            return;
        }

        int moved = 0;
        while (moved < crabs.Length && moved < DockGuardCrabs.Length)
        {
            crabs[moved].transform.position = new Vector3(
                DockGuardCrabs[moved].x, DockGuardCrabs[moved].y, 0f);
            EditorUtility.SetDirty(crabs[moved]);
            moved++;
        }

        int removed = 0;
        for (int i = DockGuardCrabs.Length; i < crabs.Length; i++)
        {
            Object.DestroyImmediate(crabs[i].gameObject);
            removed++;
        }

        if (moved < DockGuardCrabs.Length)
        {
            Debug.LogWarning($"[Level One] Only {moved} enemy marker(s) "
                + $"existed; the dock wants {DockGuardCrabs.Length}. Run "
                + "\"0. Build Everything\" to repaint the full roster.");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Level One] {moved} crab(s) now guard the dock; "
            + $"{removed} surplus marker(s) removed.");
    }

    // The chest's home on the new route: the north shore, a few metres east of
    // the third hint, so "press E" is already on screen when it comes into
    // view. Mirrors activeRewardPosition in IslandStageBuilder's level-one
    // build, so an in-place fix and a full repaint agree.
    private static readonly Vector2 ChestOnRoute = new Vector2(1f, 11f);

    /// <summary>
    /// Puts the level's one chest on the shoreline walk.
    ///
    /// It used to sit at the island centre, which the old straight west -> east
    /// march crossed anyway. The shoreline loop does not cut through the
    /// middle, so leaving the chest there would have stranded it -- and the
    /// eat-to-heal lesson attached to it -- off the route entirely.
    /// </summary>
    [MenuItem(MenuRoot + "10. Put The Chest On The Walking Route")]
    public static void MoveChestOntoRoute()
    {
        if (!TryOpenLevelOne(out Scene scene))
        {
            return;
        }

        int moved = 0;
        foreach (DeadmansTales.WorldGeneration.SeededSpawnMarker2D marker
            in Object.FindObjectsByType<
                DeadmansTales.WorldGeneration.SeededSpawnMarker2D>(
                FindObjectsSortMode.None))
        {
            if (marker.Category != DeadmansTales.WorldGeneration
                .SeededContentCategory.Reward)
            {
                continue;
            }

            marker.transform.position = new Vector3(
                ChestOnRoute.x, ChestOnRoute.y, 0f);
            EditorUtility.SetDirty(marker);
            moved++;
        }

        if (moved == 0)
        {
            Debug.LogError(
                "[Level One] No reward marker found to move.");
            return;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Level One] Reward chest moved to the north shore "
            + $"({ChestOnRoute.x}, {ChestOnRoute.y}).");
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
        UseSingleCentreChest();
        MoveChestOntoRoute();
        MoveCrabsToDock();
        AddTutorialPrompts();
        EnsureDeathScreenUi();
    }

    [MenuItem(MenuRoot + "0. Build Everything")]
    public static void BuildAllFromCommandLine()
    {
        // Paint the island (silhouette, dock crabs, on-route reward chest,
        // spawns, rowboats, camera)...
        IslandStageBuilder.BuildLevelOneIsland();
        // ...then the content pass: chest food, single chest, tutorial
        // prompts, damage UI. The two in-place movers are no-ops right after
        // a repaint, but they repair a hand-edited scene the same way.
        ApplyContentFromCommandLine();
        // ...and make sure the lobby stayed a lobby.
        StripLobbyCombat();
        ReportLevelOne();
    }
}
