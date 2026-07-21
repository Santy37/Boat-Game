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

    private const string RowboatSpritePath =
        "Assets/DeadmansTales/Art_Pixel/Props/rowboat.png";

    /// <summary>
    /// Placed by measuring the teammate's own deck collider (centre
    /// ~(3.2, 12.7), size ~24.5 x 6.3) rather than by hard-coded world
    /// coordinates, so the portal follows his ship if he reshapes it.
    ///
    /// These offsets land it on the open planking between the aft mast
    /// (cell x 6) and the bow crate (cells x 10-11) — the rowboat sprite
    /// is ~2.9 x 1.4 units, so this is the widest stretch of deck it fits
    /// on without covering any of his props. The bow tip looks tidier but
    /// the hull tapers too sharply there for the sprite to sit inside it.
    /// </summary>
    private const float BowInset = 7f;

    private const float DeckHeightOffset = 0.26f;

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
        trigger.size = new Vector2(3f, 2f);

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

        AddRowboatVisual(portalObject, 20);

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

    private static void AddRowboatVisual(
        GameObject target,
        int sortingOrder
    )
    {
        Sprite rowboatSprite = AssetDatabase
            .LoadAllAssetsAtPath(RowboatSpritePath)
            .OfType<Sprite>()
            .FirstOrDefault();

        if (rowboatSprite == null)
        {
            Debug.LogWarning(
                "[Boat Portal] The rowboat sprite is missing; the portal " +
                "will work but stay invisible."
            );
            return;
        }

        SpriteRenderer renderer = target.GetComponent<SpriteRenderer>();
        if (renderer == null)
        {
            renderer = target.AddComponent<SpriteRenderer>();
        }

        renderer.sprite = rowboatSprite;

        // Above the props tilemap (order 3) so it reads as sitting on deck.
        renderer.sortingOrder = sortingOrder;
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
