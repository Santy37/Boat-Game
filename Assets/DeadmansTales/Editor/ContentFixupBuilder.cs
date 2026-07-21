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

/// <summary>
/// Playtest-feedback fixup pass. The boat scene is deliberately NOT
/// touched anymore: it belongs to the teammate's branch and this branch
/// carries main's bytes for it verbatim, so merging stays clean.
///  - Crab/skeleton: hides the leftover placeholder sprite on the prefab
///    roots that rendered alongside the real art.
///  - Island: skeleton warriors and the orc join the enemy marker pools.
///  - Orc (BoneBrute): orc art, and the variant's legacy 1.25x root scale
///    is reset so the measured pixels-per-unit is the ONLY thing deciding
///    its size. The player prefab is the classic rig — not touched.
///
/// Idempotent: safe to run repeatedly.
/// </summary>
public static class ContentFixupBuilder
{
    private const string MenuPath =
        "Deadman's Tales/Island Content Fixup";

    private const string IslandScenePath =
        "Assets/DeadmansTales/Scenes/Island_After_Ocean_01_2D.unity";

    private const string TinyRpgFolder =
        "Assets/DeadmansTales/Art_Pixel/Characters/TinyRPG";

    private const string OrcFolder = TinyRpgFolder + "/Orc";
    private const string DemonFolder = TinyRpgFolder + "/DemonA";
    private const string BloodMonsterFolder =
        TinyRpgFolder + "/BloodMonsterA";

    private const string AnimationFolder =
        "Assets/DeadmansTales/Animations";
    private const string OrcAnimationFolder =
        AnimationFolder + "/OrcBrute2D";
    private const string DemonAnimationFolder =
        AnimationFolder + "/DemonReaver2D";
    private const string BloodMonsterAnimationFolder =
        AnimationFolder + "/BloodFiend2D";

    private const string BasicEnemyPrefabPath =
        "Assets/DeadmansTales/Prefabs/basicenemy.prefab";

    private const string GameplayPrefabFolder =
        "Assets/DeadmansTales/Prefabs/Gameplay";
    private const string CrabPrefabPath =
        GameplayPrefabFolder + "/Enemy_CrabSkitter.prefab";
    private const string SkeletonPrefabPath =
        GameplayPrefabFolder + "/Enemy_SkeletonWarrior.prefab";
    private const string BoneBrutePrefabPath =
        GameplayPrefabFolder + "/Enemy_BoneBrute.prefab";
    private const string DemonPrefabPath =
        GameplayPrefabFolder + "/Enemy_DemonReaver.prefab";
    private const string BloodFiendPrefabPath =
        GameplayPrefabFolder + "/Enemy_BloodFiend.prefab";

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
    /// world-space height.
    ///
    /// The anchor for every target below is the MEASURED player, and that
    /// measurement has a trap in it: the player prefab carries a 16x16
    /// placeholder sword icon on one child, but the sprite its animator
    /// actually renders is motw_10 on another — 68 drawn pixels at
    /// 32 px/unit, so the crew stand about **2.1 world units** tall.
    ///
    /// Sizing against the placeholder (as an earlier pass did) makes every
    /// enemy roughly half the height it should be, which is exactly how
    /// the skeleton ended up looking like a child next to the player.
    /// </summary>
    private const float PlayerWorldHeight = 2.13f;
    // Every target below is expressed as a multiple of the player so the
    // roster stays readable at a glance: a skeleton warrior is a shade
    // taller than a pirate, an orc is a head above that, and the demon
    // reaver is the thing you run from.
    private const float SkeletonTargetWorldHeight =
        PlayerWorldHeight * 1.05f;

    // The variant's legacy 1.25x root scale is reset in
    // UpgradeBoneBruteToOrc; with that gone this is the orc's real
    // on-screen height.
    private const float BruteTargetWorldHeight = PlayerWorldHeight * 1.25f;

    // A scuttling crab is knee-height; a chest reads as furniture.
    private const float CrabTargetWorldHeight = PlayerWorldHeight * 0.42f;
    private const float ChestTargetWorldHeight = PlayerWorldHeight * 0.55f;

    // The demon reaver is the elite (rarer, taller), the blood fiend a
    // fast low-health swarmer that comes up to the player's shoulder.
    private const float DemonTargetWorldHeight = PlayerWorldHeight * 1.5f;
    private const float BloodMonsterTargetWorldHeight =
        PlayerWorldHeight * 0.85f;

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

        // Orc sheets only: the player stays on the classic rig, so the
        // Soldier half of the pack is no longer sliced or animated here.
        AutoSliceCharacterFolder(
            OrcFolder,
            CharacterFrameSize,
            BruteTargetWorldHeight,
            new[] { "Orc" }
        );
        AutoSliceCharacterFolder(
            DemonFolder,
            CharacterFrameSize,
            DemonTargetWorldHeight,
            new[] { "Demon_A" }
        );
        AutoSliceCharacterFolder(
            BloodMonsterFolder,
            CharacterFrameSize,
            BloodMonsterTargetWorldHeight,
            new[] { "BloodMonster_A" }
        );

        // Re-slice the already-built skeleton, crab, and chest sheets at a
        // measured, correctly-scaled pixels-per-unit. This only changes
        // pixel density, not sprite names/GUIDs, so the animation clips
        // built earlier keep working without being rebuilt.
        AutoSliceCharacterFolder(
            SkeletonArtFolder,
            SkeletonFrameSize,
            SkeletonTargetWorldHeight,
            new[] { "All-spritesheet" }
        );
        ConfigureGridSheet(
            CrabSheetPath,
            CrabFrameSize,
            ComputeSpriteFit(
                CrabSheetPath,
                CrabFrameSize,
                CrabTargetWorldHeight
            )
        );
        ConfigureGridSheet(
            ChestSheetPath,
            ChestFrameSize,
            ComputeSpriteFit(
                ChestSheetPath,
                ChestFrameSize,
                ChestTargetWorldHeight
            )
        );

        AssetDatabase.Refresh();

        BuildOrcAnimations();
        BuildTinyRpgEnemyAnimations(
            "Demon_A",
            DemonFolder,
            DemonAnimationFolder,
            "DemonReaver2D"
        );
        BuildTinyRpgEnemyAnimations(
            "BloodMonster_A",
            BloodMonsterFolder,
            BloodMonsterAnimationFolder,
            "BloodFiend2D"
        );

        HideLegacyRootSprite(CrabPrefabPath);
        HideLegacyRootSprite(SkeletonPrefabPath);
        UpgradeBoneBruteToOrc();
        NormalizeEnemyRootScale(CrabPrefabPath);
        NormalizeEnemyRootScale(SkeletonPrefabPath);

        GameObject demon = BuildTinyRpgEnemyVariant(
            DemonPrefabPath,
            DemonFolder + "/Demon_A_Idle.png",
            DemonAnimationFolder + "/DemonReaver2D.controller",
            maxHealth: 240f,
            chaseSpeed: 2.4f,
            wanderSpeed: 1.1f,
            damage: 22f
        );
        GameObject bloodFiend = BuildTinyRpgEnemyVariant(
            BloodFiendPrefabPath,
            BloodMonsterFolder + "/BloodMonster_A_Idle.png",
            BloodMonsterAnimationFolder + "/BloodFiend2D.controller",
            maxHealth: 70f,
            chaseSpeed: 3.6f,
            wanderSpeed: 1.7f,
            damage: 8f
        );
        RegisterNetworkPrefabs(new[] { demon, bloodFiend });

        AddNewEnemiesToIslandMarkers();
        AddCoconutFoodToIslandMarkers();
        FixIslandCameraZoom();

        IslandPolishBuilder.BuildAllFromCommandLine();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "[Fixup Builder] Sprites recalibrated to the 2.13u player, " +
            "chest visuals deduplicated, demon + blood fiend added, " +
            "island polished. Boat scene untouched (teammate-owned)."
        );
    }

    public static void BuildAllFromCommandLine()
    {
        BuildAll();
    }

    // ------------------------------------------------------------------
    // Tiny RPG sheet slicing
    // ------------------------------------------------------------------

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

        SpriteFit fit = ComputeSpriteFit(
            referenceSheet,
            frameSize,
            targetWorldHeight
        );

        foreach (string sheet in sheets)
        {
            ConfigureGridSheet(sheet, frameSize, fit);
        }
    }

    /// <summary>
    /// Measures the non-transparent pixel height of the sheet's top-left
    /// frame directly from the source file (independent of any existing
    /// import settings) and returns the pixels-per-unit that makes that
    /// drawn height equal <paramref name="targetWorldHeight"/> world units.
    /// </summary>
    private readonly struct SpriteFit
    {
        public SpriteFit(int pixelsPerUnit, Vector2 pivot)
        {
            PixelsPerUnit = pixelsPerUnit;
            Pivot = pivot;
        }

        public int PixelsPerUnit { get; }

        /// <summary>
        /// Normalized custom pivot at the drawn art's feet — NOT the frame
        /// canvas bottom. These packs pad the character inside a much
        /// larger canvas, so a canvas-bottom pivot floats the visible body
        /// several units above the transform (health bar, collider, and
        /// attacks all live at the transform origin). Matching the original
        /// placeholder's feet-at-origin convention keeps everything aligned.
        /// </summary>
        public Vector2 Pivot { get; }
    }

    private static SpriteFit ComputeSpriteFit(
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

            // Within the frame band, y counts up from the frame's bottom,
            // so minY is the drawn art's lowest (feet) row.
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
            float pivotY = maxY >= minY
                ? minY / (float)frameSize
                : 0f;

            return new SpriteFit(
                Mathf.Max(
                    1,
                    Mathf.RoundToInt(trimmedHeight / targetWorldHeight)
                ),
                new Vector2(0.5f, pivotY)
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
    /// Same clip set as the orc for any Tiny RPG pack character: the packs
    /// share the 100px frame layout and animation naming.
    /// </summary>
    private static void BuildTinyRpgEnemyAnimations(
        string prefix,
        string artFolder,
        string animationFolder,
        string controllerName
    )
    {
        EnsureFolder(animationFolder);

        AnimationClip idle = CreateSpriteClip(
            animationFolder + $"/{prefix}_Idle.anim",
            LoadFrames(artFolder + $"/{prefix}_Idle.png"),
            6f,
            true
        );
        AnimationClip walk = CreateSpriteClip(
            animationFolder + $"/{prefix}_Walk.anim",
            LoadFrames(artFolder + $"/{prefix}_Walk.png"),
            10f,
            true
        );
        AnimationClip attack = CreateSpriteClip(
            animationFolder + $"/{prefix}_Attack.anim",
            LoadFrames(artFolder + $"/{prefix}_Attack01.png"),
            14f,
            false
        );
        AnimationClip hurt = CreateSpriteClip(
            animationFolder + $"/{prefix}_Hurt.anim",
            LoadFrames(artFolder + $"/{prefix}_Hurt.png"),
            12f,
            false
        );
        AnimationClip death = CreateSpriteClip(
            animationFolder + $"/{prefix}_Death.anim",
            LoadFrames(artFolder + $"/{prefix}_Death.png"),
            8f,
            false
        );

        BuildCharacterController(
            animationFolder + $"/{controllerName}.controller",
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

            // The variant carries a legacy 1.25x root-scale override from
            // its tinted-placeholder days. Stacked on the measured
            // pixels-per-unit it made the orc ~3.6 units tall; the sprite's
            // own import size must be the only thing deciding how big the
            // orc is.
            root.transform.localScale = Vector3.one;

            Transform gfx = FindDeepChild(root.transform, "GFX");
            if (gfx == null)
            {
                throw new InvalidOperationException(
                    "Enemy_BoneBrute has no GFX child."
                );
            }

            gfx.localScale = Vector3.one;

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

    /// <summary>
    /// Clears any legacy transform-scale multiplier on an enemy prefab so
    /// the sprite's measured pixels-per-unit is the only thing deciding
    /// its size (the crab variant shipped with a 0.85x root scale, the
    /// brute with 1.25x).
    /// </summary>
    private static void NormalizeEnemyRootScale(string prefabPath)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
        {
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);

        try
        {
            root.transform.localScale = Vector3.one;

            Transform gfx = FindDeepChild(root.transform, "GFX");
            if (gfx != null)
            {
                gfx.localScale = Vector3.one;
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// Creates (or refreshes) a basicenemy prefab variant skinned with a
    /// Tiny RPG character: real idle sprite, its animator controller, the
    /// motion animator driving Speed/facing, no leftover placeholder
    /// renderers, no transform-scale multipliers, and tuned combat stats.
    /// </summary>
    private static GameObject BuildTinyRpgEnemyVariant(
        string variantPath,
        string idleSheetPath,
        string controllerPath,
        float maxHealth,
        float chaseSpeed,
        float wanderSpeed,
        float damage
    )
    {
        EnsurePrefabVariant(BasicEnemyPrefabPath, variantPath);

        GameObject root = PrefabUtility.LoadPrefabContents(variantPath);

        try
        {
            root.transform.localScale = Vector3.one;

            DisableNonGfxSpriteRenderers(root);

            Transform gfx = FindDeepChild(root.transform, "GFX");
            if (gfx == null)
            {
                throw new InvalidOperationException(
                    $"{variantPath} has no GFX child."
                );
            }

            gfx.localScale = Vector3.one;

            SpriteRenderer renderer = gfx.GetComponent<SpriteRenderer>();
            renderer.sprite = LoadFrames(idleSheetPath)[0];
            renderer.color = Color.white;
            renderer.enabled = true;

            Animator animator = gfx.GetComponent<Animator>();
            if (animator == null)
            {
                animator = gfx.gameObject.AddComponent<Animator>();
            }

            animator.runtimeAnimatorController =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(
                    controllerPath
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

            Enemy enemy = root.GetComponent<Enemy>();
            if (enemy != null)
            {
                SetSerializedFloat(enemy, "maxHealth", maxHealth);
            }

            EnemyAI ai = root.GetComponentInChildren<EnemyAI>(true);
            if (ai != null)
            {
                SetSerializedFloat(ai, "chaseSpeed", chaseSpeed);
                SetSerializedFloat(ai, "wanderSpeed", wanderSpeed);
            }

            EnemyAttack attack =
                root.GetComponentInChildren<EnemyAttack>(true);
            if (attack != null)
            {
                SetSerializedFloat(attack, "damage", damage);
            }

            PrefabUtility.SaveAsPrefabAsset(root, variantPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(variantPath);
    }

    private static void EnsurePrefabVariant(
        string sourcePath,
        string variantPath
    )
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(variantPath) != null)
        {
            return;
        }

        GameObject source =
            AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);

        if (source == null)
        {
            throw new InvalidOperationException(
                $"Cannot create a variant of a missing prefab: {sourcePath}"
            );
        }

        GameObject instance =
            (GameObject)PrefabUtility.InstantiatePrefab(source);

        try
        {
            PrefabUtility.SaveAsPrefabAsset(instance, variantPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
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
        GameObject bloodFiend =
            AssetDatabase.LoadAssetAtPath<GameObject>(BloodFiendPrefabPath);
        GameObject demon =
            AssetDatabase.LoadAssetAtPath<GameObject>(DemonPrefabPath);

        Scene scene = EditorSceneManager.OpenScene(
            IslandScenePath,
            OpenSceneMode.Single
        );

        int updatedMarkers = 0;

        SeededSpawnMarker2D[] enemyMarkers = scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<SeededSpawnMarker2D>(true))
            .Where(marker =>
                marker.Category == SeededContentCategory.Enemy)
            .OrderBy(marker => marker.name, StringComparer.Ordinal)
            .ToArray();

        for (int markerIndex = 0;
            markerIndex < enemyMarkers.Length;
            markerIndex++)
        {
            SeededSpawnMarker2D marker = enemyMarkers[markerIndex];

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

            // Blood fiends swarm everywhere; the demon reaver is an elite
            // that only stalks every third camp so it stays scary.
            List<GameObject> wanted = new List<GameObject>
            {
                skeleton,
                orc,
                bloodFiend,
            };
            if (markerIndex % 3 == 0)
            {
                wanted.Add(demon);
            }

            foreach (GameObject candidate in wanted)
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
            "[Fixup] Enemy pools updated on " +
            $"{updatedMarkers}/{enemyMarkers.Length} island markers " +
            "(skeleton/orc/blood fiend everywhere, demon on every 3rd)."
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
    // Shared helpers (same conventions as the other builders)
    // ------------------------------------------------------------------

    private static void ConfigureGridSheet(
        string assetPath,
        int frameSize,
        SpriteFit fit
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
        importer.spritePixelsPerUnit = fit.PixelsPerUnit;
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
                        // Custom pivot at the measured feet of the drawn
                        // art (see SpriteFit.Pivot): keeps every animation
                        // frame on one fixed ground line AND keeps the
                        // visible body aligned with the transform origin
                        // where colliders, health bars, and attacks live.
                        alignment = SpriteAlignment.Custom,
                        pivot = fit.Pivot,
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
