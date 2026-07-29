using System.Collections.Generic;
using DeadmansTales.Networking;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Crews the enemy ship with skeletons instead of pirates.
///
/// Builds "ship_skeletonenemy.prefab": the enemy ship's existing crew prefab
/// (ship_basicenemy, which carries the deck-bounded ShipEnemyAI) wearing the
/// skeleton warrior's sprite, animator and stats. Then registers it as a
/// network prefab and points the boat scene's ship spawner at it.
///
/// WHY THIS IS A BUILDER AND NOT AN INSPECTOR EDIT
/// -----------------------------------------------
/// The obvious manual route -- duplicate Enemy_SkeletonWarrior, then strip the
/// components it should not have -- silently edits EVERY skeleton in the game.
/// Enemy_SkeletonWarrior is a prefab VARIANT of basicenemy: a duplicate of it
/// is still a variant pointing at the same base, so removing an inherited
/// component reaches through and removes it from the base instead.
///
/// So this deliberately produces a STANDALONE prefab, not a variant. The
/// source is instantiated and then Unpacked Completely before it is saved,
/// which severs every link to basicenemy. Editing the result afterwards --
/// including deleting components from it -- cannot touch any other enemy.
///
/// Idempotent: safe to run repeatedly.
/// </summary>
public static class ShipSkeletonCrewBuilder
{
    private const string MenuPath =
        "Deadman's Tales/Ship/Build Skeleton Ship Crew";

    // The crew prefab the ship already used: a flat, standalone copy of
    // basicenemy with EnemyAI swapped for the deck-bounded ShipEnemyAI. Its
    // behaviour is what we want to keep; only the look and stats change.
    private const string ShipCrewSourcePrefabPath =
        "Assets/DeadmansTales/Prefabs/ship_basicenemy.prefab";

    // Read-only donor: the sprite, animator controller and tuned stats are
    // lifted off this so the ship crew always matches the skeleton the team
    // has already balanced, rather than duplicating those values here.
    private const string SkeletonPrefabPath =
        "Assets/DeadmansTales/Prefabs/Gameplay/Enemy_SkeletonWarrior.prefab";

    private const string ShipSkeletonCrewPrefabPath =
        "Assets/DeadmansTales/Prefabs/ship_skeletonenemy.prefab";

    private const string BoatScenePath =
        "Assets/DeadmansTales/Scenes/Boat/Boat_Gameplay_2D.unity";

    private const string BootstrapSettingsPath =
        "Assets/DeadmansTales/Resources/Networking/" +
        "DeadmansNetworkBootstrapSettings.asset";

    private const string DefaultNetworkPrefabsPath =
        "Assets/DefaultNetworkPrefabs.asset";

    [MenuItem(MenuPath)]
    public static void BuildAll()
    {
        GameObject crewPrefab = BuildCrewPrefab();

        if (crewPrefab == null)
        {
            return;
        }

        RegisterNetworkPrefab(crewPrefab);
        PointShipSpawnerAtCrew(crewPrefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "[Skeleton Crew Builder] ship_skeletonenemy.prefab is built, " +
            "registered as a network prefab, and wired into the boat scene's " +
            "enemy ship spawner."
        );
    }

    public static void BuildAllFromCommandLine()
    {
        BuildAll();
    }

    // ------------------------------------------------------------------
    // The prefab itself
    // ------------------------------------------------------------------

    private static GameObject BuildCrewPrefab()
    {
        GameObject source =
            AssetDatabase.LoadAssetAtPath<GameObject>(ShipCrewSourcePrefabPath);

        if (source == null)
        {
            Debug.LogError(
                "[Skeleton Crew Builder] Missing " +
                $"{ShipCrewSourcePrefabPath}; nothing to build the crew from."
            );
            return null;
        }

        GameObject skeleton =
            AssetDatabase.LoadAssetAtPath<GameObject>(SkeletonPrefabPath);

        if (skeleton == null)
        {
            Debug.LogError(
                $"[Skeleton Crew Builder] Missing {SkeletonPrefabPath}; run " +
                "the enemy art builder first."
            );
            return null;
        }

        SkeletonLook look = ReadSkeletonLook(skeleton);

        if (look.Sprite == null || look.Controller == null)
        {
            Debug.LogError(
                "[Skeleton Crew Builder] Could not read the skeleton's " +
                "sprite/animator controller off " + SkeletonPrefabPath +
                "; run the enemy art builder first."
            );
            return null;
        }

        EnsureStandalonePrefab(source);

        GameObject root =
            PrefabUtility.LoadPrefabContents(ShipSkeletonCrewPrefabPath);

        try
        {
            ApplySkeletonLook(root, look);

            PrefabUtility.SaveAsPrefabAsset(root, ShipSkeletonCrewPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        // SaveAsPrefabAsset copies the source's serialized NetworkObject
        // verbatim, GlobalObjectIdHash included -- and NGO only recomputes that
        // hash in NetworkObject.OnValidate. Without a forced reimport to run
        // it, the new prefab ships with ship_basicenemy's hash, and NGO
        // identifies network prefabs BY that hash: two prefabs sharing one
        // means the wrong prefab gets spawned.
        AssetDatabase.ImportAsset(
            ShipSkeletonCrewPrefabPath,
            ImportAssetOptions.ForceUpdate |
            ImportAssetOptions.ForceSynchronousImport
        );

        GameObject built = AssetDatabase.LoadAssetAtPath<GameObject>(
            ShipSkeletonCrewPrefabPath
        );

        PersistNetworkIdentity(built);
        VerifyDistinctNetworkIdentity(built, source);

        return built;
    }

    /// <summary>
    /// Writes the regenerated GlobalObjectIdHash to disk.
    ///
    /// The reimport above makes NetworkObject.OnValidate compute the right hash
    /// in memory, but all OnValidate does with it is EditorUtility.SetDirty --
    /// and a dirty flag raised DURING an import is dropped when that import
    /// finishes writing the asset. The freshly computed value therefore never
    /// reached the .prefab file: the asset read back correctly in the editor
    /// while the serialized bytes still held the pirate crew's hash. OnValidate
    /// does not exist in a built player, so a build would have loaded the stale
    /// value and spawned the wrong prefab.
    /// </summary>
    private static void PersistNetworkIdentity(GameObject built)
    {
        NetworkObject networkObject = built != null
            ? built.GetComponent<NetworkObject>() : null;

        if (networkObject == null)
        {
            return;
        }

        // Note there is no "is it already correct?" shortcut here: the loaded
        // asset and the SerializedObject read the SAME in-memory field, which
        // is the value that is already right. Only the bytes on disk are stale,
        // and nothing in the editor API reports that. So this simply forces a
        // write every run -- it is idempotent in effect, and cheap.
        EditorUtility.SetDirty(networkObject);
        AssetDatabase.SaveAssetIfDirty(networkObject);

        Debug.Log(
            "[Skeleton Crew Builder] Persisted GlobalObjectIdHash " +
            $"{networkObject.PrefabIdHash} to " + ShipSkeletonCrewPrefabPath +
            "."
        );
    }

    /// <summary>
    /// Fails loudly rather than shipping a crew prefab that NGO cannot tell
    /// apart from the pirate crew it was copied from.
    /// </summary>
    private static void VerifyDistinctNetworkIdentity(
        GameObject built,
        GameObject source
    )
    {
        NetworkObject builtObject = built != null
            ? built.GetComponent<NetworkObject>() : null;
        NetworkObject sourceObject = source != null
            ? source.GetComponent<NetworkObject>() : null;

        if (builtObject == null)
        {
            Debug.LogError(
                "[Skeleton Crew Builder] The crew prefab has no " +
                "NetworkObject, so it cannot be spawned at runtime."
            );
            return;
        }

        if (sourceObject == null)
        {
            return;
        }

        if (builtObject.PrefabIdHash == sourceObject.PrefabIdHash)
        {
            Debug.LogError(
                "[Skeleton Crew Builder] ship_skeletonenemy still shares " +
                $"GlobalObjectIdHash {builtObject.PrefabIdHash} with " +
                "ship_basicenemy. NGO resolves network prefabs by this hash, " +
                "so the skeleton crew would spawn as pirates. Reimport " +
                "ship_skeletonenemy.prefab (or re-save it from the Inspector) " +
                "to force NetworkObject.OnValidate to regenerate it."
            );
            return;
        }

        Debug.Log(
            "[Skeleton Crew Builder] Network identity is distinct: " +
            $"skeleton crew hash={builtObject.PrefabIdHash}, " +
            $"pirate crew hash={sourceObject.PrefabIdHash}."
        );
    }

    /// <summary>
    /// Creates the crew prefab as a STANDALONE asset with no base prefab.
    ///
    /// PrefabUtility.SaveAsPrefabAsset on a *connected* instance would produce
    /// a variant of the source -- which is exactly the trap this whole builder
    /// exists to avoid. Unpacking Completely first turns the instance into
    /// plain GameObjects, so what gets saved is an independent prefab.
    /// </summary>
    private static void EnsureStandalonePrefab(GameObject source)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(
                ShipSkeletonCrewPrefabPath) != null)
        {
            return;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source);

        try
        {
            PrefabUtility.UnpackPrefabInstance(
                instance,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction
            );

            instance.name = "ship_skeletonenemy";

            PrefabUtility.SaveAsPrefabAsset(
                instance,
                ShipSkeletonCrewPrefabPath
            );
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    private struct SkeletonLook
    {
        public Sprite Sprite;
        public Color Tint;
        public AnimatorController Controller;
        public float MaxHealth;
        public float Damage;
        public float ChaseSpeed;
        public float WanderSpeed;
        public Vector3 LocalScale;
    }

    private static SkeletonLook ReadSkeletonLook(GameObject skeleton)
    {
        SkeletonLook look = new SkeletonLook
        {
            MaxHealth = 120f,
            Damage = 12f,
            ChaseSpeed = 2.6f,
            WanderSpeed = 1.3f,
            LocalScale = skeleton.transform.localScale
        };

        Transform gfx = FindDeepChild(skeleton.transform, "GFX");

        if (gfx != null)
        {
            SpriteRenderer renderer = gfx.GetComponent<SpriteRenderer>();

            if (renderer != null)
            {
                look.Sprite = renderer.sprite;
                look.Tint = renderer.color;
            }

            Animator animator = gfx.GetComponent<Animator>();

            if (animator != null)
            {
                look.Controller =
                    animator.runtimeAnimatorController as AnimatorController;
            }
        }

        // Stats live on the donor too, so a rebalance of the skeleton carries
        // over to its seafaring cousins instead of drifting apart.
        look.MaxHealth = ReadFloat(
            skeleton.GetComponent<Enemy>(), "maxHealth", look.MaxHealth);
        look.Damage = ReadFloat(
            skeleton.GetComponentInChildren<EnemyAttack>(true),
            "damage", look.Damage);

        EnemyAI donorAi = skeleton.GetComponentInChildren<EnemyAI>(true);
        look.ChaseSpeed = ReadFloat(donorAi, "chaseSpeed", look.ChaseSpeed);
        look.WanderSpeed = ReadFloat(donorAi, "wanderSpeed", look.WanderSpeed);

        return look;
    }

    private static void ApplySkeletonLook(GameObject root, SkeletonLook look)
    {
        Transform gfx = FindDeepChild(root.transform, "GFX");

        if (gfx == null)
        {
            Debug.LogError(
                "[Skeleton Crew Builder] The crew prefab has no GFX child to " +
                "reskin."
            );
            return;
        }

        SpriteRenderer renderer = gfx.GetComponent<SpriteRenderer>();

        if (renderer == null)
        {
            renderer = gfx.gameObject.AddComponent<SpriteRenderer>();
        }

        renderer.sprite = look.Sprite;
        renderer.color = look.Tint;

        Animator animator = gfx.GetComponent<Animator>();

        if (animator == null)
        {
            animator = gfx.gameObject.AddComponent<Animator>();
        }

        animator.runtimeAnimatorController = look.Controller;

        EnemyMotionAnimator motion = root.GetComponent<EnemyMotionAnimator>();

        if (motion == null)
        {
            motion = root.AddComponent<EnemyMotionAnimator>();
        }

        SerializedObject serializedMotion = new SerializedObject(motion);
        serializedMotion.FindProperty("animator").objectReferenceValue =
            animator;
        serializedMotion.FindProperty("facingRenderer").objectReferenceValue =
            renderer;
        serializedMotion.ApplyModifiedPropertiesWithoutUndo();

        root.transform.localScale = look.LocalScale;

        WriteFloat(root.GetComponent<Enemy>(), "maxHealth", look.MaxHealth);
        WriteFloat(
            root.GetComponentInChildren<EnemyAttack>(true),
            "damage", look.Damage);

        // ShipEnemyAI, NOT EnemyAI: this crew wanders a ship's deck bounds
        // rather than open ground, which is the whole reason the ship crew has
        // its own prefab in the first place.
        ShipEnemyAI shipAi = root.GetComponentInChildren<ShipEnemyAI>(true);
        WriteFloat(shipAi, "chaseSpeed", look.ChaseSpeed);
        WriteFloat(shipAi, "wanderSpeed", look.WanderSpeed);

        if (shipAi == null)
        {
            Debug.LogWarning(
                "[Skeleton Crew Builder] The crew prefab has no ShipEnemyAI, " +
                "so this crew will not stay on the deck. Check that " +
                ShipCrewSourcePrefabPath + " still carries it."
            );
        }
    }

    // ------------------------------------------------------------------
    // Registration + scene wiring
    // ------------------------------------------------------------------

    /// <summary>
    /// The crew is spawned at runtime via NetworkObject.Spawn, so it has to be
    /// in both prefab registries or joining clients cannot resolve it.
    /// </summary>
    private static void RegisterNetworkPrefab(GameObject crewPrefab)
    {
        DeadmansNetworkBootstrapSettings settings =
            AssetDatabase.LoadAssetAtPath<DeadmansNetworkBootstrapSettings>(
                BootstrapSettingsPath
            );

        if (settings == null)
        {
            Debug.LogWarning(
                $"[Skeleton Crew Builder] Missing {BootstrapSettingsPath}; " +
                "the crew prefab was not registered for online play."
            );
        }
        else
        {
            SerializedObject serialized = new SerializedObject(settings);
            SerializedProperty list =
                serialized.FindProperty("additionalNetworkPrefabs");

            bool alreadyListed = false;

            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue ==
                    crewPrefab)
                {
                    alreadyListed = true;
                    break;
                }
            }

            if (!alreadyListed)
            {
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1)
                    .objectReferenceValue = crewPrefab;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings);
            }
        }

        NetworkPrefabsList prefabsList =
            AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(
                DefaultNetworkPrefabsPath
            );

        if (prefabsList == null)
        {
            Debug.LogWarning(
                $"[Skeleton Crew Builder] Missing {DefaultNetworkPrefabsPath}."
            );
            return;
        }

        if (prefabsList.Contains(crewPrefab))
        {
            return;
        }

        // The list's own Add, rather than growing the SerializedProperty
        // array: SerializedProperty.arraySize++ clones the PREVIOUS entry's
        // values into the new slot, so a hand-built entry silently inherits
        // whatever override fields the last prefab happened to carry.
        prefabsList.Add(new NetworkPrefab { Prefab = crewPrefab });
        EditorUtility.SetDirty(prefabsList);
    }

    private static void PointShipSpawnerAtCrew(GameObject crewPrefab)
    {
        Scene scene = EditorSceneManager.OpenScene(
            BoatScenePath, OpenSceneMode.Single);

        if (!scene.IsValid())
        {
            Debug.LogError(
                $"[Skeleton Crew Builder] Could not open {BoatScenePath}."
            );
            return;
        }

        List<NetworkEnemyShipSpawner2D> spawners =
            new List<NetworkEnemyShipSpawner2D>();

        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            spawners.AddRange(
                rootObject.GetComponentsInChildren<NetworkEnemyShipSpawner2D>(
                    true
                )
            );
        }

        if (spawners.Count == 0)
        {
            Debug.LogWarning(
                "[Skeleton Crew Builder] No NetworkEnemyShipSpawner2D in " +
                BoatScenePath + "; crew prefab not wired."
            );
            return;
        }

        int changed = 0;

        foreach (NetworkEnemyShipSpawner2D spawner in spawners)
        {
            SerializedObject serialized = new SerializedObject(spawner);
            SerializedProperty crew = serialized.FindProperty("crewPrefab");

            if (crew == null || crew.objectReferenceValue == crewPrefab)
            {
                continue;
            }

            crew.objectReferenceValue = crewPrefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(spawner);
            changed++;
        }

        if (changed > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        Debug.Log(
            $"[Skeleton Crew Builder] Boat scene spawners updated: {changed} " +
            $"of {spawners.Count}."
        );
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in
            parent.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == name)
            {
                return child;
            }
        }

        return null;
    }

    private static float ReadFloat(
        Component component,
        string fieldName,
        float fallback
    )
    {
        if (component == null)
        {
            return fallback;
        }

        SerializedProperty property =
            new SerializedObject(component).FindProperty(fieldName);

        return property != null ? property.floatValue : fallback;
    }

    private static void WriteFloat(
        Component component,
        string fieldName,
        float value
    )
    {
        if (component == null)
        {
            return;
        }

        SerializedObject serialized = new SerializedObject(component);
        SerializedProperty property = serialized.FindProperty(fieldName);

        if (property == null)
        {
            return;
        }

        property.floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
