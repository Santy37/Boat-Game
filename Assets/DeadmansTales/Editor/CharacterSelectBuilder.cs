using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Wires the simple main-menu character select screen: adds
/// NetworkPlayerCharacterSkin to the player prefab with real Soldier/Orc
/// art references, and builds a CharacterSelectPanel in MainMenu.unity
/// with Soldier/Orc portrait buttons and a Back button.
///
/// Idempotent: safe to run repeatedly.
/// </summary>
public static class CharacterSelectBuilder
{
    private const string MenuPath =
        "Deadman's Tales/Build Character Select Screen";

    private const string PlayerPrefabPath =
        "Assets/DeadmansTales/Prefabs/Player_2D_Network.prefab";

    private const string MainMenuScenePath =
        "Assets/DeadmansTales/Scenes/MainMenu.unity";

    private const string SoldierAnimationFolder =
        "Assets/DeadmansTales/Animations/SoldierPlayer2D";
    private const string OrcAnimationFolder =
        "Assets/DeadmansTales/Animations/OrcBrute2D";
    private const string SoldierIdleSheet =
        "Assets/DeadmansTales/Art_Pixel/Characters/TinyRPG/Soldier/" +
        "Soldier_Idle.png";
    private const string OrcIdleSheet =
        "Assets/DeadmansTales/Art_Pixel/Characters/TinyRPG/Orc/" +
        "Orc_Idle.png";

    [MenuItem(MenuPath)]
    public static void BuildAll()
    {
        WirePlayerPrefabSkinComponent();
        BuildCharacterSelectPanel();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "[Character Select] Player prefab and main menu character " +
            "select screen are wired."
        );
    }

    public static void BuildAllFromCommandLine()
    {
        BuildAll();
    }

    // ------------------------------------------------------------------
    // Player prefab
    // ------------------------------------------------------------------

    private static void WirePlayerPrefabSkinComponent()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);

        try
        {
            Transform gfx = root.transform
                .GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(child => child.name == "GFX");

            if (gfx == null)
            {
                throw new System.InvalidOperationException(
                    "Player prefab has no GFX child."
                );
            }

            SpriteRenderer renderer = gfx.GetComponent<SpriteRenderer>();
            Animator animator = gfx.GetComponent<Animator>();

            NetworkPlayerCharacterSkin skin =
                root.GetComponent<NetworkPlayerCharacterSkin>();
            if (skin == null)
            {
                skin = root.AddComponent<NetworkPlayerCharacterSkin>();
            }

            Sprite soldierSprite = AssetDatabase
                .LoadAllAssetRepresentationsAtPath(SoldierIdleSheet)
                .OfType<Sprite>()
                .OrderBy(sprite => sprite.name)
                .FirstOrDefault();

            Sprite orcSprite = AssetDatabase
                .LoadAllAssetRepresentationsAtPath(OrcIdleSheet)
                .OfType<Sprite>()
                .OrderBy(sprite => sprite.name)
                .FirstOrDefault();

            AnimatorController soldierController =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(
                    SoldierAnimationFolder + "/SoldierPlayer2D.controller"
                );
            AnimatorController orcController =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(
                    OrcAnimationFolder + "/OrcBrute2D.controller"
                );

            SerializedObject serialized = new SerializedObject(skin);
            serialized.FindProperty("gfxRenderer").objectReferenceValue =
                renderer;
            serialized.FindProperty("gfxAnimator").objectReferenceValue =
                animator;
            serialized.FindProperty("soldierIdleSprite")
                .objectReferenceValue = soldierSprite;
            serialized.FindProperty("soldierController")
                .objectReferenceValue = soldierController;
            serialized.FindProperty("orcIdleSprite").objectReferenceValue =
                orcSprite;
            serialized.FindProperty("orcController").objectReferenceValue =
                orcController;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ------------------------------------------------------------------
    // Main menu UI
    // ------------------------------------------------------------------

    private static void BuildCharacterSelectPanel()
    {
        Scene scene = EditorSceneManager.OpenScene(
            MainMenuScenePath,
            OpenSceneMode.Single
        );

        Canvas canvas = scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Canvas>(true))
            .FirstOrDefault(candidate => candidate.isRootCanvas);

        if (canvas == null)
        {
            throw new System.InvalidOperationException(
                "No root canvas found in MainMenu.unity."
            );
        }

        GameObject mainMenuPanel = scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<Transform>(true))
            .Where(child => child.name == "MainMenuPanel")
            .Select(child => child.gameObject)
            .FirstOrDefault();

        Transform existingPanel = canvas.transform.Find(
            "CharacterSelectPanel"
        );
        if (existingPanel != null)
        {
            Object.DestroyImmediate(existingPanel.gameObject);
        }

        GameObject panel = new GameObject(
            "CharacterSelectPanel",
            typeof(RectTransform)
        );
        panel.transform.SetParent(canvas.transform, false);

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        Image background = panel.AddComponent<Image>();
        background.color = new Color(0.05f, 0.05f, 0.08f, 0.92f);

        CreateLabel(
            panel.transform,
            "TitleLabel",
            "CHOOSE YOUR CHARACTER",
            36,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, -80f),
            new Vector2(800f, 60f)
        );

        CreateCharacterButton(
            panel.transform,
            "SoldierButton",
            "SOLDIER",
            SoldierIdleSheet,
            new Vector2(-180f, 0f)
        );
        CreateCharacterButton(
            panel.transform,
            "OrcButton",
            "ORC",
            OrcIdleSheet,
            new Vector2(180f, 0f)
        );

        CreateTextButton(
            panel.transform,
            "BackButton",
            "BACK",
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0f, 80f),
            new Vector2(240f, 60f)
        );

        panel.SetActive(false);

        if (mainMenuPanel != null)
        {
            Transform existingCharacterButton =
                mainMenuPanel.transform.Find("CharacterButton");
            if (existingCharacterButton != null)
            {
                Object.DestroyImmediate(existingCharacterButton.gameObject);
            }

            CreateTextButton(
                mainMenuPanel.transform,
                "CharacterButton",
                "CHARACTER",
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 140f),
                new Vector2(280f, 60f)
            );
        }
        else
        {
            Debug.LogWarning(
                "[Character Select] MainMenuPanel was not found; add the " +
                "CHARACTER button manually to open the new panel."
            );
        }

        MainMenuManager manager = scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<MainMenuManager>(true))
            .FirstOrDefault();

        if (manager != null)
        {
            SerializedObject serializedManager =
                new SerializedObject(manager);
            serializedManager
                .FindProperty("characterSelectPanel")
                .objectReferenceValue = panel;
            serializedManager.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void CreateCharacterButton(
        Transform parent,
        string objectName,
        string label,
        string spriteSheetPath,
        Vector2 anchoredPosition
    )
    {
        GameObject buttonObject = new GameObject(
            objectName,
            typeof(RectTransform)
        );
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(260f, 260f);

        Image background = buttonObject.AddComponent<Image>();
        background.color = new Color(0.16f, 0.17f, 0.22f, 1f);

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = background;

        Sprite portrait = AssetDatabase
            .LoadAllAssetRepresentationsAtPath(spriteSheetPath)
            .OfType<Sprite>()
            .OrderBy(sprite => sprite.name)
            .FirstOrDefault();

        GameObject portraitObject = new GameObject(
            "Portrait",
            typeof(RectTransform)
        );
        portraitObject.transform.SetParent(buttonObject.transform, false);

        RectTransform portraitRect =
            portraitObject.GetComponent<RectTransform>();
        portraitRect.anchorMin = new Vector2(0.5f, 0.6f);
        portraitRect.anchorMax = new Vector2(0.5f, 0.6f);
        portraitRect.anchoredPosition = Vector2.zero;
        portraitRect.sizeDelta = new Vector2(140f, 140f);

        Image portraitImage = portraitObject.AddComponent<Image>();
        portraitImage.sprite = portrait;
        portraitImage.preserveAspect = true;

        CreateLabel(
            buttonObject.transform,
            "Label",
            label,
            28,
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0f, 30f),
            new Vector2(240f, 50f)
        );
    }

    private static void CreateTextButton(
        Transform parent,
        string objectName,
        string label,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 sizeDelta
    )
    {
        GameObject buttonObject = new GameObject(
            objectName,
            typeof(RectTransform)
        );
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        Image background = buttonObject.AddComponent<Image>();
        background.color = new Color(0.16f, 0.17f, 0.22f, 1f);

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = background;

        CreateLabel(
            buttonObject.transform,
            "Label",
            label,
            26,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
    }

    private static void CreateLabel(
        Transform parent,
        string objectName,
        string text,
        int fontSize,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 sizeDelta
    )
    {
        GameObject labelObject = new GameObject(
            objectName,
            typeof(RectTransform)
        );
        labelObject.transform.SetParent(parent, false);

        RectTransform rect = labelObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        Text label = labelObject.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>(
            "LegacyRuntime.ttf"
        );
        label.fontSize = fontSize;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.text = text;
    }
}
