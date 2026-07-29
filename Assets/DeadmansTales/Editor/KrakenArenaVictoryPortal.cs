using DeadmansTales.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Repair step for the arena's victory portal.
///
/// The portal started life as "PostOceanIslandPortal" -- a plain copy of the
/// boat scene's ocean-loop portal inherited from KrakenArenaBuilder's original
/// Save-As, with no gating and no visual, looping straight back out to another
/// island. Kraken_Arena_2D.unity now ships with it already renamed, gated on
/// the kraken (and any enemy ships) being defeated, and set to complete the
/// run instead of loading a next stage, so nobody has to run this by hand.
///
/// Keep it for when that scene wiring gets clobbered -- a bad merge, someone
/// dragging the object around, a Save-As into a new arena variant. It rebuilds
/// the same configuration and re-attaches a gold-tinted copy of the kraken
/// attack's own Whirlpool prefab as the visual. The portal's EXISTING
/// BoxCollider2D always stays the interaction trigger; only a visual child is
/// added alongside it.
///
/// Two things this step does NOT need to touch, because they live in
/// NetworkStagePortal itself rather than in scene wiring: the visual's
/// sortingOrder (set here, in AddPortalVisual, high enough to draw in front
/// of Ship_Hull) and the fact that requireKrakenDefeated being set is what
/// makes NetworkStagePortal hide this visual + its collider until the kraken
/// dies, so the portal does not visibly exist mid-fight.
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

    // The Whirlpool prefab this visual is cloned from defaults to
    // sortingOrder 1 ("above the water, below the ship and boss" -- see
    // KrakenArenaBuilder.BuildWhirlpoolPrefab), which put the portal BEHIND
    // Ship_Hull's Tilemap (sortingOrder 2) instead of over the deck. The
    // portal is a distinct, always-foreground object -- not part of the
    // water/reef layer the attack telegraph lives in -- so it needs its own,
    // higher order here rather than inheriting the prefab's.
    private const int PortalVisualSortingOrder = 12;

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
            // Explicit, rather than inherited from the Whirlpool prefab --
            // see PortalVisualSortingOrder for why. Above the ship (2) and
            // the kraken (10) so the portal always reads in front.
            sr.sortingOrder = PortalVisualSortingOrder;
        }

        Debug.Log(
            "[Victory Portal] Added a recolored Whirlpool as the portal's " +
            $"visual (gold, scale {PortalVisualScale}, sorting order " +
            $"{PortalVisualSortingOrder}), reusing the portal's existing " +
            "BoxCollider2D as the interaction trigger.");
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
