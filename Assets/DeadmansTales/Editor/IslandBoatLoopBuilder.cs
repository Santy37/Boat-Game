using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DeadmansTales.Networking;
using DeadmansTales.Ship;
using DeadmansTales.UI;
using DeadmansTales.WorldGeneration;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Wires the full island/boat survival loop from the GDD:
///  - Island checkpoint: seeded food pickups that heal (eat food, heal,
///    upgrade between combat stages).
///  - Boat stage: shared ship hull health, escalating leaks that damage the
///    hull, a repair station, and a ship health HUD.
///
/// Generates its own pixel-art food/patch/leak sprites so nothing depends on
/// unimported outside art. Idempotent: safe to run repeatedly.
/// </summary>
public static class IslandBoatLoopBuilder
{
    private const string MenuPath =
        "Deadman's Tales/Build Island-Boat Survival Loop";

    private const string IslandScenePath =
        "Assets/DeadmansTales/Scenes/Island_After_Ocean_01_2D.unity";

    private const string BoatScenePath =
        "Assets/DeadmansTales/Scenes/Boat_Gameplay_2D.unity";

    private const string FoodSheetPath =
        "Assets/DeadmansTales/Art_Pixel/Props/island_food_items.png";

    private const string GameplayPrefabFolder =
        "Assets/DeadmansTales/Prefabs/Gameplay";

    private const string ApplePrefabPath =
        GameplayPrefabFolder + "/NetworkFoodPickup_Apple.prefab";

    private const string MeatPrefabPath =
        GameplayPrefabFolder + "/NetworkFoodPickup_Meat.prefab";

    private const string GeneratedNetworkPrefabsPath =
        "Assets/DefaultNetworkPrefabs.asset";

    private const string BootstrapSettingsPath =
        "Assets/DeadmansTales/Resources/Networking/DeadmansNetworkBootstrapSettings.asset";

    private const int FoodFrameSize = 16;
    private const int FoodPixelsPerUnit = 14;

    // Readonly (not const) so the early-out below doesn't trip the
    // unreachable-code warning while the boat scene is hands-off.
    private static readonly bool BoatSceneIsTeammateOwned = true;

    private static readonly Vector2[] FoodMarkerPositions =
    {
        new Vector2(-15f, -9f),
        new Vector2(-9f, 7f),
        new Vector2(-2f, 12f),
        new Vector2(2f, -6f),
        new Vector2(11f, 9f),
        new Vector2(16f, -2f),
    };

    private static readonly Vector2[] LeakOffsets =
    {
        new Vector2(-2.4f, -0.6f),
        new Vector2(2.4f, -0.6f),
        new Vector2(0f, 1.1f),
    };

    [MenuItem(MenuPath)]
    public static void BuildAll()
    {
        BuildFoodArt();
        AssetDatabase.Refresh();

        GameObject applePrefab = CreateFoodPrefab(
            ApplePrefabPath,
            "NetworkFoodPickup_Apple",
            0,
            "an Apple",
            25f
        );
        GameObject meatPrefab = CreateFoodPrefab(
            MeatPrefabPath,
            "NetworkFoodPickup_Meat",
            1,
            "Roast Meat",
            45f
        );

        RegisterNetworkPrefabs(new[] { applePrefab, meatPrefab });

        AddFoodMarkersToIslandScene(applePrefab, meatPrefab);
        WireBoatShipSurvival();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "[Loop Builder] Island food pickups and boat ship-survival " +
            "systems are built and wired."
        );
    }

    public static void BuildAllFromCommandLine()
    {
        BuildAll();
    }

    // ------------------------------------------------------------------
    // Food / patch / leak pixel art
    // ------------------------------------------------------------------

    private static void BuildFoodArt()
    {
        Dictionary<char, Color32> palette = new Dictionary<char, Color32>
        {
            { '.', new Color32(0, 0, 0, 0) },
            { 'R', new Color32(196, 44, 54, 255) },
            { 'D', new Color32(138, 22, 34, 255) },
            { 'W', new Color32(255, 214, 214, 255) },
            { 'G', new Color32(74, 160, 60, 255) },
            { 'B', new Color32(104, 66, 34, 255) },
            { 'M', new Color32(168, 100, 52, 255) },
            { 'H', new Color32(214, 152, 92, 255) },
            { 'S', new Color32(110, 62, 30, 255) },
            { 'O', new Color32(236, 230, 214, 255) },
            { 'P', new Color32(150, 106, 58, 255) },
            { 'Q', new Color32(94, 64, 34, 255) },
            { 'N', new Color32(70, 46, 24, 255) },
            { 'L', new Color32(84, 156, 214, 255) },
            { 'C', new Color32(150, 204, 244, 255) },
            { 'E', new Color32(46, 104, 168, 255) },
        };

        string[] apple =
        {
            "................",
            ".......B........",
            ".......B........",
            ".....GGB........",
            "....GGGB........",
            "...RRRRRRRR.....",
            "..RRRRRRRRRR....",
            ".RRWWRRRRRRRR...",
            ".RRWRRRRRRRRR...",
            ".RRRRRRRRRRRR...",
            ".RRRRRRRRRRRD...",
            ".DRRRRRRRRRDD...",
            "..DRRRRRRRDD....",
            "...DDRRRDDD.....",
            "....DDDDD.......",
            "................",
        };

        string[] meat =
        {
            "................",
            "................",
            "....MMMMM.......",
            "...MMMMMMMM.....",
            "..MMHHMMMMMM....",
            "..MHHMMMMMMMM...",
            "..MMMMMMMMMMM...",
            "..SMMMMMMMMMM...",
            "...SMMMMMMMS....",
            "....SSMMMSS.....",
            "......SSS.......",
            ".......OO.......",
            "......OOOO......",
            ".....OO..OO.....",
            "................",
            "................",
        };

        string[] patch =
        {
            "................",
            "..PPPPPPPPPPP...",
            "..PQQPPQPPQPP...",
            "..PPPPPPPPPPP...",
            "..NPPPPPPPPPN...",
            "..PPPPPPPPPPP...",
            "..PPQPPQPPQPP...",
            "..PPPPPPPPPPP...",
            "..NPPPPPPPPPN...",
            "..PPPPPPPPPPP...",
            "..PQQPPQPPQPP...",
            "..PPPPPPPPPPP...",
            "................",
            "................",
            "................",
            "................",
        };

        string[] splash =
        {
            "................",
            "......C.........",
            "....C...C.C.....",
            "......L.L.......",
            "...C.LLLLL.C....",
            "....LLCLLLL.....",
            "...LLCCLLLLL....",
            "..LLLCLLLLLLL...",
            "..LLLLLLLELLL...",
            "..ELLLLLEELLE...",
            "...ELLLEELLE....",
            "....EELLLEE.....",
            "......EEE.......",
            "................",
            "................",
            "................",
        };

        string[][] frames = { apple, meat, patch, splash };

        Texture2D sheet = new Texture2D(
            FoodFrameSize * frames.Length,
            FoodFrameSize,
            TextureFormat.RGBA32,
            false
        );

        for (int frameIndex = 0; frameIndex < frames.Length; frameIndex++)
        {
            string[] rows = frames[frameIndex];

            for (int row = 0; row < FoodFrameSize; row++)
            {
                for (int column = 0; column < FoodFrameSize; column++)
                {
                    char key = rows[row][column];

                    sheet.SetPixel(
                        frameIndex * FoodFrameSize + column,
                        FoodFrameSize - 1 - row,
                        palette[key]
                    );
                }
            }
        }

        sheet.Apply();

        File.WriteAllBytes(
            Path.Combine(
                Directory.GetCurrentDirectory(),
                FoodSheetPath.Replace('/', Path.DirectorySeparatorChar)
            ),
            sheet.EncodeToPNG()
        );

        UnityEngine.Object.DestroyImmediate(sheet);

        AssetDatabase.ImportAsset(FoodSheetPath);
        ConfigureGridSheet(FoodSheetPath, FoodFrameSize, FoodPixelsPerUnit);
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

    private static Sprite LoadFoodSprite(int index)
    {
        Sprite sprite = AssetDatabase
            .LoadAllAssetRepresentationsAtPath(FoodSheetPath)
            .OfType<Sprite>()
            .FirstOrDefault(candidate =>
                candidate.name.EndsWith($"_{index}"));

        if (sprite == null)
        {
            throw new InvalidOperationException(
                $"Food sheet frame {index} did not import."
            );
        }

        return sprite;
    }

    // ------------------------------------------------------------------
    // Food pickup prefabs
    // ------------------------------------------------------------------

    private static GameObject CreateFoodPrefab(
        string prefabPath,
        string objectName,
        int spriteIndex,
        string foodName,
        float healAmount
    )
    {
        GameObject root = new GameObject(objectName);

        try
        {
            root.AddComponent<NetworkObject>();

            CircleCollider2D collider =
                root.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = 0.45f;

            NetworkFoodPickup pickup =
                root.AddComponent<NetworkFoodPickup>();
            SetSerializedString(pickup, "foodName", foodName);
            SetSerializedFloat(pickup, "healAmount", healAmount);
            SetSerializedBool(pickup, "allowRepeatedInteraction", false);

            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);

            SpriteRenderer renderer =
                visual.AddComponent<SpriteRenderer>();
            renderer.sprite = LoadFoodSprite(spriteIndex);
            renderer.sortingOrder = 15;

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
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
                "DefaultNetworkPrefabs.asset was not found. Run the island " +
                "stage builder first."
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
    // Island scene: food markers + healing budget
    // ------------------------------------------------------------------

    private static void AddFoodMarkersToIslandScene(
        params GameObject[] foodPrefabs
    )
    {
        Scene scene = EditorSceneManager.OpenScene(
            IslandScenePath,
            OpenSceneMode.Single
        );

        GameObject markerRoot = FindSceneObject(
            scene,
            "SeededContentMarkers"
        );

        if (markerRoot == null)
        {
            throw new InvalidOperationException(
                "SeededContentMarkers was not found in the island scene. " +
                "Run the island stage builder first."
            );
        }

        Transform existingFoodRoot =
            markerRoot.transform.Find("FoodMarkers");

        if (existingFoodRoot != null)
        {
            UnityEngine.Object.DestroyImmediate(existingFoodRoot.gameObject);
        }

        GameObject foodRoot = new GameObject("FoodMarkers");
        foodRoot.transform.SetParent(markerRoot.transform, false);

        for (int index = 0; index < FoodMarkerPositions.Length; index++)
        {
            GameObject markerObject =
                new GameObject($"FoodMarker_{index:D2}");
            markerObject.transform.SetParent(foodRoot.transform, false);
            markerObject.transform.position = FoodMarkerPositions[index];

            SeededSpawnMarker2D marker =
                markerObject.AddComponent<SeededSpawnMarker2D>();
            SerializedObject markerSerialized = new SerializedObject(marker);

            markerSerialized.FindProperty("category").enumValueIndex =
                (int)SeededContentCategory.Healing;

            SerializedProperty prefabs =
                markerSerialized.FindProperty("networkPrefabs");
            prefabs.arraySize = foodPrefabs.Length;
            for (
                int prefabIndex = 0;
                prefabIndex < foodPrefabs.Length;
                prefabIndex++
            )
            {
                prefabs
                    .GetArrayElementAtIndex(prefabIndex)
                    .objectReferenceValue = foodPrefabs[prefabIndex];
            }

            markerSerialized.FindProperty("alwaysSpawn").boolValue = false;
            markerSerialized.FindProperty("spawnChance").floatValue = 0.7f;
            markerSerialized.FindProperty("minimumStage").intValue = 2;
            markerSerialized.FindProperty("maximumStage").intValue = 0;
            markerSerialized.ApplyModifiedPropertiesWithoutUndo();
        }

        EnsureHealingBudget(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void EnsureHealingBudget(Scene scene)
    {
        SeededIslandContentGenerator generator = scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<SeededIslandContentGenerator>(
                    true
                ))
            .FirstOrDefault();

        if (generator == null)
        {
            throw new InvalidOperationException(
                "SeededIslandContentGenerator was not found in the island " +
                "scene."
            );
        }

        SerializedObject generatorObject = new SerializedObject(generator);
        SerializedProperty budgets =
            generatorObject.FindProperty("contentBudgets");

        for (int index = 0; index < budgets.arraySize; index++)
        {
            SerializedProperty existing =
                budgets.GetArrayElementAtIndex(index);

            if (
                existing.FindPropertyRelative("category").enumValueIndex ==
                (int)SeededContentCategory.Healing
            )
            {
                return;
            }
        }

        budgets.arraySize++;
        SerializedProperty budget =
            budgets.GetArrayElementAtIndex(budgets.arraySize - 1);
        budget.FindPropertyRelative("category").enumValueIndex =
            (int)SeededContentCategory.Healing;
        budget.FindPropertyRelative("minimumCount").intValue = 2;
        budget.FindPropertyRelative("maximumCount").intValue = 4;

        generatorObject.ApplyModifiedPropertiesWithoutUndo();
    }

    // ------------------------------------------------------------------
    // Boat scene: ship health, leaks, repair station, HUD
    // ------------------------------------------------------------------

    private static void WireBoatShipSurvival()
    {
        // The boat scene belongs to the teammate's branch now; this branch
        // carries main's bytes for it verbatim so the merge stays clean.
        // Rewiring it here would recreate a competing version.
        if (BoatSceneIsTeammateOwned)
        {
            Debug.Log(
                "[Island/Boat Builder] Boat scene is teammate-owned; " +
                "ship survival wiring skipped."
            );
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(
            BoatScenePath,
            OpenSceneMode.Single
        );

        GameObject shipProp = FindSceneObject(scene, "Ship_prop");

        if (shipProp == null)
        {
            throw new InvalidOperationException(
                "Ship_prop was not found in the boat scene."
            );
        }

        Vector3 shipPosition = shipProp.transform.position;

        GameObject existingSurvival = FindSceneObject(scene, "ShipSurvival");
        if (existingSurvival != null)
        {
            UnityEngine.Object.DestroyImmediate(existingSurvival);
        }

        GameObject survivalRoot = new GameObject("ShipSurvival");
        survivalRoot.transform.position = shipPosition;

        // Shared hull health + leak pacing. One in-scene NetworkObject.
        GameObject shipCore = new GameObject("ShipCore");
        shipCore.transform.SetParent(survivalRoot.transform, false);
        shipCore.AddComponent<NetworkObject>();
        NetworkShipHealth shipHealth =
            shipCore.AddComponent<NetworkShipHealth>();
        SetSerializedFloat(shipHealth, "maximumHealth", 500f);
        ShipLeakDirector director =
            shipCore.AddComponent<ShipLeakDirector>();

        // Repair station on deck.
        GameObject repairStation = new GameObject("ShipRepairStation");
        repairStation.transform.SetParent(survivalRoot.transform, false);
        repairStation.transform.localPosition = new Vector3(0f, -1.3f, 0f);
        repairStation.AddComponent<NetworkObject>();

        BoxCollider2D repairCollider =
            repairStation.AddComponent<BoxCollider2D>();
        repairCollider.isTrigger = true;
        repairCollider.size = new Vector2(1.1f, 1.1f);

        NetworkShipRepairStation repair =
            repairStation.AddComponent<NetworkShipRepairStation>();
        SetSerializedFloat(repair, "repairPerUse", 40f);
        SetSerializedObject(repair, "shipHealth", shipHealth);

        GameObject repairVisual = new GameObject("Visual");
        repairVisual.transform.SetParent(repairStation.transform, false);
        SpriteRenderer repairRenderer =
            repairVisual.AddComponent<SpriteRenderer>();
        repairRenderer.sprite = LoadFoodSprite(2);
        repairRenderer.sortingOrder = 15;

        // Hull leaks around the deck.
        List<NetworkShipLeak> leaks = new List<NetworkShipLeak>();

        for (int index = 0; index < LeakOffsets.Length; index++)
        {
            GameObject leakObject = new GameObject($"ShipLeak_{index:D2}");
            leakObject.transform.SetParent(survivalRoot.transform, false);
            leakObject.transform.localPosition = LeakOffsets[index];
            leakObject.AddComponent<NetworkObject>();

            CircleCollider2D leakCollider =
                leakObject.AddComponent<CircleCollider2D>();
            leakCollider.isTrigger = true;
            leakCollider.radius = 0.55f;

            NetworkShipLeak leak =
                leakObject.AddComponent<NetworkShipLeak>();
            SetSerializedFloat(leak, "damagePerSecond", 4f);
            SetSerializedObject(leak, "shipHealth", shipHealth);

            GameObject leakVisual = new GameObject("LeakVisual");
            leakVisual.transform.SetParent(leakObject.transform, false);
            SpriteRenderer leakRenderer =
                leakVisual.AddComponent<SpriteRenderer>();
            leakRenderer.sprite = LoadFoodSprite(3);
            leakRenderer.sortingOrder = 16;
            leakVisual.SetActive(false);

            SetSerializedObject(leak, "leakVisual", leakVisual);
            leaks.Add(leak);
        }

        SerializedObject directorObject = new SerializedObject(director);
        SerializedProperty leaksProperty =
            directorObject.FindProperty("leaks");
        leaksProperty.arraySize = leaks.Count;
        for (int index = 0; index < leaks.Count; index++)
        {
            leaksProperty.GetArrayElementAtIndex(index).objectReferenceValue =
                leaks[index];
        }

        directorObject.FindProperty("shipHealth").objectReferenceValue =
            shipHealth;
        directorObject.ApplyModifiedPropertiesWithoutUndo();

        BuildShipHealthHud(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void BuildShipHealthHud(Scene scene)
    {
        Canvas canvas = scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Canvas>(true))
            .FirstOrDefault(candidate => candidate.isRootCanvas);

        if (canvas == null)
        {
            throw new InvalidOperationException(
                "No canvas was found in the boat scene for the ship HUD."
            );
        }

        Transform existingHud = canvas.transform.Find("ShipHealthHUD");
        if (existingHud != null)
        {
            UnityEngine.Object.DestroyImmediate(existingHud.gameObject);
        }

        GameObject hudRoot = new GameObject(
            "ShipHealthHUD",
            typeof(RectTransform)
        );
        hudRoot.transform.SetParent(canvas.transform, false);

        RectTransform hudRect = hudRoot.GetComponent<RectTransform>();
        hudRect.anchorMin = new Vector2(0.5f, 1f);
        hudRect.anchorMax = new Vector2(0.5f, 1f);
        hudRect.pivot = new Vector2(0.5f, 1f);
        hudRect.anchoredPosition = new Vector2(0f, -18f);
        hudRect.sizeDelta = new Vector2(320f, 46f);

        // Slider track.
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
        sliderRect.sizeDelta = new Vector2(0f, 16f);

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

        // Label above the bar.
        GameObject labelObject = new GameObject(
            "Label",
            typeof(RectTransform)
        );
        labelObject.transform.SetParent(hudRoot.transform, false);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.offsetMin = new Vector2(0f, 18f);
        labelRect.offsetMax = Vector2.zero;

        Text label = labelObject.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>(
            "LegacyRuntime.ttf"
        );
        label.fontSize = 18;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.text = "Ship";

        ShipHealthHUD hud = hudRoot.AddComponent<ShipHealthHUD>();
        SetSerializedObject(hud, "healthSlider", slider);
        SetSerializedObject(hud, "label", label);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static GameObject FindSceneObject(Scene scene, string name)
    {
        return scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Where(candidate => candidate.name == name)
            .Select(candidate => candidate.gameObject)
            .FirstOrDefault();
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
}
