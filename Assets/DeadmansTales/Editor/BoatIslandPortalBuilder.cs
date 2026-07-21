using System;
using System.Linq;
using DeadmansTales.Networking;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Gives the ship its exit to the post-ocean island.
///
/// The boat scene is the teammate's work and this branch otherwise carries
/// his bytes verbatim, so this builder is deliberately the narrowest thing
/// that closes the gameplay loop: it adds (or repositions) exactly ONE root
/// GameObject named "PostOceanIslandPortal" and touches nothing else — not
/// his tilemap, props, camera, spawns, colliders, or lighting. Without it
/// the island can exit to the ship but the ship has no way back, so the run
/// dead-ends after one voyage.
///
/// If he later ships his own exit, delete this object and disable the
/// builder; nothing else has to be unpicked.
/// </summary>
public static class BoatIslandPortalBuilder
{
    private const string MenuPath = "Deadman's Tales/Add Boat Island Portal";

    private const string BoatScenePath =
        "Assets/DeadmansTales/Scenes/Boat_Gameplay_2D.unity";

    private const string IslandSceneName = "Island_After_Ocean_01_2D";

    private const string PortalObjectName = "PostOceanIslandPortal";

    /// <summary>
    /// Placed by measuring the teammate's own deck collider (centre
    /// ~(3.2, 12.7), size ~24.5 x 6.3) rather than by hard-coded world
    /// coordinates, so the trigger follows his ship if he reshapes it.
    ///
    /// This sits over the bow half of the deck. Because the trigger is
    /// invisible it is deliberately generous — walking toward the front
    /// of the ship has to be enough to raise the prompt, since there is
    /// no marker to aim at.
    /// </summary>
    private const float BowInset = 4f;

    private const float DeckHeightOffset = 0.26f;

    private static readonly Vector2 TriggerSize = new Vector2(7f, 5f);

    [MenuItem(MenuPath)]
    public static void BuildAll()
    {
        Scene scene = EditorSceneManager.OpenScene(
            BoatScenePath,
            OpenSceneMode.Single
        );

        Bounds deckBounds = FindDeckBounds(scene);

        GameObject portalObject = scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<Transform>(true))
            .Where(candidate => candidate.name == PortalObjectName)
            .Select(candidate => candidate.gameObject)
            .FirstOrDefault();

        bool created = portalObject == null;

        if (created)
        {
            portalObject = new GameObject(PortalObjectName);
            portalObject.AddComponent<NetworkObject>();
        }

        portalObject.transform.position = new Vector3(
            deckBounds.max.x - BowInset,
            deckBounds.center.y + DeckHeightOffset,
            0f
        );

        BoxCollider2D trigger = portalObject.GetComponent<BoxCollider2D>();
        if (trigger == null)
        {
            trigger = portalObject.AddComponent<BoxCollider2D>();
        }

        // Interactables are discovered with a physics overlap, so the
        // portal needs a collider; a trigger keeps it walk-through.
        trigger.isTrigger = true;
        trigger.size = TriggerSize;

        NetworkStagePortal portal =
            portalObject.GetComponent<NetworkStagePortal>();
        if (portal == null)
        {
            portal = portalObject.AddComponent<NetworkStagePortal>();
        }

        SetSerializedString(
            portal,
            "destinationSceneName",
            IslandSceneName
        );

        // The ship has no seeded generator and no required kill quota, so
        // neither gate applies here; advancing the stage is what makes the
        // island build its stage-2 content for the run's seed.
        SetSerializedBool(portal, "requireGenerationComplete", false);
        SetSerializedBool(portal, "requireAllEnemiesDefeated", false);
        SetSerializedBool(portal, "advanceStage", true);
        SetSerializedBool(portal, "allowRepeatedInteraction", false);
        SetSerializedFloat(portal, "additionalServerRange", 0.25f);

        EnsureNoVisual(portalObject);
        HidePrototypeOverlay(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        // An in-scene NetworkObject is worthless to NGO without a unique,
        // non-zero GlobalObjectIdHash.
        NetworkSceneIdentityRepair.RepairNow();
        AssetDatabase.SaveAssets();

        Debug.Log(
            $"[Boat Portal] {(created ? "Added" : "Repositioned")} the " +
            $"island portal at {portalObject.transform.position} " +
            $"(deck bounds centre {deckBounds.center}, size " +
            $"{deckBounds.size})."
        );
    }

    public static void BuildAllFromCommandLine()
    {
        BuildAll();
    }

    /// <summary>
    /// The scene ships with a fullscreen "PROTOTYPE COMPLETE! / Press
    /// Escape To Go Back To Main Menu" canvas that nothing ever toggles,
    /// so it covers the ship for the whole session and reads as if the
    /// level ended the moment it loads.
    ///
    /// It is deactivated rather than deleted: the object, its text, and
    /// its layout stay exactly as authored, so re-enabling it is one
    /// checkbox if it turns out to be wanted.
    /// </summary>
    private static void HidePrototypeOverlay(Scene scene)
    {
        GameObject overlay = scene
            .GetRootGameObjects()
            .FirstOrDefault(root => root.name == "Prototype");

        if (overlay == null || !overlay.activeSelf)
        {
            return;
        }

        overlay.SetActive(false);

        Debug.Log(
            "[Boat Portal] Hid the always-on PROTOTYPE COMPLETE overlay " +
            "(deactivated, not deleted)."
        );
    }

    private static Bounds FindDeckBounds(Scene scene)
    {
        EdgeCollider2D deckCollider = scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<EdgeCollider2D>(true))
            .FirstOrDefault();

        if (deckCollider == null)
        {
            throw new InvalidOperationException(
                "The boat scene has no deck EdgeCollider2D to place the " +
                "island portal against."
            );
        }

        return deckCollider.bounds;
    }

    /// <summary>
    /// The exit is intentionally invisible. The ship's art is the
    /// teammate's to author, so this adds no sprite of its own — nothing
    /// appears on his deck that he did not put there. Strips a renderer
    /// from an earlier build of this object if one is present.
    /// </summary>
    private static void EnsureNoVisual(GameObject target)
    {
        SpriteRenderer renderer = target.GetComponent<SpriteRenderer>();

        if (renderer == null)
        {
            return;
        }

        UnityEngine.Object.DestroyImmediate(renderer, true);

        Debug.Log(
            "[Boat Portal] Removed the portal's visual; the exit is now " +
            "an invisible trigger."
        );
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
}
