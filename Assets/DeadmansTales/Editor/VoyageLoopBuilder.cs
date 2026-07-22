using System.Collections.Generic;
using System.Linq;
using DeadmansTales.Networking;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Makes the run playable from the main menu all the way back around to the
/// ship, on top of the teammate's boat scene.
///
/// WHY THIS EXISTS. The boat scene on this branch is Michelin's, taken whole
/// so none of his helm or cannon work is diluted. His copy branched before
/// the island exit was added, so it arrived with three things main had and it
/// does not: the portal to the island, a fourth spawn marker, and an
/// EventSystem. Rather than hand-editing his scene YAML and hoping, this
/// re-adds them from code, positioned against his ship rather than the old
/// one — everything here is ADDITIVE, and nothing he built is moved.
///
/// THE LOOP. Even with the boat fixed, the voyage was not a loop: the island
/// sent players back to the ship and nothing anywhere led to the market, so
/// the shop was reachable only from the main menu's level select. The coins
/// dropped on the hostile island had nowhere to be spent. The island now
/// leads to the market and the market's rowboat already sails to the ship:
///
///     Menu -> Lobby -> Ship -> Ocean Island -> Port Market -> Ship -> ...
///
/// Idempotent: run it as often as you like.
/// </summary>
public static class VoyageLoopBuilder
{
    private const string MenuPath = "Deadman's Tales/Build Voyage Loop";

    private const string BoatScenePath =
        "Assets/DeadmansTales/Scenes/Boat_Gameplay_2D.unity";
    private const string IslandScenePath =
        "Assets/DeadmansTales/Scenes/Island_After_Ocean_01_2D.unity";
    private const string ShopScenePath =
        "Assets/DeadmansTales/Scenes/Island_Shop_2D.unity";

    private const string IslandSceneName = "Island_After_Ocean_01_2D";
    private const string ShopSceneName = "Island_Shop_2D";

    private const string PortalName = "PostOceanIslandPortal";

    /// <summary>
    /// The bow of the teammate's ship: the deck runs from the helm at the
    /// stern (x -6.6) out past the cannons (x 4.5) to a tapering bow around
    /// x 15, and this sits in the open planking beyond the last cannon and
    /// the last spawn. Walking to the front of the ship to go ashore is the
    /// reading we want, and it keeps the portal's trigger off every station
    /// he built.
    /// </summary>
    private static readonly Vector3 PortalPosition =
        new Vector3(11.5f, 13f, 0f);

    /// <summary>
    /// Deliberately smaller than the 7x5 the old boat scene used. That box
    /// covered the whole bow, so the prompt appeared several strides before
    /// you arrived; the market stalls had the same fault and it reads as the
    /// game being twitchy rather than generous.
    /// </summary>
    private static readonly Vector2 PortalTriggerSize = new Vector2(4f, 3f);

    private const int RequiredSpawnMarkers = 4;

    [MenuItem(MenuPath)]
    public static void BuildAll()
    {
        BuildBoatScene();
        PointIslandAtMarket();
        EnsureBuildSettings();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "[Voyage Loop] Menu -> Lobby -> Ship -> Ocean Island -> " +
            "Port Market -> Ship."
        );

        // The portal created here is an in-scene NetworkObject and still
        // needs a unique GlobalObjectIdHash. That repair deliberately
        // refuses to run in the same pass as scene edits, so it is a
        // separate step:
        //   -executeMethod NetworkSceneIdentityRepair.RepairFromCommandLine
    }

    public static void BuildAllFromCommandLine()
    {
        BuildAll();
    }

    private static void BuildBoatScene()
    {
        Scene scene = EditorSceneManager.OpenScene(
            BoatScenePath,
            OpenSceneMode.Single
        );

        NameSpawnMarkers(scene);
        SeatTheGunners(scene);
        ConnectHelmToShip(scene);
        EnsureIslandPortal(scene);
        EnsureEventSystem(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    /// <summary>
    /// Gives the spawn markers the names the rest of the game looks them up
    /// by.
    ///
    /// Nothing was actually missing from his deck — all four markers are
    /// there and all four are on planking. One of them simply kept the
    /// component's default name, "PlayerSpawnPoint2D", so the fourth player
    /// had a perfectly good spot that no lookup could find. Markers already
    /// named correctly are left alone, so this never shuffles players
    /// between spots on a rebuild.
    /// </summary>
    private static void NameSpawnMarkers(Scene scene)
    {
        List<PlayerSpawnPoint2D> markers = scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<PlayerSpawnPoint2D>(true))
            .ToList();

        if (markers.Count != RequiredSpawnMarkers)
        {
            Debug.LogWarning(
                $"[Voyage Loop] The boat scene has {markers.Count} spawn " +
                $"markers; {RequiredSpawnMarkers} are expected. Naming what " +
                "is there, but a player may have nowhere to stand."
            );
        }

        HashSet<string> wanted = new HashSet<string>(
            Enumerable
                .Range(0, RequiredSpawnMarkers)
                .Select(index => $"PlayerSpawn_{index}")
        );

        HashSet<string> taken = new HashSet<string>(
            markers
                .Select(marker => marker.name)
                .Where(name => wanted.Contains(name))
        );

        int renamed = 0;

        foreach (PlayerSpawnPoint2D marker in markers)
        {
            if (taken.Contains(marker.name))
            {
                continue;
            }

            string free = wanted.FirstOrDefault(name => !taken.Contains(name));

            if (free == null)
            {
                continue;
            }

            Debug.Log(
                $"[Voyage Loop] Spawn marker '{marker.name}' at " +
                $"{marker.transform.position} renamed to '{free}'."
            );

            marker.name = free;
            taken.Add(free);
            renamed++;
        }

        Debug.Log(
            $"[Voyage Loop] {markers.Count} spawn markers, {renamed} renamed."
        );
    }

    // The helm and the four cannons are NOT missing their interaction
    // triggers, despite what their own Awake logs claim. Each carries two
    // BoxCollider2Ds -- a solid one for the body you bump into and a trigger
    // offset towards the standing spot -- and the scene is right. The check
    // in the scripts is what is wrong, and it is fixed there rather than by
    // bolting a third collider onto his objects here.

    /// <summary>How far from a cannon its gunner stands, in world units.</summary>
    private const float GunnerStandDistance = 0.85f;

    /// <summary>
    /// Makes the cannons seatable: trims each gun's solid collider back to
    /// the gun, and stands the gunner where a gunner belongs.
    ///
    /// Manning a cannon was throwing the player across the deck, and the
    /// numbers say why. Each cannon's SOLID box is 1 x 2 centred on the gun,
    /// so it spans local y -1 to +1 — a two-metre block of deck for a piece
    /// of artillery about a metre long. The stand points sit at -1.34 and
    /// -1.35 on the two forward guns and +1.10 and +1.12 on the two aft
    /// ones, and the player's own collider is a circle of radius 0.35. So
    /// seating a gunner at an aft cannon dropped them 0.25 units INSIDE the
    /// gun's solid box; Unity did the only thing it can with two overlapping
    /// solid bodies and shoved them out. The forward pair sat right on the
    /// boundary and only sometimes escaped it.
    ///
    /// That same 1 x 2 box also swallows the interaction trigger beneath it
    /// (solid -1..+1 against trigger -1.5..-0.5), which is the overlap you
    /// can see when the gizmos are on.
    ///
    /// Two changes, both derived from the gun's own muzzle so they hold for
    /// any cannon pointing any direction:
    ///
    ///   - the solid box keeps its width but loses the half BEHIND the gun,
    ///     the half that was never the cannon in the first place — only the
    ///     muzzle side stays solid;
    ///   - the stand point is pulled in to a consistent 0.85 units, which
    ///     puts the gunner snug against the breech instead of a body-length
    ///     away, and leaves 0.35 of clearance on both sides inside the
    ///     1 x 1 trigger.
    /// </summary>
    private static void SeatTheGunners(Scene scene)
    {
        ShipCannon[] cannons = scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<ShipCannon>(true))
            .ToArray();

        foreach (ShipCannon cannon in cannons)
        {
            SerializedObject serialized = new SerializedObject(cannon);

            Transform muzzle = serialized
                .FindProperty("muzzle")?.objectReferenceValue as Transform;
            Transform stand = serialized
                .FindProperty("standPoint")?.objectReferenceValue as Transform;

            // Which way the gun points. The muzzle is the ground truth; the
            // 'facing' field is only a fallback for a cannon without one.
            float muzzleSide = muzzle != null
                ? muzzle.localPosition.y
                : serialized.FindProperty("facing").vector2Value.y;

            float gunSide = muzzleSide >= 0f ? 1f : -1f;

            foreach (BoxCollider2D box in cannon.GetComponents<BoxCollider2D>())
            {
                if (box.isTrigger || box.size.y <= 1f)
                {
                    continue;
                }

                float height = box.size.y * 0.5f;

                box.size = new Vector2(box.size.x, height);
                box.offset = new Vector2(
                    box.offset.x,
                    box.offset.y + gunSide * height * 0.5f
                );

                Debug.Log(
                    $"[Voyage Loop] '{cannon.name}' body collider trimmed to " +
                    $"{box.size} at {box.offset}; the gunner's half of the " +
                    "deck is walkable again."
                );
            }

            if (stand == null)
            {
                continue;
            }

            Vector3 local = stand.localPosition;
            stand.localPosition = new Vector3(
                local.x,
                -gunSide * GunnerStandDistance,
                local.z
            );

            Debug.Log(
                $"[Voyage Loop] '{cannon.name}' gunner moved from " +
                $"y {local.y:0.00} to {stand.localPosition.y:0.00}."
            );
        }

        Debug.Log($"[Voyage Loop] {cannons.Length} cannons made seatable.");
    }

    /// <summary>
    /// Hands the helm the ship it is supposed to steer.
    ///
    /// ShipHelm moves whatever Transform is in its 'ship' field, and that
    /// field is empty in the scene. Everything else about steering works —
    /// you can walk up, press E, take the wheel, watch the camera pull back
    /// — and then WASD does nothing at all, because every line that would
    /// move the ship sits behind a null check that never passes. Taking the
    /// helm of a ship that cannot move is a worse bug than not being able to
    /// take it.
    ///
    /// The helm is a child of the ship root, so the root is what it steers.
    /// </summary>
    private static void ConnectHelmToShip(Scene scene)
    {
        ShipHelm[] helms = scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<ShipHelm>(true))
            .ToArray();

        foreach (ShipHelm helm in helms)
        {
            SerializedObject serialized = new SerializedObject(helm);
            SerializedProperty property = serialized.FindProperty("ship");

            if (property == null || property.objectReferenceValue != null)
            {
                continue;
            }

            Transform shipRoot = helm.transform.root;

            if (shipRoot == null || shipRoot == helm.transform)
            {
                Debug.LogWarning(
                    $"[Voyage Loop] '{helm.name}' is not parented under a " +
                    "ship, so there is nothing for it to steer."
                );
                continue;
            }

            property.objectReferenceValue = shipRoot;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log(
                $"[Voyage Loop] '{helm.name}' now steers '{shipRoot.name}'."
            );
        }
    }

    /// <summary>
    /// Puts the island exit back on the ship. Without it the boat is a dead
    /// end: you can sail, aim and fire, and never get off.
    /// </summary>
    private static void EnsureIslandPortal(Scene scene)
    {
        NetworkStagePortal portal = scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<NetworkStagePortal>(true))
            .FirstOrDefault();

        if (portal == null)
        {
            GameObject created = new GameObject(PortalName);
            created.transform.position = PortalPosition;

            created.AddComponent<NetworkObject>();

            BoxCollider2D trigger = created.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.size = PortalTriggerSize;

            portal = created.AddComponent<NetworkStagePortal>();

            Debug.Log(
                $"[Voyage Loop] Added the island exit at {PortalPosition}."
            );
        }

        SetSerializedString(
            portal,
            "destinationSceneName",
            IslandSceneName
        );

        // There are no enemies to clear on the ship and nothing to generate,
        // so neither gate may hold the crew aboard.
        SetSerializedBool(portal, "requireAllEnemiesDefeated", false);
        SetSerializedBool(portal, "requireGenerationComplete", false);
        SetSerializedBool(portal, "advanceStage", true);
        SetSerializedBool(portal, "allowRepeatedInteraction", false);
        SetSerializedFloat(portal, "additionalServerRange", 0.25f);
    }

    /// <summary>
    /// The pause menu and every other uGUI panel need an EventSystem to
    /// receive a click. His scene has none, so on this branch the menu would
    /// draw and then ignore you.
    /// </summary>
    private static void EnsureEventSystem(Scene scene)
    {
        bool present = scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<EventSystem>(true))
            .Any();

        if (present)
        {
            return;
        }

        GameObject created = new GameObject("EventSystem");
        created.AddComponent<EventSystem>();

        // The rest of the game's scenes use the Input System module, and the
        // project runs with both input backends enabled.
        created.AddComponent<InputSystemUIInputModule>();

        Debug.Log("[Voyage Loop] Added the missing EventSystem.");
    }

    /// <summary>
    /// Sends the hostile island's exit to the market instead of straight
    /// back to the ship.
    ///
    /// This is what turns three scenes into a run. The island is where coins
    /// are plundered and the market is the only place to spend them, but
    /// nothing led there — the shop existed solely as a level-select entry.
    /// The market's rowboat already sails back to the ship, so re-pointing
    /// this one portal closes the circle.
    /// </summary>
    private static void PointIslandAtMarket()
    {
        Scene scene = EditorSceneManager.OpenScene(
            IslandScenePath,
            OpenSceneMode.Single
        );

        NetworkStagePortal portal = scene
            .GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<NetworkStagePortal>(true))
            .FirstOrDefault();

        if (portal == null)
        {
            Debug.LogWarning(
                "[Voyage Loop] The hostile island has no stage portal; the " +
                "crew would be stranded there."
            );
            return;
        }

        SetSerializedString(portal, "destinationSceneName", ShopSceneName);
        SetSerializedBool(portal, "advanceStage", true);

        // The island never had one either, so the pause menu was dead on the
        // one scene you most want to leave.
        EnsureEventSystem(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log(
            "[Voyage Loop] The island now lands the crew at the Port Market."
        );
    }

    private static void EnsureBuildSettings()
    {
        string[] required =
        {
            "Assets/DeadmansTales/Scenes/MainMenu.unity",
            "Assets/DeadmansTales/Scenes/Lobby_Island_2D.unity",
            BoatScenePath,
            IslandScenePath,
            ShopScenePath,
        };

        List<EditorBuildSettingsScene> scenes =
            EditorBuildSettings.scenes.ToList();

        bool changed = false;

        foreach (string path in required)
        {
            EditorBuildSettingsScene entry =
                scenes.FirstOrDefault(scene => scene.path == path);

            if (entry == null)
            {
                scenes.Add(new EditorBuildSettingsScene(path, true));
                changed = true;
                continue;
            }

            if (!entry.enabled)
            {
                entry.enabled = true;
                changed = true;
            }
        }

        if (changed)
        {
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[Voyage Loop] Build Settings updated.");
        }
    }

    private static void SetSerializedString(
        Object target,
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
        Object target,
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
        Object target,
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
