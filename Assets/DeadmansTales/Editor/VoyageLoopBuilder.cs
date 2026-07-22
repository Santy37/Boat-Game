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
using UnityEngine.Tilemaps;

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

    /// <summary>
    /// How far the gunner stands from the CENTRE of the cannon art, in world
    /// units. The art is two cells tall, so 1.6 puts them 0.6 clear of it —
    /// close enough to read as manning the gun, far enough that their 0.35
    /// collider never touches its solid box.
    /// </summary>
    private const float GunnerStandDistance = 1.6f;

    /// <summary>The tilemap the cannons are actually painted on.</summary>
    private const string ShipPropLayer = "Ship_prop";

    /// <summary>
    /// Lines every cannon's colliders and stand point up with the cannon a
    /// player can actually SEE.
    ///
    /// The cannons have no SpriteRenderer. What you see on deck is painted
    /// into the Ship_prop tilemap, and the ShipCannon objects are separate
    /// empties positioned by hand — so the art and the interaction were never
    /// tied together, and on two of the four they drifted apart:
    ///
    ///     cannon        art cells      art centre   object   error
    ///     Cannon        y 15..17         16.00      15.99      ok
    ///     Cannon (1)    y 15..17         16.00      16.11     0.11
    ///     Cannon (2)    y  9..11         10.00       9.58     0.42
    ///     Cannon (3)    y  9..11         10.00       9.34     0.66
    ///
    /// The aft pair sits below its own gun. Their trigger landed ON the
    /// painted cannon and their solid box on the empty rail beneath it, so
    /// the prompt appeared while you stood in the barrel and the seat put
    /// you inside it. Sizing the boxes off the OBJECT — which is what an
    /// earlier pass did, trimming them by half — cannot fix that, because
    /// the object is the thing in the wrong place.
    ///
    /// So everything here is measured from the art instead. For each cannon
    /// the painted cells are read out of the tilemap, and:
    ///
    ///   - the solid box becomes exactly the art's footprint, so the gun you
    ///     see is the gun you bump into;
    ///   - the gunner stands <see cref="GunnerStandDistance"/> from the art's
    ///     centre on the side away from the muzzle;
    ///   - the trigger is centred on that standing spot rather than on the
    ///     gun, so the prompt appears where a gunner would be.
    /// </summary>
    private static void SeatTheGunners(Scene scene)
    {
        Tilemap art = scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Tilemap>(true))
            .FirstOrDefault(map => map.name == ShipPropLayer);

        if (art == null)
        {
            Debug.LogWarning(
                $"[Voyage Loop] No '{ShipPropLayer}' tilemap; the cannons " +
                "cannot be aligned to their art."
            );
            return;
        }

        ShipCannon[] cannons = scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<ShipCannon>(true))
            .ToArray();

        foreach (ShipCannon cannon in cannons)
        {
            if (!TryFindArtBounds(art, cannon.transform.position, out Bounds gun))
            {
                Debug.LogWarning(
                    $"[Voyage Loop] Found no painted cannon under " +
                    $"'{cannon.name}'; leaving it alone."
                );
                continue;
            }

            SerializedObject serialized = new SerializedObject(cannon);

            Transform muzzle = serialized
                .FindProperty("muzzle")?.objectReferenceValue as Transform;
            Transform stand = serialized
                .FindProperty("standPoint")?.objectReferenceValue as Transform;

            // Which end of the art the gun fires from. Measured against the
            // art's centre, not the object's — the object is what drifted.
            float muzzleSide = muzzle != null
                ? muzzle.position.y - gun.center.y
                : serialized.FindProperty("facing").vector2Value.y;

            float gunSide = muzzleSide >= 0f ? 1f : -1f;

            Vector2 artLocal = cannon.transform
                .InverseTransformPoint(gun.center);
            Vector2 seatLocal = artLocal +
                new Vector2(0f, -gunSide * GunnerStandDistance);

            foreach (BoxCollider2D box in cannon.GetComponents<BoxCollider2D>())
            {
                if (box.isTrigger)
                {
                    box.offset = seatLocal;
                    box.size = new Vector2(1.2f, 1.2f);
                }
                else
                {
                    box.offset = artLocal;
                    box.size = gun.size;
                }
            }

            if (stand != null)
            {
                stand.localPosition = new Vector3(
                    seatLocal.x,
                    seatLocal.y,
                    stand.localPosition.z
                );
            }

            Debug.Log(
                $"[Voyage Loop] '{cannon.name}': art centred at " +
                $"{gun.center.x:0.00},{gun.center.y:0.00} (object is at " +
                $"{cannon.transform.position.y:0.00}); gun box {gun.size.x:0.0}" +
                $"x{gun.size.y:0.0}, gunner at " +
                $"{cannon.transform.TransformPoint(seatLocal).y:0.00}."
            );
        }

        Debug.Log($"[Voyage Loop] {cannons.Length} cannons aligned to their art.");
    }

    /// <summary>
    /// Finds the painted cannon under a cannon object: the run of filled
    /// cells in its own column, walked outwards from the cell the object
    /// stands in. Returns the run's world bounds.
    /// </summary>
    private static bool TryFindArtBounds(
        Tilemap art,
        Vector3 near,
        out Bounds bounds
    )
    {
        bounds = default;

        Vector3Int origin = art.WorldToCell(near);

        // The object can sit just off its art, so start from whichever of
        // the cell it is in or the one above has paint in it.
        if (!art.HasTile(origin))
        {
            if (art.HasTile(origin + Vector3Int.up))
            {
                origin += Vector3Int.up;
            }
            else if (art.HasTile(origin + Vector3Int.down))
            {
                origin += Vector3Int.down;
            }
            else
            {
                return false;
            }
        }

        int bottom = origin.y;
        while (art.HasTile(new Vector3Int(origin.x, bottom - 1, origin.z)))
        {
            bottom--;
        }

        int top = origin.y;
        while (art.HasTile(new Vector3Int(origin.x, top + 1, origin.z)))
        {
            top++;
        }

        Vector3 min = art.CellToWorld(new Vector3Int(origin.x, bottom, origin.z));
        Vector3 max = art.CellToWorld(
            new Vector3Int(origin.x + 1, top + 1, origin.z)
        );

        bounds = new Bounds(
            new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, 0f),
            new Vector3(max.x - min.x, max.y - min.y, 0f)
        );

        return true;
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
    /// Strips every EventSystem from a scene, for the case where another
    /// branch owns the one that should be there. Removes the whole object
    /// when it carries nothing else, so no empty husk is left behind in the
    /// hierarchy for someone to wonder about later.
    /// </summary>
    private static void RemoveEventSystems(Scene scene)
    {
        EventSystem[] found = scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<EventSystem>(true))
            .ToArray();

        foreach (EventSystem system in found)
        {
            GameObject owner = system.gameObject;

            bool onlyTheEventSystem = owner.GetComponents<Component>()
                .All(part =>
                    part is Transform ||
                    part is EventSystem ||
                    part is BaseInputModule);

            if (onlyTheEventSystem && owner.transform.childCount == 0)
            {
                Object.DestroyImmediate(owner);
            }
            else
            {
                Object.DestroyImmediate(system);
            }
        }

        if (found.Length > 0)
        {
            Debug.Log(
                $"[Voyage Loop] Removed {found.Length} EventSystem(s) from " +
                $"'{scene.name}'; another branch owns that fix."
            );
        }
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

        // No EventSystem here, deliberately.
        //
        // This scene had none, so the pause menu drew and ignored you, and an
        // earlier pass added one. Shay fixed the same gap on the UI branch,
        // in the same scene, as part of their pause-menu prefab — and two
        // EventSystems in one scene is a Unity warning and flaky UI input, so
        // only one of the two fixes can survive. UI is their lane, and their
        // version arrives attached to the menu it serves, so ours goes.
        //
        // Until NEWUI merges, the island's pause menu is unresponsive again.
        // That is the known cost of standing down, not an oversight.
        RemoveEventSystems(scene);

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
