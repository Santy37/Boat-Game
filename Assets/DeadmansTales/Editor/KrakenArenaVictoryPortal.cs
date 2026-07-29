using DeadmansTales.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Turns the arena's leftover "PostOceanIslandPortal" -- a plain copy of the
/// boat scene's ocean-loop portal, inherited from KrakenArenaBuilder's
/// original Save-As -- into the boss arena's final gate. Right now it has NO
/// gating at all and just continues the ocean loop into another island; this
/// makes it require the kraken (and any enemy ships) to be defeated first,
/// and completes the run instead of loading a next stage.
///
/// It also currently has no visual whatsoever -- just an invisible
/// BoxCollider2D trigger -- so this gives it one by repurposing the kraken
/// attack's own Whirlpool prefab (recolored gold) as a child. The portal's
/// EXISTING collider stays the interaction trigger; only a visual is added
/// alongside it, so it stays exactly as gated as before, just visible now
/// and paired with a proper win condition.
///
/// Same idempotent-editor-step spirit as KrakenArenaBuilder /
/// KrakenArenaShipHealthWiring: safe to re-run.
/// </summary>
public static class KrakenArenaVictoryPortal
{
    private const string ArenaScenePath =
        "Assets/DeadmansTales/Scenes/Boat/Kraken_Arena_2D.unity";
    private const string WhirlpoolPrefabPath =
        "Assets/DeadmansTales/Prefabs/KrakenArena/Whirlpool.prefab";

    private const string OldPortalName = "PostOceanIslandPortal";
    private const string NewPortalName = "VictoryPortal";
    private const string VisualChildName = "PortalVisual";

    // A warm gold glow reads as "safe passage / victory," clearly distinct
    // from the attack telegraph's own menacing white-to-red whirlpool, so
    // the two are never confused for each other in the arena.
    private static readonly Color PortalTint = new Color(1f, 0.83f, 0.35f, 0.9f);
    private const float PortalVisualScale = 0.55f;

    [MenuItem("Deadman's Tales/Kraken Arena/7. Wire Victory Portal")]
    public static void WireVictoryPortal()
    {
        Scene scene = EditorSceneManager.OpenScene(ArenaScenePath, OpenSceneMode.Single);

        GameObject portalObject = FindByName(scene, NewPortalName)
            ?? FindByName(scene, OldPortalName);

        if (portalObject == null)
        {
            Debug.LogError(
                $"[Victory Portal] No '{OldPortalName}' or '{NewPortalName}' " +
                "object found in the arena scene.");
            return;
        }

        portalObject.name = NewPortalName;

        NetworkStagePortal portal = portalObject.GetComponent<NetworkStagePortal>();
        if (portal == null)
        {
            Debug.LogError(
                $"[Victory Portal] '{portalObject.name}' has no " +
                "NetworkStagePortal component.");
            return;
        }

        SerializedObject so = new SerializedObject(portal);
        so.FindProperty("requireKrakenDefeated").boolValue = true;
        so.FindProperty("requireAllEnemyShipsDefeated").boolValue = true;
        so.FindProperty("completesRun").boolValue = true;
        so.FindProperty("advanceStage").boolValue = false;
        so.FindProperty("destinationSceneName").stringValue = string.Empty;
        so.ApplyModifiedPropertiesWithoutUndo();

        AddPortalVisual(portalObject);

        bool ok = EditorSceneManager.SaveScene(scene);
        Debug.Log(ok
            ? "[Victory Portal] Arena portal reconfigured as the victory portal."
            : "[Victory Portal] FAILED to save the arena scene.");
    }

    private static void AddPortalVisual(GameObject portalObject)
    {
        Transform existing = portalObject.transform.Find(VisualChildName);
        if (existing != null)
        {
            Object.DestroyImmediate(existing.gameObject);
        }

        GameObject whirlpoolPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(WhirlpoolPrefabPath);
        if (whirlpoolPrefab == null)
        {
            Debug.LogWarning(
                "[Victory Portal] Whirlpool prefab not found at " +
                $"'{WhirlpoolPrefabPath}'; portal left without a visual.");
            return;
        }

        GameObject visual =
            (GameObject)PrefabUtility.InstantiatePrefab(whirlpoolPrefab);
        visual.name = VisualChildName;
        visual.transform.SetParent(portalObject.transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localScale = Vector3.one * PortalVisualScale;

        SpriteRenderer sr = visual.GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
        {
            sr.color = PortalTint;
        }

        Debug.Log(
            "[Victory Portal] Added a recolored Whirlpool as the portal's " +
            $"visual (gold, scale {PortalVisualScale}), reusing the " +
            "portal's existing BoxCollider2D as the interaction trigger.");
    }

    private static GameObject FindByName(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == name)
            {
                return root;
            }
            Transform found = root.transform.Find(name);
            if (found != null)
            {
                return found.gameObject;
            }
        }
        return null;
    }
}
