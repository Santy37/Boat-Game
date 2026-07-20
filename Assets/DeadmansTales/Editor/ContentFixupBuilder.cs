using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DeadmansTales.Networking;
using DeadmansTales.WorldGeneration;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

/// <summary>
/// Playtest-feedback fixup pass:
///  - Boat scene: removes the fullscreen "PROTOTYPE COMPLETE" overlay, gives
///    the ship tilemap the Grid it needs to render at all, moves the ship
///    survival stations and player spawns onto the actual deck, and rebuilds
///    the ship HUD on its own dedicated canvas.
///  - Crab/skeleton: hides the leftover placeholder sprite on the prefab
///    roots that rendered alongside the real art.
///  - Island: skeleton warriors and the new orc join the enemy marker pools.
///  - Player: full overhaul to the Tiny RPG Soldier with real idle/walk/
///    attack/hurt/death animations. Orc art replaces the BoneBrute tint.
///
/// Idempotent: safe to run repeatedly.
/// </summary>
public static class ContentFixupBuilder
{
    private const string MenuPath =
        "Deadman's Tales/Fix Boat Scene + Character Overhaul";

    private const string BoatScenePath =
        "Assets/DeadmansTales/Scenes/Boat_Gameplay_2D.unity";

    private const string IslandScenePath =
        "Assets/DeadmansTales/Scenes/Island_After_Ocean_01_2D.unity";

    private const string TinyRpgFolder =
        "Assets/DeadmansTales/Art_Pixel/Characters/TinyRPG";

    private const string SoldierFolder = TinyRpgFolder + "/Soldier";
    private const string OrcFolder = TinyRpgFolder + "/Orc";

    private const string AnimationFolder =
        "Assets/DeadmansTales/Animations";
    private const string SoldierAnimationFolder =
        AnimationFolder + "/SoldierPlayer2D";
    private const string OrcAnimationFolder =
        AnimationFolder + "/OrcBrute2D";

    private const string PlayerPrefabPath =
        "Assets/DeadmansTales/Prefabs/Player_2D_Network.prefab";

    private const string GameplayPrefabFolder =
        "Assets/DeadmansTales/Prefabs/Gameplay";
    private const string CrabPrefabPath =
        GameplayPrefabFolder + "/Enemy_CrabSkitter.prefab";
    private const string SkeletonPrefabPath =
        GameplayPrefabFolder + "/Enemy_SkeletonWarrior.prefab";
    private const string BoneBrutePrefabPath =
        GameplayPrefabFolder + "/Enemy_BoneBrute.prefab";

    private const int CharacterFrameSize = 100;
    private const int SkeletonFrameSize = 96;
    private const int CrabFrameSize = 32;
    private const int ChestFrameSize = 32;

    private const string ChestSheetPath =
        "Assets/DeadmansTales/Art_Pixel/Props/rpg_chests.png";

    private const string SkeletonArtFolder =
        "Assets/DeadmansTales/Art_Pixel/Characters/SkeletonWarrior";
    private const string CrabSheetPath =
        "Assets/DeadmansTales/Art_Pixel/Characters/crab_sheet.png";

    /// <summary>
    /// These packs draw a small character inside a much larger canvas
    /// (room for weapon swings), so pixels-per-unit cannot be derived from
    /// the frame size directly. Instead each sheet's actual drawn (trimmed,
    /// non-transparent) pixel height is measured and scaled to hit a target
    /// world-space height, matching the original placeholder's on-screen
    /// scale (72px tall at 32 px/unit = 2.25 units) so nothing looks like a
    /// doll standing next to giants or vice versa.
    /// </summary>
    private const float HumanoidTargetWorldHeight = 2.2f;
    private const float BruteTargetWorldHeight = 2.9f;
    private const float CrabTargetWorldHeight = 0.9f;
    private const float ChestTargetWorldHeight = 0.75f;

    // Matches Lobby_Island_2D and Boat_Gameplay_2D's camera so all three
    // gameplay scenes share the same tiles-per-screen density.
    private const float IslandCameraOrthographicSize = 11.25f;

    [MenuItem(MenuPath)]
    public static void BuildAll()
    {
        // Rebuilds skeleton/crab/chest with the corrected crab attack
        // frames and bottom-anchored pivots before this pass re-fits their
        // pixels-per-unit on top.
        EnemyAndChestArtBuilder.BuildAllFromCommandLine();

        SliceCharacterSheets();

        // Re-slice the already-built skeleton, crab, and chest sheets at a
        // measured, correctly-scaled pixels-per-unit. This only changes
        // pixel density, not sprite names/GUIDs, so the animation clips
        // built earlier keep working without being rebuilt.
        AutoSliceCharacterFolder(
            SkeletonArtFolder,
            SkeletonFrameSize,
            HumanoidTargetWorldHeight,
            new[] { "All-spritesheet" }
        );
        ConfigureGridSheet(
            CrabSheetPath,
            CrabFrameSize,
            ComputeAutoPixelsPerUnit(
                CrabSheetPath,
                CrabFrameSize,
                CrabTargetWorldHeight
            )
        );
        ConfigureGridSheet(
            ChestSheetPath,
            ChestFrameSize,
            ComputeAutoPixelsPerUnit(
                ChestSheetPath,
                ChestFrameSize,
                ChestTargetWorldHeight
            )
        );

        AssetDatabase.Refresh();

        BuildSoldierAnimations();
        BuildOrcAnimations();

        OverhaulPlayerPrefab();
        HideLegacyRootSprite(CrabPrefabPath);
        HideLegacyRootSprite(SkeletonPrefabPath);
        UpgradeBoneBruteToOrc();

        AddNewEnemiesToIslandMarkers();
        AddCoconutFoodToIslandMarkers();
        FixIslandCameraZoom();
        FixBoatScene();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "[Fixup Builder] Boat scene repaired, player overhauled to " +
            "Soldier, crab/skeleton placeholder sprites hidden, orc + " +
            "skeleton added to island spawns, coconut food added, island " +
            "camera zoom matched to the other scenes."
        );
    }

    public static void BuildAllFromCommandLine()
    {
        BuildAll();
    }

    // ------------------------------------------------------------------
    // Tiny RPG sheet slicing
    // ------------------------------------------------------------------

    private static void SliceCharacterSheets()
    {
        AutoSliceCharacterFolder(
            SoldierFolder,
            CharacterFrameSize,
            HumanoidTargetWorldHeight,
            new[] { "Soldier" }
        );
        AutoSliceCharacterFolder(
            OrcFolder,
            CharacterFrameSize,
            BruteTargetWorldHeight,
            new[] { "Orc" }
        );
    }

    /// <summary>
    /// Slices every animation sheet in a character folder at one shared
    /// pixels-per-unit, computed from the drawn (trimmed) height of a
    /// representative frame so the character reaches
    /// <paramref name="targetWorldHeight"/> world units tall.
    /// </summary>
    private static void AutoSliceCharacterFolder(
        string folder,
        int frameSize,
        float targetWorldHeight,
        string[] excludeFileNames
    )
    {
        string[] sheets = Directory
            .GetFiles(folder, "*.png", SearchOption.AllDirectories)
            .Select(path => path.Replace('\\', '/'))
            .Where(path => !excludeFileNames.Contains(
                Path.GetFileNameWithoutExtension(path)))
            .ToArray();

        if (sheets.Length == 0)
        {
            throw new InvalidOperationException(
                $"No usable sprite sheets found in {folder}."
            );
        }

        string referenceSheet =
            sheets.FirstOrDefault(path => path.Contains("Idle")) ??
            sheets[0];

        int pixelsPerUnit = ComputeAutoPixelsPerUnit(
            referenceSheet,
            frameSize,
            targetWorldHeight
        );

        foreach (string sheet in sheets)
        {
            ConfigureGridSheet(sheet, frameSize, pixelsPerUnit);
        }
    }

    /// <summary>
    /// Measures the non-transparent pixel height of the sheet's top-left
    /// frame directly from the source file (independent of any existing
    /// import settings) and returns the pixels-per-unit that makes that
    /// drawn height equal <paramref name="targetWorldHeight"/> world units.
    /// </summary>
    private static int ComputeAutoPixelsPerUnit(
        string assetPath,
        int frameSize,
        float targetWorldHeight
    )
    {
        string fullPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            assetPath.Replace('/', Path.DirectorySeparatorChar)
        );

        byte[] bytes = File.ReadAllBytes(fullPath);
        Texture2D probe = new Texture2D(2, 2, TextureFormat.RGBA32, false);

        try
        {
            probe.LoadImage(bytes);

            // Texture2D pixel rows are bottom-up; the top-left VISUAL frame
            // (frame index 0) is the top rows of the source image.
            int yStart = Mathf.Max(0, probe.height - frameSize);
            Color32[] pixels = probe.GetPixels32();

            int minY = frameSize;
            int maxY = -1;

            for (int y = 0; y < frameSize; y++)
            {
                int sourceY = yStart + y;
                if (sourceY < 0 || sourceY >= probe.height)
                {
                    continue;
                }

                for (int x = 0; x < frameSize && x < probe.width; x++)
                {
                    if (pixels[sourceY * probe.width + x].a > 10)
                    {
                        if (y < minY)
                        {
                            minY = y;
                        }

                        if (y > maxY)
                        {
                            maxY = y;
                        }
                    }
                }
            }

            int trimmedHeight = maxY >= minY
                ? maxY - minY + 1
                : frameSize;

            return Mathf.Max(
                1,
                Mathf.RoundToInt(trimmedHeight / targetWorldHeight)
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(probe);
        }
    }

    // ------------------------------------------------------------------
    // Animations
    // ------------------------------------------------------------------

    private static void BuildSoldierAnimations()
    {
        EnsureFolder(SoldierAnimationFolder);

        AnimationClip idle = CreateSpriteClip(
            SoldierAnimationFolder + "/Soldier_Idle.anim",
            LoadFrames(SoldierFolder + "/Soldier_Idle.png"),
            6f,
            true
        );
        AnimationClip walk = CreateSpriteClip(
            SoldierAnimationFolder + "/Soldier_Walk.anim",
            LoadFrames(SoldierFolder + "/Soldier_Walk.png"),
            12f,
            true
        );
        AnimationClip attack = CreateSpriteClip(
            SoldierAnimationFolder + "/Soldier_Attack.anim",
            LoadFrames(SoldierFolder + "/Soldier_Attack01.png"),
            16f,
            false
        );
        AnimationClip hurt = CreateSpriteClip(
            SoldierAnimationFolder + "/Soldier_Hurt.anim",
            LoadFrames(SoldierFolder + "/Soldier_Hurt.png"),
            12f,
            false
        );
        AnimationClip death = CreateSpriteClip(
            SoldierAnimationFolder + "/Soldier_Death.anim",
            LoadFrames(SoldierFolder + "/Soldier_Death.png"),
            8f,
            false
        );

        BuildCharacterController(
            SoldierAnimationFolder + "/SoldierPlayer2D.controller",
            idle,
            walk,
            attack,
            hurt,
            death
        );
    }

    private static void BuildOrcAnimations()
    {
        EnsureFolder(OrcAnimationFolder);

        AnimationClip idle = CreateSpriteClip(
            OrcAnimationFolder + "/Orc_Idle.anim",
            LoadFrames(OrcFolder + "/Orc_Idle.png"),
            6f,
            true
        );
        AnimationClip walk = CreateSpriteClip(
            OrcAnimationFolder + "/Orc_Walk.anim",
            LoadFrames(OrcFolder + "/Orc_Walk.png"),
            10f,
            true
        );
        AnimationClip attack = CreateSpriteClip(
            OrcAnimationFolder + "/Orc_Attack.anim",
            LoadFrames(OrcFolder + "/Orc_Attack01.png"),
            14f,
            false
        );
        AnimationClip hurt = CreateSpriteClip(
            OrcAnimationFolder + "/Orc_Hurt.anim",
            LoadFrames(OrcFolder + "/Orc_Hurt.png"),
            12f,
            false
        );
        AnimationClip death = CreateSpriteClip(
            OrcAnimationFolder + "/Orc_Death.anim",
            LoadFrames(OrcFolder + "/Orc_Death.png"),
            8f,
            false
        );

        BuildCharacterController(
            OrcAnimationFolder + "/OrcBrute2D.controller",
            idle,
            walk,
            attack,
            hurt,
            death
        );
    }

    /// <summary>
    /// Speed-driven idle/walk plus Attack/Hurt/Die triggers. Matches the
    /// parameters PlayerAttack ("Attack") and PlayerHealth ("Die") fire.
    /// </summary>
    private static void BuildCharacterController(
        string assetPath,
        AnimationClip idle,
        AnimationClip walk,
        AnimationClip attack,
        AnimationClip hurt,
        AnimationClip death
    )
    {
        AssetDatabase.DeleteAsset(assetPath);
        AnimatorController controller =
            AnimatorController.CreateAnimatorControllerAtPath(assetPath);

        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter(
            "Attack",
            AnimatorControllerParameterType.Trigger
        );
        controller.AddParameter(
            "Hurt",
            AnimatorControllerParameterType.Trigger
        );
        controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;

        AnimatorState idleState = stateMachine.AddState("Idle");
        idleState.motion = idle;
        stateMachine.defaultState = idleState;

        AnimatorState walkState = stateMachine.AddState("Walk");
        walkState.motion = walk;

        AnimatorStateTransition toWalk = idleState.AddTransition(walkState);
        toWalk.hasExitTime = false;
        toWalk.duration = 0f;
        toWalk.AddCondition(AnimatorConditionMode.Greater, 0.15f, "Speed");

        AnimatorStateTransition toIdle = walkState.AddTransition(idleState);
        toIdle.hasExitTime = false;
        toIdle.duration = 0f;
        toIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");

        AnimatorState attackState = stateMachine.AddState("Attack");
        attackState.motion = attack;

        AnimatorStateTransition anyToAttack =
            stateMachine.AddAnyStateTransition(attackState);
        anyToAttack.hasExitTime = false;
        anyToAttack.duration = 0f;
        anyToAttack.canTransitionToSelf = false;
        anyToAttack.AddCondition(AnimatorConditionMode.If, 0f, "Attack");

        AnimatorStateTransition attackDone =
            attackState.AddTransition(idleState);
        attackDone.hasExitTime = true;
        attackDone.exitTime = 1f;
        attackDone.duration = 0f;

        AnimatorState hurtState = stateMachine.AddState("Hurt");
        hurtState.motion = hurt;

        AnimatorStateTransition anyToHurt =
            stateMachine.AddAnyStateTransition(hurtState);
        anyToHurt.hasExitTime = false;
        anyToHurt.duration = 0f;
        anyToHurt.canTransitionToSelf = false;
        anyToHurt.AddCondition(AnimatorConditionMode.If, 0f, "Hurt");

        AnimatorStateTransition hurtDone = hurtState.AddTransition(idleState);
        hurtDone.hasExitTime = true;
        hurtDone.exitTime = 1f;
        hurtDone.duration = 0f;

        AnimatorState deathState = stateMachine.AddState("Die");
        deathState.motion = death;

        AnimatorStateTransition anyToDeath =
            stateMachine.AddAnyStateTransition(deathState);
        anyToDeath.hasExitTime = false;
        anyToDeath.duration = 0f;
        anyToDeath.canTransitionToSelf = false;
        anyToDeath.AddCondition(AnimatorConditionMode.If, 0f, "Die");
    }

    // ------------------------------------------------------------------
    // Player overhaul
    // ------------------------------------------------------------------

    private static void OverhaulPlayerPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);

        try
        {
            Transform gfx = FindDeepChild(root.transform, "GFX");
            if (gfx == null)
            {
                throw new InvalidOperationException(
                    "Player prefab has no GFX child."
                );
            }

            SpriteRenderer renderer = gfx.GetComponent<SpriteRenderer>();
            renderer.sprite =
                LoadFrames(SoldierFolder + "/Soldier_Idle.png")[0];
            renderer.color = Color.white;
            renderer.enabled = true;

            DisableNonGfxSpriteRenderers(root);

            Animator animator = gfx.GetComponent<Animator>();
            if (animator == null)
            {
                animator = gfx.gameObject.AddComponent<Animator>();
            }

            animator.runtimeAnimatorController =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(
                    SoldierAnimationFolder + "/SoldierPlayer2D.controller"
                );

            // The old 4-direction placeholder driver plays states that no
            // longer exist; the motion animator drives Speed + facing flip
            // for the new side-view sheets instead.
            PlayerAnimation2D legacyDriver =
                root.GetComponentInChildren<PlayerAnimation2D>(true);
            if (legacyDriver != null)
            {
                UnityEngine.Object.DestroyImmediate(legacyDriver, true);
            }

            EnemyMotionAnimator motion =
                root.GetComponent<EnemyMotionAnimator>();
            if (motion == null)
            {
                motion = root.AddComponent<EnemyMotionAnimator>();
            }

            SerializedObject serializedMotion = new SerializedObject(motion);
            serializedMotion.FindProperty("animator").objectReferenceValue =
                animator;
            serializedMotion.FindProperty("facingRenderer")
                .objectReferenceValue = renderer;
            serializedMotion.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ------------------------------------------------------------------
    // Enemy prefab fixes
    // ------------------------------------------------------------------

    /// <summary>
    /// basicenemy renders the old placeholder character on a legacy sprite
    /// renderer elsewhere in the hierarchy (not consistently the prefab
    /// root — on some variants it sits on a different child) while the real
    /// art lives on the GFX child. Every SpriteRenderer outside the GFX
    /// subtree is disabled so no leftover placeholder can render alongside
    /// the real art, regardless of where it lives.
    /// </summary>
    private static void HideLegacyRootSprite(string prefabPath)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
        {
            Debug.LogWarning($"[Fixup] Missing prefab: {prefabPath}");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);

        try
        {
            DisableNonGfxSpriteRenderers(root);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// Disables (and clears) every SpriteRenderer in the hierarchy that is
    /// not the GFX child or one of its descendants, wherever that renderer
    /// actually lives.
    /// </summary>
    private static void DisableNonGfxSpriteRenderers(GameObject root)
    {
        Transform gfx = FindDeepChild(root.transform, "GFX");

        foreach (SpriteRenderer renderer in
            root.GetComponentsInChildren<SpriteRenderer>(true))
        {
            bool isGfxOrUnderGfx =
                gfx != null &&
                (renderer.transform == gfx ||
                    renderer.transform.IsChildOf(gfx));

            if (isGfxOrUnderGfx)
            {
                continue;
            }

            renderer.enabled = false;
            renderer.sprite = null;
        }
    }

    private static void UpgradeBoneBruteToOrc()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(
                BoneBrutePrefabPath) == null)
        {
            Debug.LogWarning(
                "[Fixup] Enemy_BoneBrute.prefab is missing; skipping the " +
                "orc upgrade."
            );
            return;
        }

        GameObject root =
            PrefabUtility.LoadPrefabContents(BoneBrutePrefabPath);

        try
        {
            DisableNonGfxSpriteRenderers(root);

            Transform gfx = FindDeepChild(root.transform, "GFX");
            if (gfx == null)
            {
                throw new InvalidOperationException(
                    "Enemy_BoneBrute has no GFX child."
                );
            }

            SpriteRenderer renderer = gfx.GetComponent<SpriteRenderer>();
            renderer.sprite = LoadFrames(OrcFolder + "/Orc_Idle.png")[0];
            renderer.color = Color.white;
            renderer.enabled = true;

            Animator animator = gfx.GetComponent<Animator>();
            if (animator == null)
            {
                animator = gfx.gameObject.AddComponent<Animator>();
            }

            animator.runtimeAnimatorController =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(
                    OrcAnimationFolder + "/OrcBrute2D.controller"
                );

            EnemyMotionAnimator motion =
                root.GetComponent<EnemyMotionAnimator>();
            if (motion == null)
            {
                motion = root.AddComponent<EnemyMotionAnimator>();
            }

            SerializedObject serializedMotion = new SerializedObject(motion);
            serializedMotion.FindProperty("animator").objectReferenceValue =
                animator;
            serializedMotion.FindProperty("facingRenderer")
                .objectReferenceValue = renderer;
            serializedMotion.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, BoneBrutePrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ------------------------------------------------------------------
    // Island: enemy variety
    // ------------------------------------------------------------------

    private static void AddNewEnemiesToIslandMarkers()
    {
        GameObject skeleton =
            AssetDatabase.LoadAssetAtPath<GameObject>(SkeletonPrefabPath);
        GameObject orc =
            AssetDatabase.LoadAssetAtPath<GameObject>(BoneBrutePrefabPath);

        Scene scene = EditorSceneManager.OpenScene(
            IslandScenePath,
            OpenSceneMode.Single
        );

        int updatedMarkers = 0;

        foreach (SeededSpawnMarker2D marker in scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<SeededSpawnMarker2D>(true))
            .Where(marker =>
                marker.Category == SeededContentCategory.Enemy))
        {
            SerializedObject serialized = new SerializedObject(marker);
            SerializedProperty prefabs =
                serialized.FindProperty("networkPrefabs");

            List<UnityEngine.Object> current = Enumerable
                .Range(0, prefabs.arraySize)
                .Select(index => prefabs
                    .GetArrayElementAtIndex(index).objectReferenceValue)
                .Where(value => value != null)
                .ToList();

            bool changed = false;

            foreach (GameObject candidate in new[] { skeleton, orc })
            {
                if (candidate != null && !current.Contains(candidate))
                {
                    current.Add(candidate);
                    changed = true;
                }
            }

            if (!changed)
            {
                continue;
            }

            prefabs.arraySize = current.Count;
            for (int index = 0; index < current.Count; index++)
            {
                prefabs.GetArrayElementAtIndex(index).objectReferenceValue =
                    current[index];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            updatedMarkers++;
        }

        Debug.Log(
            $"[Fixup] Added skeleton/orc to {updatedMarkers} island enemy " +
            "markers."
        );

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private const string CoconutPrefabPath =
        GameplayPrefabFolder + "/NetworkFoodPickup_Coconut.prefab";

    private const string GeneratedNetworkPrefabsPath =
        "Assets/DefaultNetworkPrefabs.asset";
    private const string BootstrapSettingsPath =
        "Assets/DeadmansTales/Resources/Networking/" +
        "DeadmansNetworkBootstrapSettings.asset";

    // Beach prop tile index 116 is a single whole coconut, from the same
    // catalog IslandStageBuilder already scatters as decoration.
    private const string CoconutTileSourcePath =
        "Assets/DeadmansTales/Art_Pixel/Tiles/BeachPropTiles/" +
        "tf_beach_tileB_116.asset";

    private static void AddCoconutFoodToIslandMarkers()
    {
        GameObject coconut = CreateCoconutFoodPrefab();

        RegisterNetworkPrefabs(new[] { coconut });

        Scene scene = EditorSceneManager.OpenScene(
            IslandScenePath,
            OpenSceneMode.Single
        );

        int updatedMarkers = 0;

        foreach (SeededSpawnMarker2D marker in scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<SeededSpawnMarker2D>(true))
            .Where(marker =>
                marker.Category == SeededContentCategory.Healing))
        {
            SerializedObject serialized = new SerializedObject(marker);
            SerializedProperty prefabs =
                serialized.FindProperty("networkPrefabs");

            List<UnityEngine.Object> current = Enumerable
                .Range(0, prefabs.arraySize)
                .Select(index => prefabs
                    .GetArrayElementAtIndex(index).objectReferenceValue)
                .Where(value => value != null)
                .ToList();

            if (current.Contains(coconut))
            {
                continue;
            }

            current.Add(coconut);

            prefabs.arraySize = current.Count;
            for (int index = 0; index < current.Count; index++)
            {
                prefabs.GetArrayElementAtIndex(index).objectReferenceValue =
                    current[index];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            updatedMarkers++;
        }

        Debug.Log(
            $"[Fixup] Added coconut food to {updatedMarkers} island " +
            "healing markers."
        );

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static GameObject CreateCoconutFoodPrefab()
    {
        Tile sourceTile =
            AssetDatabase.LoadAssetAtPath<Tile>(CoconutTileSourcePath);

        if (sourceTile == null || sourceTile.sprite == null)
        {
            throw new InvalidOperationException(
                $"Coconut tile asset missing at {CoconutTileSourcePath}."
            );
        }

        GameObject root = new GameObject("NetworkFoodPickup_Coconut");

        try
        {
            root.AddComponent<NetworkObject>();

            CircleCollider2D collider =
                root.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = 0.4f;

            NetworkFoodPickup pickup =
                root.AddComponent<NetworkFoodPickup>();
            SetSerializedString(pickup, "foodName", "a Coconut");
            SetSerializedFloat(pickup, "healAmount", 20f);
            SetSerializedBool(pickup, "allowRepeatedInteraction", false);

            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);

            SpriteRenderer renderer =
                visual.AddComponent<SpriteRenderer>();
            renderer.sprite = sourceTile.sprite;
            renderer.sortingOrder = 15;

            PrefabUtility.SaveAsPrefabAsset(root, CoconutPrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(CoconutPrefabPath);
    }

    private static void RegisterNetworkPrefabs(GameObject[] prefabs)
    {
        NetworkPrefabsList generatedList =
            AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(
                GeneratedNetworkPrefabsPath
            );

        if (generatedList == null)
        {
            throw new InvalidOperationException(
                "DefaultNetworkPrefabs.asset was not found."
            );
        }

        foreach (GameObject prefab in prefabs)
        {
            if (prefab != null && !generatedList.Contains(prefab))
            {
                generatedList.Add(new NetworkPrefab
                {
                    Override = NetworkPrefabOverride.None,
                    Prefab = prefab,
                });
            }
        }

        EditorUtility.SetDirty(generatedList);

        DeadmansNetworkBootstrapSettings settings =
            AssetDatabase.LoadAssetAtPath<DeadmansNetworkBootstrapSettings>(
                BootstrapSettingsPath
            );

        if (settings == null)
        {
            throw new InvalidOperationException(
                "Bootstrap settings asset was not found."
            );
        }

        List<GameObject> additionalPrefabs = settings
            .AdditionalNetworkPrefabs
            .Where(prefab => prefab != null)
            .Distinct()
            .ToList();

        foreach (GameObject prefab in prefabs)
        {
            if (prefab != null && !additionalPrefabs.Contains(prefab))
            {
                additionalPrefabs.Add(prefab);
            }
        }

        SerializedObject settingsObject = new SerializedObject(settings);
        SerializedProperty additional =
            settingsObject.FindProperty("additionalNetworkPrefabs");

        additional.arraySize = additionalPrefabs.Count;
        for (int index = 0; index < additionalPrefabs.Count; index++)
        {
            additional.GetArrayElementAtIndex(index).objectReferenceValue =
                additionalPrefabs[index];
        }

        settingsObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(settings);
    }

    // ------------------------------------------------------------------
    // Island: camera zoom
    // ------------------------------------------------------------------

    private static void FixIslandCameraZoom()
    {
        Scene scene = EditorSceneManager.OpenScene(
            IslandScenePath,
            OpenSceneMode.Single
        );

        Camera camera = scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Camera>(true))
            .FirstOrDefault();

        if (camera == null)
        {
            Debug.LogWarning(
                "[Fixup] No camera found in the island scene to fix zoom."
            );
            return;
        }

        float previousSize = camera.orthographicSize;
        camera.orthographicSize = IslandCameraOrthographicSize;

        Debug.Log(
            "[Fixup] Island camera orthographic size " +
            $"{previousSize} -> {IslandCameraOrthographicSize} (now " +
            "matches Lobby_Island_2D and Boat_Gameplay_2D)."
        );

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    // ------------------------------------------------------------------
    // Boat scene repair
    // ------------------------------------------------------------------

    private static void FixBoatScene()
    {
        Scene scene = EditorSceneManager.OpenScene(
            BoatScenePath,
            OpenSceneMode.Single
        );

        // 1. Remove the fullscreen "PROTOTYPE COMPLETE" overlay canvas that
        //    hid the entire scene.
        GameObject prototypeOverlay = scene
            .GetRootGameObjects()
            .FirstOrDefault(root => root.name == "Prototype");

        if (prototypeOverlay != null)
        {
            UnityEngine.Object.DestroyImmediate(prototypeOverlay);
            Debug.Log("[Fixup] Removed the PROTOTYPE COMPLETE overlay.");
        }

        // 2. The ship tilemap cannot render without a Grid.
        GameObject shipProp = FindSceneObject(scene, "Ship_prop");

        if (shipProp == null)
        {
            throw new InvalidOperationException(
                "Ship_prop was not found in the boat scene."
            );
        }

        if (shipProp.GetComponent<Grid>() == null)
        {
            Grid grid = shipProp.AddComponent<Grid>();
            grid.cellSize = new Vector3(1f, 1f, 0f);
            Debug.Log("[Fixup] Added the missing Grid to Ship_prop.");
        }

        Tilemap shipTilemap = shipProp.GetComponent<Tilemap>();
        shipTilemap.CompressBounds();

        FillShipDeckGaps(shipTilemap);
        shipTilemap.CompressBounds();

        Bounds deckBounds = shipTilemap.localBounds;
        Vector3 deckCenter =
            shipProp.transform.TransformPoint(deckBounds.center);

        Debug.Log(
            $"[Fixup] Ship deck center: {deckCenter}, " +
            $"size: {deckBounds.size}."
        );

        // 3. Move the ship survival stations onto the actual deck.
        GameObject survivalRoot = FindSceneObject(scene, "ShipSurvival");

        if (survivalRoot != null)
        {
            survivalRoot.transform.position = deckCenter;

            float horizontalReach =
                Mathf.Max(1.2f, deckBounds.extents.x * 0.55f);
            float verticalReach =
                Mathf.Max(0.8f, deckBounds.extents.y * 0.4f);

            Transform repair =
                survivalRoot.transform.Find("ShipRepairStation");
            if (repair != null)
            {
                repair.localPosition =
                    new Vector3(0f, -verticalReach, 0f);
            }

            Vector3[] leakOffsets =
            {
                new Vector3(-horizontalReach, -verticalReach * 0.5f, 0f),
                new Vector3(horizontalReach, -verticalReach * 0.5f, 0f),
                new Vector3(0f, verticalReach, 0f),
            };

            for (int index = 0; index < leakOffsets.Length; index++)
            {
                Transform leak = survivalRoot.transform.Find(
                    $"ShipLeak_{index:D2}"
                );

                if (leak != null)
                {
                    leak.localPosition = leakOffsets[index];
                }
            }
        }

        // 4. Put player spawns on the deck when they are off the ship.
        GameObject spawnRoot = FindSceneObject(scene, "PlayerSpawns");

        if (spawnRoot != null)
        {
            Bounds worldDeck = new Bounds(deckCenter, deckBounds.size);
            bool anyOutside = spawnRoot
                .GetComponentsInChildren<Transform>(true)
                .Where(child => child.name.StartsWith("PlayerSpawn_"))
                .Any(child => !worldDeck.Contains(
                    new Vector3(
                        child.position.x,
                        child.position.y,
                        worldDeck.center.z
                    )
                ));

            if (anyOutside)
            {
                Transform[] spawns = spawnRoot
                    .GetComponentsInChildren<Transform>(true)
                    .Where(child => child.name.StartsWith("PlayerSpawn_"))
                    .OrderBy(child => child.name, StringComparer.Ordinal)
                    .ToArray();

                for (int index = 0; index < spawns.Length; index++)
                {
                    float offset = (index - (spawns.Length - 1) * 0.5f);
                    spawns[index].position = deckCenter +
                        new Vector3(offset * 0.9f, -0.4f, 0f);
                }

                Debug.Log(
                    $"[Fixup] Moved {spawns.Length} player spawns onto " +
                    "the deck."
                );
            }
        }

        // 5. The old HUD died with the overlay canvas; rebuild it on its
        //    own dedicated canvas. Clear any stragglers first so reruns
        //    never leave duplicates.
        GameObject existingHudCanvas = scene
            .GetRootGameObjects()
            .FirstOrDefault(root => root.name == "GameHUDCanvas");

        if (existingHudCanvas != null)
        {
            UnityEngine.Object.DestroyImmediate(existingHudCanvas);
        }

        GameObject strandedHud;
        while ((strandedHud = FindSceneObject(scene, "ShipHealthHUD")) != null)
        {
            UnityEngine.Object.DestroyImmediate(strandedHud);
        }

        BuildHudCanvas();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private const string ShipDeckFloorTilePath =
        "Assets/DeadmansTales/Art_Pixel/2DShip/tileset/" +
        "tf_ship_tileA5_interior_17.asset";

    // The most-used-tile heuristic previously here picked a vertical mast
    // segment from the props sheet (tf_ship_tileB), which blanketed the
    // deck in repeated wooden poles instead of flooring. This is a single,
    // visually-verified flat plank tile from the ship's own interior sheet
    // instead.
    private static readonly string[] LegacyBadFillTileNames =
    {
        "tf_ship_tileB_225",
    };

    /// <summary>
    /// The teammate's ship layout only paints masts, sails, and a handful
    /// of props, leaving most of the deck's cells empty (open water shows
    /// through), so the "ship" reads as scattered debris instead of a hull.
    /// Fills every empty cell within the painted area's bounds with a
    /// single verified flat plank floor tile so the deck reads as one
    /// continuous surface. Also removes any cells a previous run filled
    /// with the wrong (mast-segment) tile.
    /// </summary>
    private static void FillShipDeckGaps(Tilemap shipTilemap)
    {
        BoundsInt bounds = shipTilemap.cellBounds;

        int clearedCount = 0;

        for (int x = bounds.xMin; x < bounds.xMax; x++)
        {
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                Vector3Int cell = new Vector3Int(x, y, 0);
                TileBase tile = shipTilemap.GetTile(cell);

                if (
                    tile != null &&
                    LegacyBadFillTileNames.Contains(tile.name)
                )
                {
                    shipTilemap.SetTile(cell, null);
                    clearedCount++;
                }
            }
        }

        if (clearedCount > 0)
        {
            Debug.Log(
                $"[Fixup] Cleared {clearedCount} deck cells that a " +
                "previous run filled with the wrong tile."
            );
        }

        TileBase floorTile = AssetDatabase.LoadAssetAtPath<TileBase>(
            ShipDeckFloorTilePath
        );

        if (floorTile == null)
        {
            Debug.LogWarning(
                "[Fixup] Deck floor tile is missing at " +
                $"{ShipDeckFloorTilePath}; leaving deck gaps as-is."
            );
            return;
        }

        int filledCount = 0;

        for (int x = bounds.xMin; x < bounds.xMax; x++)
        {
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                Vector3Int cell = new Vector3Int(x, y, 0);

                if (shipTilemap.GetTile(cell) != null)
                {
                    continue;
                }

                shipTilemap.SetTile(cell, floorTile);
                filledCount++;
            }
        }

        Debug.Log(
            $"[Fixup] Filled {filledCount} empty deck cells with " +
            $"'{floorTile.name}' so the hull reads as one solid surface."
        );
    }

    private static void BuildHudCanvas()
    {
        GameObject canvasObject = new GameObject(
            "GameHUDCanvas",
            typeof(RectTransform)
        );

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject hudRoot = new GameObject(
            "ShipHealthHUD",
            typeof(RectTransform)
        );
        hudRoot.transform.SetParent(canvasObject.transform, false);

        RectTransform hudRect = hudRoot.GetComponent<RectTransform>();
        hudRect.anchorMin = new Vector2(0.5f, 1f);
        hudRect.anchorMax = new Vector2(0.5f, 1f);
        hudRect.pivot = new Vector2(0.5f, 1f);
        hudRect.anchoredPosition = new Vector2(0f, -18f);
        hudRect.sizeDelta = new Vector2(340f, 48f);

        GameObject sliderObject = new GameObject(
            "HullBar",
            typeof(RectTransform)
        );
        sliderObject.transform.SetParent(hudRoot.transform, false);
        RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(0f, 0f);
        sliderRect.anchorMax = new Vector2(1f, 0f);
        sliderRect.pivot = new Vector2(0.5f, 0f);
        sliderRect.anchoredPosition = Vector2.zero;
        sliderRect.sizeDelta = new Vector2(0f, 18f);

        Image background = sliderObject.AddComponent<Image>();
        background.color = new Color(0.08f, 0.09f, 0.12f, 0.85f);

        GameObject fillArea = new GameObject(
            "FillArea",
            typeof(RectTransform)
        );
        fillArea.transform.SetParent(sliderObject.transform, false);
        RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
        fillAreaRect.anchorMin = Vector2.zero;
        fillAreaRect.anchorMax = Vector2.one;
        fillAreaRect.offsetMin = new Vector2(2f, 2f);
        fillAreaRect.offsetMax = new Vector2(-2f, -2f);

        GameObject fill = new GameObject("Fill", typeof(RectTransform));
        fill.transform.SetParent(fillArea.transform, false);
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        Image fillImage = fill.AddComponent<Image>();
        fillImage.color = new Color(0.78f, 0.34f, 0.2f, 1f);

        Slider slider = sliderObject.AddComponent<Slider>();
        slider.interactable = false;
        slider.transition = Selectable.Transition.None;
        slider.fillRect = fillRect;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 1f;

        GameObject labelObject = new GameObject(
            "Label",
            typeof(RectTransform)
        );
        labelObject.transform.SetParent(hudRoot.transform, false);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.offsetMin = new Vector2(0f, 20f);
        labelRect.offsetMax = Vector2.zero;

        Text label = labelObject.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 20;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.text = "Ship";

        DeadmansTales.UI.ShipHealthHUD hud =
            hudRoot.AddComponent<DeadmansTales.UI.ShipHealthHUD>();
        SetSerializedObject(hud, "healthSlider", slider);
        SetSerializedObject(hud, "label", label);
    }

    // ------------------------------------------------------------------
    // Shared helpers (same conventions as the other builders)
    // ------------------------------------------------------------------

    private static void ConfigureGridSheet(
        string assetPath,
        int frameSize,
        int pixelsPerUnit
    )
    {
        TextureImporter importer =
            (TextureImporter)AssetImporter.GetAtPath(assetPath);

        if (importer == null)
        {
            throw new InvalidOperationException(
                $"Missing sprite sheet: {assetPath}"
            );
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = pixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;

        importer.GetSourceTextureWidthAndHeight(
            out int width,
            out int height
        );

        int columns = width / frameSize;
        int rows = height / frameSize;
        string baseName = Path.GetFileNameWithoutExtension(assetPath);

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        ISpriteEditorDataProvider provider =
            factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();

        List<SpriteRect> spriteRects = new List<SpriteRect>();
        List<SpriteNameFileIdPair> nameFileIdPairs =
            new List<SpriteNameFileIdPair>();

        int frameIndex = 0;
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                string spriteName = $"{baseName}_{frameIndex}";
                GUID spriteId = DeterministicGuid(assetPath + spriteName);

                spriteRects.Add(
                    new SpriteRect
                    {
                        name = spriteName,
                        spriteID = spriteId,
                        rect = new Rect(
                            column * frameSize,
                            height - (row + 1) * frameSize,
                            frameSize,
                            frameSize
                        ),
                        // Bottom-anchored so every animation frame shares a
                        // fixed ground-contact point instead of visibly
                        // bobbing between poses of different drawn height.
                        alignment = SpriteAlignment.BottomCenter,
                        pivot = new Vector2(0.5f, 0f),
                    }
                );
                nameFileIdPairs.Add(
                    new SpriteNameFileIdPair(spriteName, spriteId)
                );
                frameIndex++;
            }
        }

        provider.SetSpriteRects(spriteRects.ToArray());

        ISpriteNameFileIdDataProvider nameProvider =
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        nameProvider.SetNameFileIdPairs(nameFileIdPairs);

        provider.Apply();
        importer.SaveAndReimport();
    }

    private static GUID DeterministicGuid(string seed)
    {
        using MD5 md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(seed));
        StringBuilder builder = new StringBuilder(32);
        foreach (byte value in hash)
        {
            builder.Append(value.ToString("x2"));
        }

        GUID.TryParse(builder.ToString(), out GUID result);
        return result;
    }

    private static Sprite[] LoadFrames(string assetPath)
    {
        Sprite[] frames = AssetDatabase
            .LoadAllAssetRepresentationsAtPath(assetPath)
            .OfType<Sprite>()
            .OrderBy(sprite => FrameNumber(sprite.name))
            .ToArray();

        if (frames.Length == 0)
        {
            throw new InvalidOperationException(
                $"No sprite frames available at {assetPath}."
            );
        }

        return frames;
    }

    private static int FrameNumber(string spriteName)
    {
        int underscore = spriteName.LastIndexOf('_');
        return int.Parse(spriteName.Substring(underscore + 1));
    }

    private static AnimationClip CreateSpriteClip(
        string assetPath,
        Sprite[] frames,
        float framesPerSecond,
        bool loop
    )
    {
        AnimationClip clip =
            AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);

        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, assetPath);
        }

        clip.frameRate = framesPerSecond;

        EditorCurveBinding binding = new EditorCurveBinding
        {
            type = typeof(SpriteRenderer),
            path = string.Empty,
            propertyName = "m_Sprite",
        };

        ObjectReferenceKeyframe[] keyframes =
            new ObjectReferenceKeyframe[frames.Length];

        for (int index = 0; index < frames.Length; index++)
        {
            keyframes[index] = new ObjectReferenceKeyframe
            {
                time = index / framesPerSecond,
                value = frames[index],
            };
        }

        AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);

        AnimationClipSettings settings =
            AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        EditorUtility.SetDirty(clip);
        return clip;
    }

    private static Transform FindDeepChild(Transform parent, string name)
    {
        return parent
            .GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(child => child.name == name);
    }

    private static GameObject FindSceneObject(Scene scene, string name)
    {
        return scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Where(candidate => candidate.name == name)
            .Select(candidate => candidate.gameObject)
            .FirstOrDefault();
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
        {
            return;
        }

        string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
        string leaf = Path.GetFileName(folder);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    private static void SetSerializedObject(
        UnityEngine.Object target,
        string propertyName,
        UnityEngine.Object value
    )
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null)
        {
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void SetSerializedString(
        UnityEngine.Object target,
        string propertyName,
        string value
    )
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null)
        {
            property.stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void SetSerializedFloat(
        UnityEngine.Object target,
        string propertyName,
        float value
    )
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null)
        {
            property.floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void SetSerializedBool(
        UnityEngine.Object target,
        string propertyName,
        bool value
    )
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null)
        {
            property.boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
