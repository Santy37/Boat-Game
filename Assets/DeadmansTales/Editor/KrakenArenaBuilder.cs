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

    // Placed sprites (kraken, whirlpool) match the tile density at 16 PPU.
    private const float ArenaPixelsPerUnit = 16f;

    // The scrolling water is a special case: it must import at the SAME PPU as
    // the day-water it replaces (parallax_water_b, 32 PPU), because the
    // ScrollingWater component's tileSize/pixelsPerUnit are tuned for a 1-world-
    // unit tile. Import it at 16 and the sprite becomes a 2-unit tile while the
    // scroller still wraps at 1 unit -- the water snaps back every wrap, which
    // reads as the whole ship lurching forward and back.
    private const float WaterPixelsPerUnit = 32f;

    private const string BoatScenePath =
        "Assets/DeadmansTales/Scenes/Boat/Boat_Gameplay_2D.unity";
    private const string ArenaScenePath =
        "Assets/DeadmansTales/Scenes/Boat/Kraken_Arena_2D.unity";

    // Looming over the bow: base near the fore deck, rising north. Close enough
    // to see from the deck and to shell with the fore cannons.
    private static readonly Vector3 KrakenPosition = new Vector3(0f, 22f, 0f);

    // Wide enough to frame the ship AND the boss above it without manning the
    // helm. The stock boat camera (11.25) cropped the kraken off the top.
    private const float ArenaCameraSize = 14f;

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
            ApplySpriteImport(path, TextureWrapMode.Clamp, ArenaPixelsPerUnit);
        }

        ApplySpriteImport(WhirlPath, TextureWrapMode.Clamp, ArenaPixelsPerUnit);
        // Water tiles, so it must wrap -- and at 32 PPU to match the scroller.
        ApplySpriteImport(WaterPath, TextureWrapMode.Repeat, WaterPixelsPerUnit);

        AssetDatabase.Refresh();
        Debug.Log("[Kraken Arena] Art imported at 16 PPU, point-filtered.");
    }

    private static void ApplySpriteImport(
        string path, TextureWrapMode wrap, float pixelsPerUnit)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

        if (importer == null)
        {
            Debug.LogError($"[Kraken Arena] No importer for '{path}'.");
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = pixelsPerUnit;
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

        // Drop the boss in north of the ship (or reposition an existing one so
        // re-runs keep it at the current KrakenPosition).
        KrakenHealth existing = scene
            .GetRootGameObjects()
            .SelectMany(go => go.GetComponentsInChildren<KrakenHealth>(true))
            .FirstOrDefault();

        if (existing != null)
        {
            existing.transform.position = KrakenPosition;
        }
        else
        {
            GameObject kraken =
                (GameObject)PrefabUtility.InstantiatePrefab(krakenPrefab);
            kraken.transform.position = KrakenPosition;
        }

        SwapWaterToNight();
        FrameArenaCamera();

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

    // Zoom the arena camera out so the boss above the bow is on screen from the
    // start. Sets both the Camera and Camera2DFollow's serialized size, since
    // the helm reads the latter as its "default" zoom to return to.
    private static void FrameArenaCamera()
    {
        Camera cam = Object
            .FindObjectsByType<Camera>(FindObjectsSortMode.None)
            .FirstOrDefault(c => c.orthographic);
        if (cam != null)
        {
            cam.orthographicSize = ArenaCameraSize;
        }

        foreach (MonoBehaviour mb in Object
            .FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (mb.GetType().Name != "Camera2DFollow")
            {
                continue;
            }

            SerializedObject so = new SerializedObject(mb);
            SerializedProperty size = so.FindProperty("orthographicSize");
            if (size != null)
            {
                size.floatValue = ArenaCameraSize;
                so.ApplyModifiedPropertiesWithoutUndo();
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
