using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the kraken boss-arena prototype from art the team already owns.
///
/// EXPERIMENT ONLY. This lives on the experiment/kraken-crop-test branch and
/// touches nothing on main. It is deliberately built the same way as
/// VoyageLoopBuilder -- idempotent editor steps, runnable headless -- so it can
/// be thrown away or re-run cleanly.
///
/// The art:
///   - kraken_idle_0/1/2 : the front-facing 3-frame idle from the owned Time
///     Fantasy "Mythical Monsters" kraken (boss_kraken_1), hue-shifted to the
///     purple of Shay's concept with the eyes protected. 16 PPU to match tiles.
///   - arena_night_water : parallax_water_b tone-mapped to night.
///   - arena_whirlpool   : the polished whirlpool crop.
/// </summary>
public static class KrakenArenaBuilder
{
    private const string ArtDir =
        "Assets/DeadmansTales/Art_Pixel/KrakenArena";
    private const string PrefabDir =
        "Assets/DeadmansTales/Prefabs/KrakenArena";
    private const string AnimDir =
        "Assets/DeadmansTales/Animations/KrakenArena";

    private const float ArenaPixelsPerUnit = 16f;

    private const string BoatScenePath =
        "Assets/DeadmansTales/Scenes/Boat/Boat_Gameplay_2D.unity";
    private const string ArenaScenePath =
        "Assets/DeadmansTales/Scenes/Boat/Kraken_Arena_2D.unity";

    // North of the ship, past the fore cannons, within cannonball reach.
    private static readonly Vector3 KrakenPosition = new Vector3(0f, 26f, 0f);

    private static readonly string[] KrakenFrames =
    {
        ArtDir + "/kraken_idle_0.png",
        ArtDir + "/kraken_idle_1.png",
        ArtDir + "/kraken_idle_2.png",
    };
    private static readonly string WaterPath = ArtDir + "/arena_night_water.png";
    private static readonly string WhirlPath = ArtDir + "/arena_whirlpool.png";

    [MenuItem("Deadman's Tales/Kraken Arena/1. Import Art")]
    public static void ImportArenaArt()
    {
        foreach (string path in KrakenFrames)
        {
            ApplySpriteImport(path, TextureWrapMode.Clamp);
        }

        ApplySpriteImport(WhirlPath, TextureWrapMode.Clamp);
        // Water tiles, so it must wrap.
        ApplySpriteImport(WaterPath, TextureWrapMode.Repeat);

        AssetDatabase.Refresh();
        Debug.Log("[Kraken Arena] Art imported at 16 PPU, point-filtered.");
    }

    private static void ApplySpriteImport(string path, TextureWrapMode wrap)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

        if (importer == null)
        {
            Debug.LogError($"[Kraken Arena] No importer for '{path}'.");
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = ArenaPixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.wrapMode = wrap;
        importer.SaveAndReimport();
    }

    [MenuItem("Deadman's Tales/Kraken Arena/2. Build Kraken Prefab")]
    public static void BuildKrakenPrefab()
    {
        Directory.CreateDirectory(PrefabDir);
        Directory.CreateDirectory(AnimDir);

        Sprite[] frames = new Sprite[KrakenFrames.Length];
        for (int i = 0; i < KrakenFrames.Length; i++)
        {
            frames[i] = AssetDatabase.LoadAssetAtPath<Sprite>(KrakenFrames[i]);
            if (frames[i] == null)
            {
                Debug.LogError(
                    $"[Kraken Arena] Frame '{KrakenFrames[i]}' did not import " +
                    "as a Sprite. Run step 1 first.");
                return;
            }
        }

        // --- idle animation clip: 3 frames, looping, ~6 fps for a slow menace
        AnimationClip clip = new AnimationClip { frameRate = 6f };
        EditorCurveBinding binding = new EditorCurveBinding
        {
            type = typeof(SpriteRenderer),
            path = string.Empty,
            propertyName = "m_Sprite",
        };
        ObjectReferenceKeyframe[] keys =
            new ObjectReferenceKeyframe[frames.Length];
        for (int i = 0; i < frames.Length; i++)
        {
            keys[i] = new ObjectReferenceKeyframe
            {
                time = i / clip.frameRate,
                value = frames[i],
            };
        }
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

        AnimationClipSettings settings =
            AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        string clipPath = AnimDir + "/KrakenIdle.anim";
        AssetDatabase.CreateAsset(clip, clipPath);

        string controllerPath = AnimDir + "/Kraken.controller";
        AnimatorController controller =
            AnimatorController.CreateAnimatorControllerAtPathWithClip(
                controllerPath, clip);

        // --- prefab: SpriteRenderer + Animator + KrakenHealth
        GameObject root = new GameObject("Kraken");
        SpriteRenderer sr = root.AddComponent<SpriteRenderer>();
        sr.sprite = frames[0];
        sr.sortingOrder = 10;

        Animator animator = root.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;

        root.AddComponent<KrakenHealth>();

        // A generous body trigger so cannonballs register hits.
        CircleCollider2D body = root.AddComponent<CircleCollider2D>();
        body.isTrigger = true;
        body.radius = 2.6f;

        string prefabPath = PrefabDir + "/Kraken.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Kraken Arena] Built {prefabPath} with a 3-frame idle loop.");
    }

    [MenuItem("Deadman's Tales/Kraken Arena/3. Build Arena Scene")]
    public static void BuildArenaScene()
    {
        GameObject krakenPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabDir + "/Kraken.prefab");

        if (krakenPrefab == null)
        {
            Debug.LogError(
                "[Kraken Arena] No Kraken prefab; run step 2 first.");
            return;
        }

        // Open the working boat scene and Save-As a new arena scene, so the
        // ship, cannons, helm, spawns and networking all come for free and the
        // original boat scene on disk is never touched.
        Scene scene = EditorSceneManager.OpenScene(
            BoatScenePath, OpenSceneMode.Single);

        // Drop the boss in north of the ship, if not already there.
        bool krakenPresent = scene
            .GetRootGameObjects()
            .Any(go => go.GetComponentInChildren<KrakenHealth>() != null);

        if (!krakenPresent)
        {
            GameObject kraken =
                (GameObject)PrefabUtility.InstantiatePrefab(krakenPrefab);
            kraken.transform.position = KrakenPosition;
        }

        SwapWaterToNight();

        bool ok = EditorSceneManager.SaveScene(scene, ArenaScenePath);
        Debug.Log(ok
            ? $"[Kraken Arena] Saved playable arena to {ArenaScenePath}."
            : "[Kraken Arena] FAILED to save the arena scene.");

        RegisterArenaScene();
    }

    // Repoint the ship scene's scrolling water at the night tile, so the arena
    // reads as a boss fight rather than the daytime sail. Only the sprite is
    // swapped; the existing tiling/scroll setup is left alone.
    private static void SwapWaterToNight()
    {
        Sprite night = AssetDatabase.LoadAssetAtPath<Sprite>(WaterPath);
        if (night == null)
        {
            return;
        }

        ScrollingWater[] waters = Object
            .FindObjectsByType<ScrollingWater>(FindObjectsSortMode.None);

        foreach (ScrollingWater water in waters)
        {
            SpriteRenderer sr = water.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.sprite = night;
            }
        }
    }

    private static void RegisterArenaScene()
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == ArenaScenePath))
        {
            return;
        }

        scenes.Add(new EditorBuildSettingsScene(ArenaScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        Debug.Log("[Kraken Arena] Arena scene added to Build Settings.");
    }

    // One-shot: run all three steps in order.
    public static void BuildAllFromCommandLine()
    {
        ImportArenaArt();
        BuildKrakenPrefab();
        BuildArenaScene();
    }
}
