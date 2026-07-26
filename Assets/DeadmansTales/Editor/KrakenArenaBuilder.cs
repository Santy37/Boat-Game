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
///   - whirl_anim_0..7   : the new 8-frame whirlpool sheet, checker background
///     keyed to transparency and each frame cropped/registered on its centre.
///   - rock_0..5         : the new six reef formations (small..large), likewise
///     keyed from the sheet, one tight sprite each with a bottom-centre pivot.
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

    // The staged vortex frames are 332px, each cropped and registered on its
    // own centre so the flipbook doesn't wobble. 45 PPU makes the ~317px spiral
    // read as a ~7-unit maelstrom.
    private const float WhirlPixelsPerUnit = 45f;
    private const float WhirlFps = 12f;

    // The six reef formations (rock_0..5, small..large) are staged at their
    // native pixel sizes, so a single shared PPU preserves their size
    // progression and the world size comes from the sprite itself rather than a
    // scale multiplier. 78 PPU makes the tallest crag ~6 units, the smallest
    // ~2.6. They import with a bottom-centre pivot so a rock's position is the
    // point where its foam base meets the water.
    private const float RockPixelsPerUnit = 78f;

    private const string BoatScenePath =
        "Assets/DeadmansTales/Scenes/Boat/Boat_Gameplay_2D.unity";
    private const string ArenaScenePath =
        "Assets/DeadmansTales/Scenes/Boat/Kraken_Arena_2D.unity";

    // Looming high above the bow. Scaled up (KrakenScale) so a boss reads as a
    // boss rather than a pinprick next to the big ship. The runtime KrakenStrafe
    // takes over its position from here.
    private static readonly Vector3 KrakenPosition = new Vector3(0f, 28.5f, 0f);
    // A boss should DWARF the ship. At scale 2 it was dwarfed BY the ship, so
    // it's blown up big to read as a giant monster looming over the deck.
    private const float KrakenScale = 3.5f;

    // The kraken (KrakenStrafe) stays in the TOP band and only slides
    // left-right there -- no diving through the ship. Rocks stream through the
    // centre for the crew to dodge; the bottom is left open for enemy ships
    // later, and whirlpools are reserved for a telegraphed tentacle attack.
    // Close enough that the whole boss fits INSIDE the combat camera frame --
    // at 32 its head was cut off at the top of the screen. The camera centre
    // sits at player+16 with ortho 24-26 and the scaled sprite is ~20 units
    // tall, AND the ship can now dodge ~10 units downward (camera follows),
    // so the boss needs to fit even from the bottom of the steering box.
    private const float KrakenHoverDistance = 23f; // above the ship (top only)
    private const float KrakenStrafeRange = 12f;   // left-right sweep half-width
    // The reef streams as GATES with a guaranteed passable gap. Gates are spaced
    // well apart so only one is over the ship at a time.
    private const float ReefGateSpacing = 42f;
    private const int ReefRocksPerGate = 3;
    private const float ReefGapHeight = 16f;
    // The boss hovers well above the deck, so the cannons need the reach to aim
    // at it (the stock reticle range is far too short for a boss fight).
    private const float CannonAimRange = 30f;
    // Push the camera's framed centre up so the ship sits in the LOWER part of
    // the screen, opening the top for the boss to loom without landing on it.
    private const float CameraFrameOffsetY = 16f;

    // Zoomed well out, so the ship is one element in the arena rather than
    // filling the frame, with room for the boss and the hazards around it.
    private const float ArenaCameraSize = 24f;
    // The combat views (manning a cannon/helm) were far too tight -- the ship
    // filled the screen and dwarfed everything. Pull them out so the whole
    // arena reads during the fight.
    private const float CannonCameraZoom = 24f;
    private const float SteeringCameraZoom = 26f;

    private static readonly string[] KrakenFrames =
    {
        ArtDir + "/kraken_idle_0.png",
        ArtDir + "/kraken_idle_1.png",
        ArtDir + "/kraken_idle_2.png",
    };
    private static readonly string WaterPath = ArtDir + "/arena_night_water.png";
    private static readonly string[] RockPaths =
    {
        ArtDir + "/rock_0.png", ArtDir + "/rock_1.png",
        ArtDir + "/rock_2.png", ArtDir + "/rock_3.png",
        ArtDir + "/rock_4.png", ArtDir + "/rock_5.png",
    };

    // The whirlpool is an 8-frame procedural vortex flipbook (arms spiral
    // inward), not a rotated decal.
    private static readonly string[] WhirlFrames =
    {
        ArtDir + "/whirl_anim_0.png", ArtDir + "/whirl_anim_1.png",
        ArtDir + "/whirl_anim_2.png", ArtDir + "/whirl_anim_3.png",
        ArtDir + "/whirl_anim_4.png", ArtDir + "/whirl_anim_5.png",
        ArtDir + "/whirl_anim_6.png", ArtDir + "/whirl_anim_7.png",
    };

    // The tentacle slam: Santiago's 8-frame sheet (splash -> rise -> arc -> slam
    // -> sink), keyed off its baked grid guides and registered so every frame
    // shares one water-base anchor. That anchor is the sprite's pivot, so placing
    // the tentacle at the marked spot puts the splash there and the limb rises
    // above it. Imported at 20 PPU so the ~200px full-rise reads as a ~10-unit
    // tentacle -- bigger than the whirlpool it erupts from.
    private const float TentaclePixelsPerUnit = 20f;
    // Water-base anchor within the 361x219 staged canvas, normalised from the
    // bottom-left (see scratchpad/stage_tentacle.py).
    private static readonly Vector2 TentaclePivot = new Vector2(0.3934f, 0.0365f);
    private static readonly string[] TentacleFrames =
    {
        ArtDir + "/tentacle_0.png", ArtDir + "/tentacle_1.png",
        ArtDir + "/tentacle_2.png", ArtDir + "/tentacle_3.png",
        ArtDir + "/tentacle_4.png", ArtDir + "/tentacle_5.png",
        ArtDir + "/tentacle_6.png", ArtDir + "/tentacle_7.png",
    };

    // The oil glob the kraken lobs at the ship (Javier's "reuse the cannonball"
    // idea). One compact blob lifted from Santiago's transparent blob sheet
    // (largest connected component, highlight intact). 60 PPU makes the ~150px
    // glob a ~2.5-unit projectile.
    private const float OilPixelsPerUnit = 60f;
    private static readonly string OilPath = ArtDir + "/oil.png";

    // Cosmetic drifting ghost souls (4-frame float). Small, faint, in the
    // background -- pure atmosphere, no gameplay.
    private const float SoulPixelsPerUnit = 90f;
    private const float SoulFps = 6f;
    private const int SoulCount = 7;
    private const int SoulSeed = 20260726;
    private static readonly string[] SoulFrames =
    {
        ArtDir + "/soul_0.png", ArtDir + "/soul_1.png",
        ArtDir + "/soul_2.png", ArtDir + "/soul_3.png",
    };

    [MenuItem("Deadman's Tales/Kraken Arena/1. Import Art")]
    public static void ImportArenaArt()
    {
        foreach (string path in KrakenFrames)
        {
            ApplySpriteImport(path, TextureWrapMode.Clamp, ArenaPixelsPerUnit);
        }

        foreach (string frame in WhirlFrames)
        {
            ApplySpriteImport(frame, TextureWrapMode.Clamp, WhirlPixelsPerUnit);
        }
        foreach (string frame in SoulFrames)
        {
            ApplySpriteImport(frame, TextureWrapMode.Clamp, SoulPixelsPerUnit);
        }
        foreach (string frame in TentacleFrames)
        {
            // Custom pivot on the shared water-base anchor so the flipbook rises
            // from a fixed point instead of jittering frame to frame.
            ApplySpriteImport(
                frame, TextureWrapMode.Clamp, TentaclePixelsPerUnit,
                SpriteAlignment.Custom, TentaclePivot);
        }
        // Oil projectile: a centred pivot so it flies and spins about its middle.
        ApplySpriteImport(OilPath, TextureWrapMode.Clamp, OilPixelsPerUnit);
        foreach (string rock in RockPaths)
        {
            // Bottom-centre pivot so the placement point is the rock's waterline.
            ApplySpriteImport(
                rock, TextureWrapMode.Clamp, RockPixelsPerUnit,
                SpriteAlignment.BottomCenter);
        }
        // Water tiles, so it must wrap -- and at 32 PPU to match the scroller.
        ApplySpriteImport(WaterPath, TextureWrapMode.Repeat, WaterPixelsPerUnit);

        AssetDatabase.Refresh();
        Debug.Log("[Kraken Arena] Art imported (point-filtered, per-sprite PPU).");
    }

    private static void ApplySpriteImport(
        string path, TextureWrapMode wrap, float pixelsPerUnit,
        SpriteAlignment alignment = SpriteAlignment.Center,
        Vector2? customPivot = null)
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

        // Sprite alignment (pivot) lives on TextureImporterSettings, not on the
        // importer directly. Read the settings we just configured, set the
        // pivot, and write them back. A custom pivot (normalised, from bottom-
        // left) registers frames that share a canvas on a common anchor.
        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)alignment;
        if (alignment == SpriteAlignment.Custom && customPivot.HasValue)
        {
            settings.spritePivot = customPivot.Value;
        }
        importer.SetTextureSettings(settings);

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

        // Idle sprite loop (breathing), ~6 fps for a slow menace.
        AnimatorController controller = BuildLoopClip(
            frames, 6f,
            AnimDir + "/KrakenIdle.anim", AnimDir + "/Kraken.controller");

        // --- prefab: SpriteRenderer + Animator + KrakenHealth
        GameObject root = new GameObject("Kraken");
        SpriteRenderer sr = root.AddComponent<SpriteRenderer>();
        sr.sprite = frames[0];
        sr.sortingOrder = 10;

        Animator animator = root.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;

        // A boss should loom. The sprite is ~6.7 units wide at 1x; this makes
        // it read as a threat over the ship rather than a pinprick.
        root.transform.localScale = Vector3.one * KrakenScale;

        // Make it feel alive: a gentle float and sway on top of the breathing
        // idle. BoatBob already does exactly this (it just oscillates a
        // transform), so reuse it rather than write a second bobber.
        BoatBob bob = root.AddComponent<BoatBob>();
        SerializedObject bobbed = new SerializedObject(bob);
        SetFloat(bobbed, "bobHeight", 0.45f);
        SetFloat(bobbed, "bobSpeed", 0.4f);
        SetFloat(bobbed, "rockDegrees", 3f);
        SetFloat(bobbed, "rockSpeed", 0.3f);
        bobbed.ApplyModifiedPropertiesWithoutUndo();

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

    [MenuItem("Deadman's Tales/Kraken Arena/2b. Build Whirlpool Prefab")]
    public static void BuildWhirlpoolPrefab()
    {
        Directory.CreateDirectory(PrefabDir);
        Directory.CreateDirectory(AnimDir);

        Sprite[] frames = new Sprite[WhirlFrames.Length];
        for (int i = 0; i < WhirlFrames.Length; i++)
        {
            frames[i] = AssetDatabase.LoadAssetAtPath<Sprite>(WhirlFrames[i]);
            if (frames[i] == null)
            {
                Debug.LogError(
                    $"[Kraken Arena] Whirl frame '{WhirlFrames[i]}' missing; " +
                    "run step 1 first.");
                return;
            }
        }

        // The vortex is a real flipbook now -- arms spiral inward -- not a
        // rotated sprite.
        AnimatorController controller = BuildLoopClip(
            frames, WhirlFps,
            AnimDir + "/Whirlpool.anim", AnimDir + "/Whirlpool.controller");

        GameObject root = new GameObject("Whirlpool");
        SpriteRenderer sr = root.AddComponent<SpriteRenderer>();
        sr.sprite = frames[0];
        // Above the water, below the ship and boss.
        sr.sortingOrder = 1;

        Animator animator = root.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;

        string prefabPath = PrefabDir + "/Whirlpool.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Kraken Arena] Built {prefabPath} (8-frame vortex).");
    }

    // Builds a looping sprite-swap AnimationClip from frames and returns a
    // controller that plays it. Shared by the kraken idle and the whirlpool.
    private static AnimatorController BuildLoopClip(
        Sprite[] frames, float fps, string clipPath, string controllerPath)
    {
        AnimationClip clip = new AnimationClip { frameRate = fps };
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
                time = i / fps,
                value = frames[i],
            };
        }
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

        AnimationClipSettings settings =
            AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        AssetDatabase.CreateAsset(clip, clipPath);
        return AnimatorController.CreateAnimatorControllerAtPathWithClip(
            controllerPath, clip);
    }

    private static void SetFloat(
        SerializedObject so, string prop, float value)
    {
        SerializedProperty p = so.FindProperty(prop);
        if (p != null)
        {
            p.floatValue = value;
        }
    }

    [MenuItem("Deadman's Tales/Kraken Arena/2c. Build Rock Prefabs")]
    public static void BuildRockPrefabs()
    {
        Directory.CreateDirectory(PrefabDir);

        for (int i = 0; i < RockPaths.Length; i++)
        {
            Sprite rock = AssetDatabase.LoadAssetAtPath<Sprite>(RockPaths[i]);
            if (rock == null)
            {
                Debug.LogError(
                    $"[Kraken Arena] Rock sprite '{RockPaths[i]}' missing; " +
                    "run step 1 first.");
                return;
            }

            GameObject root = new GameObject($"Rock_{i}");
            SpriteRenderer sr = root.AddComponent<SpriteRenderer>();
            sr.sprite = rock;
            // Above the whirlpools, below the ship and boss.
            sr.sortingOrder = 2;

            string prefabPath = PrefabDir + $"/Rock_{i}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            $"[Kraken Arena] Built {RockPaths.Length} reef-rock prefabs.");
    }

    [MenuItem("Deadman's Tales/Kraken Arena/2d. Build Soul Prefab")]
    public static void BuildSoulPrefab()
    {
        Directory.CreateDirectory(PrefabDir);
        Directory.CreateDirectory(AnimDir);

        Sprite[] frames = new Sprite[SoulFrames.Length];
        for (int i = 0; i < SoulFrames.Length; i++)
        {
            frames[i] = AssetDatabase.LoadAssetAtPath<Sprite>(SoulFrames[i]);
            if (frames[i] == null)
            {
                Debug.LogError(
                    $"[Kraken Arena] Soul frame '{SoulFrames[i]}' missing; " +
                    "run step 1 first.");
                return;
            }
        }

        AnimatorController controller = BuildLoopClip(
            frames, SoulFps,
            AnimDir + "/Soul.anim", AnimDir + "/Soul.controller");

        GameObject root = new GameObject("Soul");
        SpriteRenderer sr = root.AddComponent<SpriteRenderer>();
        sr.sprite = frames[0];
        sr.color = new Color(1f, 1f, 1f, 0.65f); // faint + ghostly
        sr.sortingOrder = 0;                      // drift in the background

        Animator animator = root.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;

        root.AddComponent<SoulDrift>();

        string prefabPath = PrefabDir + "/Soul.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Kraken Arena] Built {prefabPath} (4-frame drifting soul).");
    }

    [MenuItem("Deadman's Tales/Kraken Arena/2e. Build Oil Prefab")]
    public static void BuildOilPrefab()
    {
        Directory.CreateDirectory(PrefabDir);

        Sprite oil = AssetDatabase.LoadAssetAtPath<Sprite>(OilPath);
        if (oil == null)
        {
            Debug.LogError(
                $"[Kraken Arena] Oil sprite '{OilPath}' missing; run step 1.");
            return;
        }

        GameObject root = new GameObject("OilShot");
        SpriteRenderer sr = root.AddComponent<SpriteRenderer>();
        sr.sprite = oil;
        sr.sortingOrder = 7; // in front of the tentacle as it flies
        root.AddComponent<OilShot>();

        string prefabPath = PrefabDir + "/OilShot.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Kraken Arena] Built {prefabPath} (oil projectile).");
    }

    // Scatter faint drifting souls across the arena for atmosphere.
    // Idempotent via the "Souls" parent.
    private static void PlaceSouls(Scene scene, Vector2 shipCenter)
    {
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            if (go.name.StartsWith("Souls"))
            {
                Object.DestroyImmediate(go);
            }
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            PrefabDir + "/Soul.prefab");
        if (prefab == null)
        {
            return;
        }

        GameObject parent = new GameObject("Souls");
        System.Random rng = new System.Random(SoulSeed);
        for (int i = 0; i < SoulCount; i++)
        {
            float x = shipCenter.x
                + Mathf.Lerp(-46f, 46f, (float)rng.NextDouble());
            float y = shipCenter.y
                + Mathf.Lerp(-24f, 30f, (float)rng.NextDouble());
            GameObject s = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            s.transform.SetParent(parent.transform, true);
            s.transform.position = new Vector3(x, y, 0f);
        }
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

        GameObject kraken = existing != null
            ? existing.gameObject
            : (GameObject)PrefabUtility.InstantiatePrefab(krakenPrefab);
        kraken.transform.position = KrakenPosition;
        kraken.transform.localScale = Vector3.one * KrakenScale;

        // Water is left at the boat scene's original colour on purpose -- the
        // dark "night" tone clashed with the bright whirlpool art, so the arena
        // keeps the original day water it inherits from the Save-As.
        FrameArenaCamera();
        OrientArenaSail(scene);

        // Three bands: the kraken strafes across the TOP only, rocks stream
        // through the CENTRE for the ship to dodge, and the bottom is left open
        // (enemy ships + whirlpool tentacle attacks come later). Cannons get the
        // range to reach the boss overhead.
        Vector2 shipCenter = GetShipCenter(scene);
        RemoveBoatRunnerClutter(scene);
        BuildScrollingReef(scene, shipCenter);
        SetupKrakenMotion(kraken, shipCenter);
        SetupKrakenAttack(kraken, scene);
        TuneCannonsForBoss(scene);
        TuneArenaCameraFraming(scene);
        PlaceSouls(scene, shipCenter);

        EnsureArenaHud();
        EnsureArenaTestPlayer(scene);

        bool ok = EditorSceneManager.SaveScene(scene, ArenaScenePath);
        Debug.Log(ok
            ? $"[Kraken Arena] Saved playable arena to {ArenaScenePath}."
            : "[Kraken Arena] FAILED to save the arena scene.");

        RegisterArenaScene();
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

    // Orient the arena as a side-scrolling gauntlet: the ship is a side-view
    // sprite, so the reef streams horizontally (right -> left) rather than up
    // and down. The treadmill water scrolls left to match; the ship is mirrored
    // to face right (bow into the oncoming reef); and the helm's steering box is
    // reshaped to give vertical room, since the crew now weaves up and down.
    private static void OrientArenaSail(Scene scene)
    {
        foreach (ScrollingWater water in Object
            .FindObjectsByType<ScrollingWater>(FindObjectsSortMode.None))
        {
            SerializedObject so = new SerializedObject(water);
            SerializedProperty speed = so.FindProperty("scrollSpeed");
            if (speed != null)
            {
                speed.vector2Value = new Vector2(-3f, 0f);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        foreach (ShipHelm helm in Object
            .FindObjectsByType<ShipHelm>(FindObjectsSortMode.None))
        {
            SerializedObject so = new SerializedObject(helm);
            SerializedProperty bounds = so.FindProperty("moveBounds");
            if (bounds != null)
            {
                // Real room in BOTH axes: vertical for dodging the reef gates,
                // horizontal so the ship can chase/flee across the arena.
                bounds.vector2Value = new Vector2(16f, 10f);
            }
            SerializedProperty steer = so.FindProperty("moveSpeed");
            if (steer != null)
            {
                // Stock 3 u/s was a barge; the dodge-gauntlet needs a ship
                // that answers the wheel.
                steer.floatValue = 6f;
            }
            SerializedProperty helmZoom = so.FindProperty("steeringCameraZoom");
            if (helmZoom != null)
            {
                helmZoom.floatValue = SteeringCameraZoom;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            // Mirror the ship so the bow points right, into the oncoming reef.
            Transform ship =
                so.FindProperty("ship")?.objectReferenceValue as Transform;
            MirrorFaceRight(ship);
        }

        MirrorFaceRight(FindByName(scene, "Ship"));
    }

    // Mirror the ship (localScale.x = -1) so its bow faces right, into the
    // oncoming reef. Santiago wants this look.
    //
    // NOTE: the mirror was once suspected of causing the spawn fly-up (the hull
    // EdgeCollider2D is a child of Ship and flips with it, while the
    // PlayerSpawns are root objects and do not). MEASURED result: the player
    // climbs identically mirrored or not, with zero physics contacts, zero
    // rigidbody velocity and zero input -- a direct transform write by
    // something on the player, nothing to do with hull collision. Cannon aim
    // is world-space, so the negative scale is safe.
    private static void MirrorFaceRight(Transform ship)
    {
        if (ship == null)
        {
            return;
        }
        Vector3 s = ship.localScale;
        ship.localScale = new Vector3(-Mathf.Abs(s.x), s.y, s.z);
    }

    private static Transform FindByName(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == name)
            {
                return root.transform;
            }
            Transform found = root.transform.Find(name);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    // Replace any static hazards with a single ScrollingReef that streams the
    // rock formations + whirlpools past the (near-stationary) ship, so the crew
    // actually has a field to dodge. Clears legacy static rocks/spires/
    // whirlpools and any prior reef so re-runs stay idempotent.
    private static void BuildScrollingReef(Scene scene, Vector2 shipCenter)
    {
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            if (go.name.StartsWith("Rock") || go.name.StartsWith("Spire")
                || go.name.StartsWith("Whirlpool")
                || go.name.StartsWith("ScrollingReef")
                || go.name.StartsWith("MarginFillers"))
            {
                Object.DestroyImmediate(go);
            }
        }

        GameObject reefObject = new GameObject("ScrollingReef");
        ScrollingReef reef = reefObject.AddComponent<ScrollingReef>();

        SerializedObject so = new SerializedObject(reef);

        SerializedProperty rocks = so.FindProperty("rockPrefabs");
        rocks.arraySize = RockPaths.Length;
        for (int i = 0; i < RockPaths.Length; i++)
        {
            rocks.GetArrayElementAtIndex(i).objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    PrefabDir + $"/Rock_{i}.prefab");
        }

        // Gated so there is ALWAYS a passable gap; band centred on the ship.
        so.FindProperty("gateSpacing").floatValue = ReefGateSpacing;
        so.FindProperty("rocksPerGate").intValue = ReefRocksPerGate;
        so.FindProperty("gapHeight").floatValue = ReefGapHeight;
        so.FindProperty("laneMinY").floatValue = shipCenter.y - 13f;
        so.FindProperty("laneMaxY").floatValue = shipCenter.y + 13f;

        Collider2D shipHitbox = FindShipHitbox(scene);
        if (shipHitbox != null)
        {
            so.FindProperty("shipHitbox").objectReferenceValue = shipHitbox;
        }
        else
        {
            Debug.LogWarning(
                "[Kraken Arena] No 'ShipHitBox' collider found; reef will be "
                + "visual only (no contact damage).");
        }

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // The arena is a Save-As of the boat endless-runner, so it inherits that
    // runner's obstacle spawner (BoatObstacleGenerator) and its spawn area. The
    // arena streams its own ScrollingReef instead, so strip the inherited spawner
    // -- otherwise it litters the deck with boat rocks (NetworkObjects with
    // colliders) that fight the reef and can shove the just-spawned player.
    private static void RemoveBoatRunnerClutter(Scene scene)
    {
        foreach (BoatObstacleGenerator gen in Object
            .FindObjectsByType<BoatObstacleGenerator>(FindObjectsSortMode.None))
        {
            SerializedObject so = new SerializedObject(gen);
            BoxCollider2D area = so.FindProperty("generationArea")
                ?.objectReferenceValue as BoxCollider2D;
            if (area != null)
            {
                Object.DestroyImmediate(area.gameObject);
            }
            Object.DestroyImmediate(gen.gameObject);
        }
    }

    // The ship's hull hitbox, so the reef can damage the ship on contact.
    private static Collider2D FindShipHitbox(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Collider2D col in
                root.GetComponentsInChildren<Collider2D>(true))
            {
                if (col.gameObject.name == "ShipHitBox")
                {
                    return col;
                }
            }
        }
        return null;
    }

    // The playable deck centre -- the anchor the reef/boss/souls/camera build
    // around. IMPORTANT: the "Ship" root transform sits at the waterline origin
    // (0,0), NOT on the deck, which is ~12 units up (cannons, helm, spawns and
    // the camera all cluster there). Centring on the Ship root put the whole
    // arena 12 units below the crew -- the reef and boss ended up off-screen
    // under the deck. The spawn markers ARE the deck, so use their centroid.
    private static Vector2 GetShipCenter(Scene scene)
    {
        PlayerSpawnPoint2D[] spawns = Object
            .FindObjectsByType<PlayerSpawnPoint2D>(FindObjectsSortMode.None);
        if (spawns.Length > 0)
        {
            Vector2 sum = Vector2.zero;
            foreach (PlayerSpawnPoint2D s in spawns)
            {
                sum += (Vector2)s.transform.position;
            }
            return sum / spawns.Length;
        }

        foreach (ShipHelm helm in Object
            .FindObjectsByType<ShipHelm>(FindObjectsSortMode.None))
        {
            SerializedObject so = new SerializedObject(helm);
            Transform ship =
                so.FindProperty("ship")?.objectReferenceValue as Transform;
            if (ship != null)
            {
                return ship.position;
            }
        }

        Transform named = FindByName(scene, "Ship");
        return named != null ? (Vector2)named.position : new Vector2(0f, 2f);
    }

    // Swap the kraken's BoatBob for a KrakenStrafe so it slides left-right in
    // the TOP band only -- no diving through the ship.
    private static void SetupKrakenMotion(GameObject kraken, Vector2 center)
    {
        BoatBob bob = kraken.GetComponent<BoatBob>();
        if (bob != null)
        {
            Object.DestroyImmediate(bob);
        }

        KrakenStrafe strafe = kraken.GetComponent<KrakenStrafe>();
        if (strafe == null)
        {
            strafe = kraken.AddComponent<KrakenStrafe>();
        }

        SerializedObject so = new SerializedObject(strafe);
        so.FindProperty("shipCenter").vector2Value = center;
        so.FindProperty("hoverDistance").floatValue = KrakenHoverDistance;
        so.FindProperty("strafeRange").floatValue = KrakenStrafeRange;
        // Slow, heavy motion -- a giant boss with weight, not a floaty cork.
        so.FindProperty("strafeSpeed").floatValue = 0.35f;
        so.FindProperty("bobHeight").floatValue = 0.35f;
        so.FindProperty("bobSpeed").floatValue = 0.5f;
        // Stay up top -- never dive through the ship.
        so.FindProperty("sideSwitchSeconds").floatValue = 99999f;
        so.FindProperty("rocks").arraySize = 0;
        so.ApplyModifiedPropertiesWithoutUndo();

        // Open with the boss up top.
        kraken.transform.position = new Vector3(
            center.x, center.y + KrakenHoverDistance,
            kraken.transform.position.z);
    }

    // Give the kraken its telegraphed whirlpool tentacle-slam attack.
    private static void SetupKrakenAttack(GameObject kraken, Scene scene)
    {
        KrakenAttack attack = kraken.GetComponent<KrakenAttack>();
        if (attack == null)
        {
            attack = kraken.AddComponent<KrakenAttack>();
        }

        SerializedObject so = new SerializedObject(attack);
        so.FindProperty("whirlpoolPrefab").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabDir + "/Whirlpool.prefab");

        // The strike now plays the WHOLE rise-arc-slam flipbook as the hit
        // (the telegraph is whirlpool-only), so give the pop-out enough time
        // to read as an eruption rather than a single flicker.
        SerializedProperty slam = so.FindProperty("slamTime");
        if (slam != null)
        {
            slam.floatValue = 0.55f;
        }

        // The tentacle flipbook that erupts from the whirlpool telegraph.
        SerializedProperty frames = so.FindProperty("tentacleFrames");
        if (frames != null)
        {
            frames.arraySize = TentacleFrames.Length;
            for (int i = 0; i < TentacleFrames.Length; i++)
            {
                frames.GetArrayElementAtIndex(i).objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<Sprite>(TentacleFrames[i]);
            }
        }

        Collider2D shipHitbox = FindShipHitbox(scene);
        if (shipHitbox != null)
        {
            so.FindProperty("shipHitbox").objectReferenceValue = shipHitbox;
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        SetupKrakenOilAttack(kraken, scene, shipHitbox);
    }

    // Give the kraken its ranged oil barrage (Javier's cannonball-reuse idea):
    // it lobs oil blobs at the ship on a separate cadence from the slam.
    private static void SetupKrakenOilAttack(
        GameObject kraken, Scene scene, Collider2D shipHitbox)
    {
        KrakenOilAttack oil = kraken.GetComponent<KrakenOilAttack>();
        if (oil == null)
        {
            oil = kraken.AddComponent<KrakenOilAttack>();
        }

        SerializedObject so = new SerializedObject(oil);
        so.FindProperty("oilPrefab").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabDir + "/OilShot.prefab");
        if (shipHitbox != null)
        {
            so.FindProperty("shipHitbox").objectReferenceValue = shipHitbox;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // Give the ship's cannons the reach to hit the boss overhead, and pull the
    // cannon camera out so the arena is visible while manning one.
    private static void TuneCannonsForBoss(Scene scene)
    {
        foreach (ShipCannon cannon in Object
            .FindObjectsByType<ShipCannon>(FindObjectsSortMode.None))
        {
            SerializedObject so = new SerializedObject(cannon);
            SerializedProperty range = so.FindProperty("aimRange");
            if (range != null)
            {
                range.floatValue = CannonAimRange;
            }
            SerializedProperty zoom = so.FindProperty("cannonCameraZoom");
            if (zoom != null)
            {
                zoom.floatValue = CannonCameraZoom;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    // Frame the arena so the ship sits low and the boss has room above it.
    //
    // ONLY Camera2DFollow (the networked follow camera) gets the offset, and
    // LocalCoopCamera is DISABLED in this scene outright. LocalCoopCamera is
    // the couch co-op framer and it LEASHES players -- it clamps every
    // PlayerCharacter's transform+rigidbody into its visible rectangle each
    // LateUpdate. With the follow camera holding the view 16 units above the
    // player, the leash rectangle sat above the player too, so the leash
    // dragged the player up, the follow camera re-centred above the new spot,
    // and the pair ran away at ~55 u/s -- the measured "shot straight up on
    // spawn" bug (see Logs/arena_diag.txt: climb stopped the instant the
    // camera object was disabled; zero velocity, zero input, zero contacts
    // throughout). The arena is networked-only for now, so the couch framer
    // has no business running here. Re-enable it deliberately (leash tuned for
    // the offset) if the arena ever gets a local co-op mode.
    private static void TuneArenaCameraFraming(Scene scene)
    {
        foreach (MonoBehaviour mb in Object
            .FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            string typeName = mb.GetType().Name;
            if (typeName == "LocalCoopCamera")
            {
                SerializedObject coop = new SerializedObject(mb);
                SerializedProperty enabledProp = coop.FindProperty("m_Enabled");
                if (enabledProp != null)
                {
                    enabledProp.boolValue = false;
                    coop.ApplyModifiedPropertiesWithoutUndo();
                }
                continue;
            }

            if (typeName != "Camera2DFollow")
            {
                continue;
            }

            SerializedObject so = new SerializedObject(mb);
            SerializedProperty off = so.FindProperty("viewOffset");
            if (off != null)
            {
                off.vector2Value = new Vector2(0f, CameraFrameOffsetY);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }

    // TESTING ONLY: drop a KrakenArenaTestPlayer into the arena so hitting Play
    // on the scene directly spawns a controllable player (the arena isn't wired
    // through the menu/spawn coordinator yet). Idempotent by name.
    private static void EnsureArenaTestPlayer(Scene scene)
    {
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            if (go.name == "KrakenArenaTestPlayer")
            {
                Object.DestroyImmediate(go);
            }
        }

        GameObject tester = new GameObject("KrakenArenaTestPlayer");
        tester.AddComponent<KrakenArenaTestPlayer>();
    }

    // Add the boss HUD object if the scene has none.
    private static void EnsureArenaHud()
    {
        bool present = Object
            .FindObjectsByType<KrakenArenaHud>(FindObjectsSortMode.None)
            .Length > 0;
        if (present)
        {
            return;
        }

        GameObject hud = new GameObject("KrakenArenaHud");
        hud.AddComponent<KrakenArenaHud>();
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

    // One-shot: run every step in order.
    [MenuItem("Deadman's Tales/Kraken Arena/0. Build Everything")]
    public static void BuildAllFromCommandLine()
    {
        ImportArenaArt();
        BuildKrakenPrefab();
        BuildWhirlpoolPrefab();
        BuildRockPrefabs();
        BuildSoulPrefab();
        BuildOilPrefab();
        BuildArenaScene();
    }
}
