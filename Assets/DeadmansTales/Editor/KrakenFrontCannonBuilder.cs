using DeadmansTales.Ship;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Gives the kraken arena's ship the bow cannon the boat level has.
///
/// The two scenes were built from the same ship: both carry a cannon named
/// "Cannon", "Cannon (1)", "Cannon (2)" and "Cannon (3)" at matching local
/// positions under the same ship root. The boat level has a fifth,
/// "Cannon (5)" at local (-7.81, 12.6) -- far to the -x side of every other
/// cannon (they sit between 0.48 and 4.5), which is the bow. That is the one
/// the arena is missing.
///
/// It is built by DUPLICATING one of the arena's own cannons rather than
/// copying the boat scene's. A cross-scene copy would drag the boat ship's
/// references with it -- ShipCannon points at a reticle that lives on the ship
/// root, not inside the cannon -- and land a cannon aiming at an object in
/// another scene. Duplicating in place keeps every arena reference correct and
/// lets Unity remap the Muzzle/Standpoint children automatically.
///
/// Idempotent: re-running finds the bow cannon already there and does nothing.
/// </summary>
public static class KrakenFrontCannonBuilder
{
    private const string MenuPath =
        "Deadman's Tales/Ship/Add Front Cannon To Kraken Arena";

    private const string KrakenScenePath =
        "Assets/DeadmansTales/Scenes/Boat/Kraken_Arena_2D.unity";

    // Name and local position taken from the boat level's bow cannon.
    private const string FrontCannonName = "Cannon (5)";

    private static readonly Vector3 FrontCannonLocalPosition =
        new Vector3(-7.81f, 12.6f, 0f);

    [MenuItem(MenuPath)]
    public static void BuildAll()
    {
        Scene scene = EditorSceneManager.OpenScene(
            KrakenScenePath, OpenSceneMode.Single);

        if (!scene.IsValid())
        {
            Debug.LogError(
                $"[Kraken Front Cannon] Could not open {KrakenScenePath}.");
            return;
        }

        Transform shipRoot = FindShipRoot(scene);

        if (shipRoot == null)
        {
            Debug.LogError(
                "[Kraken Front Cannon] No PlayerShipMarker in " +
                KrakenScenePath + ", so there is no ship to mount a cannon on.");
            return;
        }

        // Already built.
        foreach (ShipCannon existing in
            shipRoot.GetComponentsInChildren<ShipCannon>(true))
        {
            if (existing.name == FrontCannonName)
            {
                Debug.Log(
                    "[Kraken Front Cannon] " + FrontCannonName + " is already " +
                    "on the arena ship; nothing to do.");
                return;
            }
        }

        ShipCannon template = PickTemplate(shipRoot);

        if (template == null)
        {
            Debug.LogError(
                "[Kraken Front Cannon] The arena ship has no existing " +
                "ShipCannon to duplicate.");
            return;
        }

        GameObject copy = Object.Instantiate(
            template.gameObject, template.transform.parent);

        copy.name = FrontCannonName;
        copy.transform.localPosition = FrontCannonLocalPosition;
        copy.transform.localRotation = template.transform.localRotation;
        copy.transform.localScale = template.transform.localScale;

        EditorUtility.SetDirty(copy);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log(
            $"[Kraken Front Cannon] Added {FrontCannonName} at local " +
            $"{FrontCannonLocalPosition} by duplicating '{template.name}'. " +
            $"The arena ship now has " +
            $"{shipRoot.GetComponentsInChildren<ShipCannon>(true).Length} " +
            "cannons.");
    }

    public static void BuildAllFromCommandLine()
    {
        BuildAll();
    }

    private static Transform FindShipRoot(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            PlayerShipMarker marker =
                root.GetComponentInChildren<PlayerShipMarker>(true);

            if (marker != null)
            {
                return marker.transform;
            }
        }

        return null;
    }

    // Prefer a cannon on the same (upper) row as the bow cannon so the
    // duplicate's facing and stand point read sensibly at the front; otherwise
    // just take the first one.
    private static ShipCannon PickTemplate(Transform shipRoot)
    {
        ShipCannon[] cannons =
            shipRoot.GetComponentsInChildren<ShipCannon>(true);

        if (cannons.Length == 0)
        {
            return null;
        }

        ShipCannon best = cannons[0];
        float bestY = float.NegativeInfinity;

        foreach (ShipCannon cannon in cannons)
        {
            float y = cannon.transform.localPosition.y;

            if (y > bestY)
            {
                bestY = y;
                best = cannon;
            }
        }

        return best;
    }
}
