using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Imports the hand-picked crab / skeleton / chest sprite art, slices the
/// sheets, generates animation clips and animator controllers, and upgrades
/// the enemy and chest prefabs from tinted placeholders to real animated art.
///
/// Idempotent: safe to run repeatedly.
/// </summary>
public static class EnemyAndChestArtBuilder
{
    private const string MenuPath =
        "Deadman's Tales/Art/Build Enemy And Chest Art";

    private const string CharacterArtFolder =
        "Assets/DeadmansTales/Art_Pixel/Characters";
    private const string SkeletonArtFolder =
        CharacterArtFolder + "/SkeletonWarrior";
    private const string CrabSheetPath =
        CharacterArtFolder + "/crab_sheet.png";
    private const string ChestSheetPath =
        "Assets/DeadmansTales/Art_Pixel/Props/rpg_chests.png";

    private const string AnimationFolder =
        "Assets/DeadmansTales/Animations";
    private const string SkeletonAnimationFolder =
        AnimationFolder + "/SkeletonWarrior2D";
    private const string CrabAnimationFolder =
        AnimationFolder + "/Crab2D";

    private const string GameplayPrefabFolder =
        "Assets/DeadmansTales/Prefabs/Gameplay";
    private const string BasicEnemyPrefabPath =
        "Assets/DeadmansTales/Prefabs/basicenemy.prefab";
    private const string SkeletonWarriorPrefabPath =
        GameplayPrefabFolder + "/Enemy_SkeletonWarrior.prefab";
    private const string CrabSkitterPrefabPath =
        GameplayPrefabFolder + "/Enemy_CrabSkitter.prefab";
    private const string RewardChestPrefabPath =
        GameplayPrefabFolder + "/NetworkRewardChest.prefab";
    private const string WeaponChestPrefabPath =
        GameplayPrefabFolder + "/NetworkRewardChest_Weapon.prefab";
    private const string UpgradeChestPrefabPath =
        GameplayPrefabFolder + "/NetworkRewardChest_Upgrade.prefab";

    private const int SkeletonFrameSize = 96;
    private const int SkeletonPixelsPerUnit = 48;
    private const int CrabFrameSize = 32;
    private const int CrabPixelsPerUnit = 24;
    private const int ChestFrameSize = 32;
    private const int ChestPixelsPerUnit = 26;

    [MenuItem(MenuPath)]
    public static void BuildAll()
    {
        ConfigureSkeletonSheets();
        ConfigureGridSheet(CrabSheetPath, CrabFrameSize, CrabPixelsPerUnit);
        ConfigureGridSheet(ChestSheetPath, ChestFrameSize, ChestPixelsPerUnit);
        AssetDatabase.Refresh();

        BuildSkeletonAnimations();
        BuildCrabAnimations();

        BuildSkeletonWarriorPrefab();
        UpgradeCrabSkitterPrefab();
        UpgradeChestPrefabVisuals();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "[Art Builder] Skeleton warrior, crab, and chest art are built " +
            "and wired into their prefabs."
        );
    }

    public static void BuildAllFromCommandLine()
    {
        BuildAll();
    }

    // ------------------------------------------------------------------
    // Sprite sheet import + slicing
    // ------------------------------------------------------------------

    private static void ConfigureSkeletonSheets()
    {
        foreach (string sheetPath in
            Directory.GetFiles(
                SkeletonArtFolder,
                "*.png",
                SearchOption.AllDirectories
            ))
        {
            string assetPath = sheetPath.Replace('\\', '/');
            if (assetPath.EndsWith("All-spritesheet.png"))
            {
                continue;
            }

            ConfigureGridSheet(
                assetPath,
                SkeletonFrameSize,
                SkeletonPixelsPerUnit
            );
        }
    }

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

        // Read source size from the importer platform settings.
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
                        alignment = SpriteAlignment.Center,
                        pivot = new Vector2(0.5f, 0.5f),
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
        return AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath)
            .OfType<Sprite>()
            .OrderBy(sprite => FrameNumber(sprite.name))
            .ToArray();
    }

    private static int FrameNumber(string spriteName)
    {
        int underscore = spriteName.LastIndexOf('_');
        return int.Parse(spriteName.Substring(underscore + 1));
    }

    // ------------------------------------------------------------------
    // Animation clips + controllers
    // ------------------------------------------------------------------

    private static void BuildSkeletonAnimations()
    {
        EnsureFolder(SkeletonAnimationFolder);

        AnimationClip idle = CreateSpriteClip(
            SkeletonAnimationFolder + "/Skeleton_Idle.anim",
            LoadFrames(SkeletonArtFolder + "/Idle/Idle.png"),
            6f,
            true
        );
        AnimationClip walk = CreateSpriteClip(
            SkeletonAnimationFolder + "/Skeleton_Walk.anim",
            LoadFrames(SkeletonArtFolder + "/Walk/Walk.png"),
            10f,
            true
        );
        AnimationClip attack = CreateSpriteClip(
            SkeletonAnimationFolder + "/Skeleton_Attack.anim",
            LoadFrames(SkeletonArtFolder + "/Attack/Attack1.png"),
            14f,
            false
        );
        AnimationClip hurt = CreateSpriteClip(
            SkeletonAnimationFolder + "/Skeleton_Hurt.anim",
            LoadFrames(SkeletonArtFolder + "/Hurt/Hurt.png"),
            12f,
            false
        );
        AnimationClip death = CreateSpriteClip(
            SkeletonAnimationFolder + "/Skeleton_Death.anim",
            LoadFrames(SkeletonArtFolder + "/Death/Death.png"),
            10f,
            false
        );

        BuildEnemyController(
            SkeletonAnimationFolder + "/SkeletonWarrior2D.controller",
            idle,
            walk,
            attack,
            hurt,
            death
        );
    }

    private static void BuildCrabAnimations()
    {
        EnsureFolder(CrabAnimationFolder);

        Sprite[] allFrames = LoadFrames(CrabSheetPath);
        Sprite[] walkFrames = allFrames.Take(4).ToArray();
        Sprite[] attackFrames = allFrames.Skip(12).Take(4).ToArray();

        AnimationClip idle = CreateSpriteClip(
            CrabAnimationFolder + "/Crab_Idle.anim",
            walkFrames.Take(2).ToArray(),
            3f,
            true
        );
        AnimationClip walk = CreateSpriteClip(
            CrabAnimationFolder + "/Crab_Walk.anim",
            walkFrames,
            10f,
            true
        );
        AnimationClip attack = CreateSpriteClip(
            CrabAnimationFolder + "/Crab_Attack.anim",
            attackFrames,
            12f,
            false
        );

        BuildEnemyController(
            CrabAnimationFolder + "/Crab2D.controller",
            idle,
            walk,
            attack,
            null,
            null
        );
    }

    private static AnimationClip CreateSpriteClip(
        string assetPath,
        Sprite[] frames,
        float framesPerSecond,
        bool loop
    )
    {
        if (frames == null || frames.Length == 0)
        {
            throw new InvalidOperationException(
                $"No sprite frames available for {assetPath}."
            );
        }

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

    private static void BuildEnemyController(
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
        controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine stateMachine =
            controller.layers[0].stateMachine;

        AnimatorState idleState = stateMachine.AddState("Idle");
        idleState.motion = idle;
        stateMachine.defaultState = idleState;

        AnimatorState walkState = stateMachine.AddState("Walk");
        walkState.motion = walk;

        AnimatorStateTransition toWalk =
            idleState.AddTransition(walkState);
        toWalk.hasExitTime = false;
        toWalk.duration = 0f;
        toWalk.AddCondition(AnimatorConditionMode.Greater, 0.15f, "Speed");

        AnimatorStateTransition toIdle =
            walkState.AddTransition(idleState);
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

        if (hurt != null)
        {
            controller.AddParameter(
                "Hurt",
                AnimatorControllerParameterType.Trigger
            );

            AnimatorState hurtState = stateMachine.AddState("Hurt");
            hurtState.motion = hurt;

            AnimatorStateTransition anyToHurt =
                stateMachine.AddAnyStateTransition(hurtState);
            anyToHurt.hasExitTime = false;
            anyToHurt.duration = 0f;
            anyToHurt.canTransitionToSelf = false;
            anyToHurt.AddCondition(AnimatorConditionMode.If, 0f, "Hurt");

            AnimatorStateTransition hurtDone =
                hurtState.AddTransition(idleState);
            hurtDone.hasExitTime = true;
            hurtDone.exitTime = 1f;
            hurtDone.duration = 0f;
        }

        if (death != null)
        {
            AnimatorState deathState = stateMachine.AddState("Die");
            deathState.motion = death;

            AnimatorStateTransition anyToDeath =
                stateMachine.AddAnyStateTransition(deathState);
            anyToDeath.hasExitTime = false;
            anyToDeath.duration = 0f;
            anyToDeath.canTransitionToSelf = false;
            anyToDeath.AddCondition(AnimatorConditionMode.If, 0f, "Die");
        }
    }

    // ------------------------------------------------------------------
    // Prefab wiring
    // ------------------------------------------------------------------

    private static void BuildSkeletonWarriorPrefab()
    {
        EnsurePrefabVariant(BasicEnemyPrefabPath, SkeletonWarriorPrefabPath);

        GameObject root =
            PrefabUtility.LoadPrefabContents(SkeletonWarriorPrefabPath);

        try
        {
            ConfigureEnemyVisual(
                root,
                LoadFrames(SkeletonArtFolder + "/Idle/Idle.png")[0],
                AssetDatabase.LoadAssetAtPath<AnimatorController>(
                    SkeletonAnimationFolder + "/SkeletonWarrior2D.controller"
                ),
                Color.white
            );

            root.transform.localScale = Vector3.one;
            SetSerializedField(root, typeof(Enemy), "maxHealth", 120f);
            SetChildSerializedFloat<EnemyAI>(root, "chaseSpeed", 2.6f);
            SetChildSerializedFloat<EnemyAI>(root, "wanderSpeed", 1.3f);
            SetChildSerializedFloat<EnemyAttack>(root, "damage", 12f);

            PrefabUtility.SaveAsPrefabAsset(root, SkeletonWarriorPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void UpgradeCrabSkitterPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(
                CrabSkitterPrefabPath) == null)
        {
            Debug.LogWarning(
                "[Art Builder] Enemy_CrabSkitter.prefab is missing; run " +
                "the island builder first."
            );
            return;
        }

        GameObject root =
            PrefabUtility.LoadPrefabContents(CrabSkitterPrefabPath);

        try
        {
            ConfigureEnemyVisual(
                root,
                LoadFrames(CrabSheetPath)[0],
                AssetDatabase.LoadAssetAtPath<AnimatorController>(
                    CrabAnimationFolder + "/Crab2D.controller"
                ),
                Color.white
            );

            PrefabUtility.SaveAsPrefabAsset(root, CrabSkitterPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureEnemyVisual(
        GameObject root,
        Sprite sprite,
        AnimatorController controller,
        Color tint
    )
    {
        Transform gfx = FindDeepChild(root.transform, "GFX");
        if (gfx == null)
        {
            throw new InvalidOperationException(
                $"Prefab {root.name} has no GFX child to reskin."
            );
        }

        SpriteRenderer renderer = gfx.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = tint;

        Animator animator = gfx.GetComponent<Animator>();
        if (animator == null)
        {
            animator = gfx.gameObject.AddComponent<Animator>();
        }

        animator.runtimeAnimatorController = controller;

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
    }

    private static void UpgradeChestPrefabVisuals()
    {
        Sprite[] chestSprites = LoadFrames(ChestSheetPath);
        if (chestSprites.Length < 36)
        {
            throw new InvalidOperationException(
                "The chest sheet did not slice into the expected 36 sprites."
            );
        }

        // Column layout in the RPG chest sheet (9 columns x 4 rows).
        // Row 0 holds the primary closed chests.
        UpgradeChestPrefab(
            RewardChestPrefabPath,
            chestSprites[2],
            new Color(0.75f, 1f, 0.78f, 1f)
        );
        UpgradeChestPrefab(
            WeaponChestPrefabPath,
            chestSprites[5],
            Color.white
        );
        UpgradeChestPrefab(
            UpgradeChestPrefabPath,
            chestSprites[4],
            Color.white
        );
    }

    private static void UpgradeChestPrefab(
        string prefabPath,
        Sprite closedSprite,
        Color tint
    )
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
        {
            Debug.LogWarning(
                $"[Art Builder] Missing chest prefab {prefabPath}; run the " +
                "island builder first."
            );
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);

        try
        {
            ReplaceChestVisual(
                root,
                "ClosedVisual",
                closedSprite,
                tint,
                true
            );
            ReplaceChestVisual(
                root,
                "OpenedVisual",
                closedSprite,
                new Color(0.45f, 0.45f, 0.45f, 0.8f),
                false
            );

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ReplaceChestVisual(
        GameObject root,
        string visualName,
        Sprite sprite,
        Color tint,
        bool activeByDefault
    )
    {
        Transform existing = root.transform.Find(visualName);
        if (existing != null)
        {
            UnityEngine.Object.DestroyImmediate(existing.gameObject);
        }

        GameObject visual = new GameObject(visualName);
        visual.transform.SetParent(root.transform, false);
        visual.transform.localScale = new Vector3(1.4f, 1.4f, 1f);
        visual.transform.localPosition = new Vector3(0f, 0.25f, 0f);
        visual.SetActive(activeByDefault);

        SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = tint;
        renderer.sortingOrder = 15;

        var chest = root.GetComponent<DeadmansTales.Networking.NetworkRewardChest>();
        SerializedObject serializedChest = new SerializedObject(chest);
        string property = visualName == "ClosedVisual"
            ? "closedVisual"
            : "openedVisual";
        serializedChest.FindProperty(property).objectReferenceValue = visual;
        serializedChest.ApplyModifiedPropertiesWithoutUndo();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

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
                $"Cannot create variant of missing prefab {sourcePath}."
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

    private static Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in
            parent.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == name)
            {
                return child;
            }
        }

        return null;
    }

    private static void SetSerializedField(
        GameObject root,
        Type componentType,
        string fieldName,
        float value
    )
    {
        Component component = root.GetComponent(componentType);
        if (component == null)
        {
            return;
        }

        SerializedObject serialized = new SerializedObject(component);
        serialized.FindProperty(fieldName).floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetChildSerializedFloat<T>(
        GameObject root,
        string fieldName,
        float value
    ) where T : Component
    {
        T component = root.GetComponentInChildren<T>(true);
        if (component == null)
        {
            return;
        }

        SerializedObject serialized = new SerializedObject(component);
        SerializedProperty property = serialized.FindProperty(fieldName);
        if (property == null)
        {
            return;
        }

        property.floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
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
}
