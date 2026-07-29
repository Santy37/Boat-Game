using DeadmansTales.Ship;
using DeadmansTales.UI;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Wires the shared ship-health systems into the kraken arena, and fixes a
/// stale value left behind on the boat scene's own ship.
///
/// KrakenArenaBuilder built Kraken_Arena_2D.unity as a Save-As of
/// Boat_Gameplay_2D.unity "so the ship ... and networking all come for
/// free." That was true at the time, but NetworkShipHealth,
/// NetworkShipSinkMeter, PlayerShipMarker, and the ShipHealthHUD /
/// ShipSinkMeterHUD slider HUD were all added to Boat_Gameplay_2D's Ship
/// AFTER that Save-As, so the arena's copy of Ship never picked them up --
/// ship health does not carry into the boss fight at all right now, and
/// there is no HUD there to show it either.
///
/// Same idempotent-editor-step spirit as KrakenArenaBuilder: safe to re-run.
/// </summary>
public static class KrakenArenaShipHealthWiring
{
    private const string BoatScenePath =
        "Assets/DeadmansTales/Scenes/Boat/Boat_Gameplay_2D.unity";
    private const string ArenaScenePath =
        "Assets/DeadmansTales/Scenes/Boat/Kraken_Arena_2D.unity";

    private const float ShipMaximumHealth = 500f;
    private const float ShipHealthRestoredPerStageAdvance = 200f;
    // Keep in sync with NetworkShipSinkMeter's own default, or re-running
    // this step would quietly walk the player ship's sink meter back down.
    private const float ShipMaximumSinkLevel = 300f;
    private const float ShipMaximumHealthDrainPerSecond = 8f;

    [MenuItem("Deadman's Tales/Kraken Arena/4. Fix Boat Stage-Advance Heal Value")]
    public static void FixBoatStageAdvanceHeal()
    {
        Scene scene = EditorSceneManager.OpenScene(BoatScenePath, OpenSceneMode.Single);

        NetworkShipHealth shipHealth = FindPlayerShipHealth(scene);
        if (shipHealth == null)
        {
            Debug.LogError(
                "[Ship Health Wiring] No player Ship with NetworkShipHealth " +
                "found in the boat scene.");
            return;
        }

        SerializedObject so = new SerializedObject(shipHealth);
        SerializedProperty heal = so.FindProperty("healthRestoredPerStageAdvance");

        if (heal == null)
        {
            Debug.LogError(
                "[Ship Health Wiring] healthRestoredPerStageAdvance field " +
                "not found on NetworkShipHealth -- has the script changed?");
            return;
        }

        if (Mathf.Approximately(heal.floatValue, ShipHealthRestoredPerStageAdvance))
        {
            Debug.Log(
                "[Ship Health Wiring] Boat Ship's healthRestoredPerStageAdvance " +
                "is already 200; nothing to do.");
            return;
        }

        Debug.Log(
            $"[Ship Health Wiring] Boat Ship's healthRestoredPerStageAdvance " +
            $"was {heal.floatValue} (a stale value from before the 200 HP " +
            "restore was added to the script default) -- setting it to 200.");
        heal.floatValue = ShipHealthRestoredPerStageAdvance;
        so.ApplyModifiedPropertiesWithoutUndo();

        bool ok = EditorSceneManager.SaveScene(scene);
        Debug.Log(ok
            ? "[Ship Health Wiring] Boat scene saved."
            : "[Ship Health Wiring] FAILED to save the boat scene.");
    }

    [MenuItem("Deadman's Tales/Kraken Arena/5. Wire Ship Health Into Arena")]
    public static void WireShipHealthIntoArena()
    {
        Scene arenaScene = EditorSceneManager.OpenScene(ArenaScenePath, OpenSceneMode.Single);

        GameObject ship = FindByName(arenaScene, "Ship");
        if (ship == null)
        {
            Debug.LogError("[Ship Health Wiring] No 'Ship' object found in the arena scene.");
            return;
        }

        WireShipComponents(ship, arenaScene);
        CopyShipHealthHud(arenaScene);

        bool ok = EditorSceneManager.SaveScene(arenaScene);
        Debug.Log(ok
            ? "[Ship Health Wiring] Ship health + HUD wired into the arena."
            : "[Ship Health Wiring] FAILED to save the arena scene.");
    }

    // KrakenHealth is now a server-authoritative NetworkBehaviour (mirrors
    // NetworkShipHealth), which needs a NetworkObject on the same
    // GameObject. Kraken.prefab already exists as a built asset, so this
    // patches it in place -- AddComponent only, via LoadPrefabContents --
    // rather than re-running the full "2. Build Kraken Prefab" step, which
    // would needlessly re-import art and rebuild the Animator/clip assets
    // just to add one component.
    [MenuItem("Deadman's Tales/Kraken Arena/6. Network The Kraken Prefab")]
    public static void NetworkTheKrakenPrefab()
    {
        const string prefabPath = "Assets/DeadmansTales/Prefabs/KrakenArena/Kraken.prefab";

        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        bool changed = false;

        if (prefabRoot.GetComponent<NetworkObject>() == null)
        {
            prefabRoot.AddComponent<NetworkObject>();
            changed = true;
            Debug.Log(
                "[Ship Health Wiring] Added NetworkObject to Kraken.prefab " +
                "-- KrakenHealth requires one now.");
        }
        else
        {
            Debug.Log(
                "[Ship Health Wiring] Kraken.prefab already has a " +
                "NetworkObject; nothing to do there.");
        }

        // Neither the kraken nor NetworkCannonball has a Rigidbody2D. Unity
        // 2D physics only raises trigger callbacks for a collider pair where
        // AT LEAST ONE side has a Rigidbody2D -- two colliders that are both
        // Rigidbody-less never generate OnTriggerEnter2D/OnTriggerStay2D at
        // all, so cannon shots were flying straight through with zero
        // collision events, not just failing to apply damage. Kinematic
        // (not Dynamic) because KrakenStrafe is "purely kinematic" per its
        // own doc comment -- it writes transform.position directly every
        // frame, so this must not add gravity or let physics forces fight
        // that.
        if (prefabRoot.GetComponent<Rigidbody2D>() == null)
        {
            Rigidbody2D body = prefabRoot.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.simulated = true;
            changed = true;
            Debug.Log(
                "[Ship Health Wiring] Added a Kinematic Rigidbody2D to " +
                "Kraken.prefab -- without one, cannon shots pass straight " +
                "through it with zero trigger events (Unity 2D requires at " +
                "least one side of a trigger pair to have a Rigidbody2D).");
        }

        if (changed)
        {
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
        }

        PrefabUtility.UnloadPrefabContents(prefabRoot);

        // Re-open and re-save the arena scene so its already-placed Kraken
        // instance picks up the prefab's new NetworkObject, and so Netcode's
        // own editor tooling gets a chance to backfill the in-scene
        // GlobalObjectIdHash it needs to auto-spawn correctly. Check the
        // Kraken object in the Hierarchy afterward -- if it doesn't show a
        // NetworkObject component in the Inspector, select it once by hand
        // and save the scene again; in-scene prefab-instance hashes
        // occasionally need that nudge.
        Scene arenaScene = EditorSceneManager.OpenScene(ArenaScenePath, OpenSceneMode.Single);
        bool ok = EditorSceneManager.SaveScene(arenaScene);
        Debug.Log(ok
            ? "[Ship Health Wiring] Arena scene re-saved with the networked kraken."
            : "[Ship Health Wiring] FAILED to re-save the arena scene.");
    }

    // Adds (idempotently) the same NetworkObject / NetworkShipSinkMeter /
    // NetworkShipHealth / PlayerShipMarker stack that Boat_Gameplay_2D's own
    // Ship carries, with the same tuning. NetworkShipHealth and
    // NetworkShipSinkMeter both [RequireComponent(typeof(NetworkObject))],
    // and the arena's Ship never had one -- it was Save-As'd before Ship
    // became a networked object at all.
    private static void WireShipComponents(GameObject ship, Scene scene)
    {
        if (ship.GetComponent<NetworkObject>() == null)
        {
            ship.AddComponent<NetworkObject>();
            Debug.Log(
                "[Ship Health Wiring] Added NetworkObject to the arena's " +
                "Ship (missing since the Save-As, before Ship carried " +
                "ship-health at all).");
        }

        if (ship.GetComponent<NetworkShipSinkMeter>() == null)
        {
            NetworkShipSinkMeter sinkMeter = ship.AddComponent<NetworkShipSinkMeter>();
            SerializedObject so = new SerializedObject(sinkMeter);
            so.FindProperty("maximumSinkLevel").floatValue = ShipMaximumSinkLevel;
            so.FindProperty("maximumHealthDrainPerSecond").floatValue =
                ShipMaximumHealthDrainPerSecond;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log(
                "[Ship Health Wiring] Added NetworkShipSinkMeter to the " +
                $"arena's Ship (max {ShipMaximumSinkLevel}, drain " +
                $"{ShipMaximumHealthDrainPerSecond}/s) -- this is what " +
                "actually patches back up between stages.");
        }

        if (ship.GetComponent<NetworkShipHealth>() == null)
        {
            NetworkShipHealth shipHealth = ship.AddComponent<NetworkShipHealth>();
            SerializedObject so = new SerializedObject(shipHealth);
            so.FindProperty("maximumHealth").floatValue = ShipMaximumHealth;
            so.FindProperty("healthRestoredPerStageAdvance").floatValue =
                ShipHealthRestoredPerStageAdvance;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log(
                "[Ship Health Wiring] Added NetworkShipHealth to the " +
                $"arena's Ship (max {ShipMaximumHealth}, +" +
                $"{ShipHealthRestoredPerStageAdvance} per stage advance).");
        }

        if (ship.GetComponent<PlayerShipMarker>() == null)
        {
            ship.AddComponent<PlayerShipMarker>();
            Debug.Log("[Ship Health Wiring] Added PlayerShipMarker to the arena's Ship.");
        }

        PlayerShipMarker marker = ship.GetComponent<PlayerShipMarker>();
        SerializedObject markerSo = new SerializedObject(marker);
        SerializedProperty hitboxProp = markerSo.FindProperty("hitbox");

        if (hitboxProp != null && hitboxProp.objectReferenceValue == null)
        {
            Collider2D hitbox = FindShipHitbox(scene);
            if (hitbox != null)
            {
                hitboxProp.objectReferenceValue = hitbox;
                markerSo.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log("[Ship Health Wiring] Wired PlayerShipMarker.hitbox to ShipHitBox.");
            }
            else
            {
                Debug.LogWarning(
                    "[Ship Health Wiring] No 'ShipHitBox' collider found in " +
                    "the arena; PlayerShipMarker.hitbox left unset.");
            }
        }
    }

    // Duplicates the boat scene's ship-health HUD (Canvas, with
    // ShipHealthSlider / ShipSinkSlider / ShipHealthLabel / ShipSinkLabel /
    // ShipHUD as its children) into the arena via an additive scene load +
    // Instantiate, rather than hand-building new UI -- that way every
    // internal Slider/Text reference on ShipHealthHUD/ShipSinkMeterHUD comes
    // along correctly instead of being rebuilt (and possibly mis-wired) by
    // hand. ShipHealthHUD/ShipSinkMeterHUD both auto-find the ship via
    // FindFirstObjectByType<PlayerShipMarker>(), so once this HUD exists in
    // the arena it needs no further wiring to the Ship added above.
    private static void CopyShipHealthHud(Scene arenaScene)
    {
        foreach (GameObject go in arenaScene.GetRootGameObjects())
        {
            if (go.name == "Canvas" && go.GetComponentInChildren<ShipHealthHUD>(true) != null)
            {
                Debug.Log(
                    "[Ship Health Wiring] Arena already has the ship-health " +
                    "HUD Canvas; skipping copy.");
                return;
            }
        }

        Scene boatScene = EditorSceneManager.OpenScene(BoatScenePath, OpenSceneMode.Additive);

        GameObject sourceCanvas = FindByName(boatScene, "Canvas");
        if (sourceCanvas == null || sourceCanvas.GetComponentInChildren<ShipHealthHUD>(true) == null)
        {
            Debug.LogError(
                "[Ship Health Wiring] Could not find the ship-health HUD " +
                "Canvas in the boat scene.");
            EditorSceneManager.CloseScene(boatScene, true);
            return;
        }

        GameObject copy = Object.Instantiate(sourceCanvas);
        copy.name = sourceCanvas.name;
        SceneManager.MoveGameObjectToScene(copy, arenaScene);

        EditorSceneManager.CloseScene(boatScene, true);

        Debug.Log("[Ship Health Wiring] Copied the ship-health HUD Canvas into the arena.");
    }

    // Same helper KrakenArenaBuilder uses to find the hull hitbox by name.
    private static Collider2D FindShipHitbox(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Collider2D col in root.GetComponentsInChildren<Collider2D>(true))
            {
                if (col.gameObject.name == "ShipHitBox")
                {
                    return col;
                }
            }
        }
        return null;
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

    private static NetworkShipHealth FindPlayerShipHealth(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (PlayerShipMarker marker in root.GetComponentsInChildren<PlayerShipMarker>(true))
            {
                NetworkShipHealth health = marker.GetComponent<NetworkShipHealth>();
                if (health != null)
                {
                    return health;
                }
            }
        }
        return null;
    }
}
