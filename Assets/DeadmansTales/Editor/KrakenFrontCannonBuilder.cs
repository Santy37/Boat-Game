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

        // An existing bow cannon is REPOSITIONED rather than left alone: the
        // first build placed it by copying the boat level's local position,
        // which is off this ship's deck entirely.
        ShipCannon current = null;

        foreach (ShipCannon existing in
            shipRoot.GetComponentsInChildren<ShipCannon>(true))
        {
            if (existing.name == FrontCannonName)
            {
                current = existing;
                break;
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

        if (!TryFindBowSpot(shipRoot, template, out Vector3 bowLocal,
                out Vector2 bowFacing, out string how))
        {
            Debug.LogError(
                "[Kraken Front Cannon] Could not work out where this ship's " +
                "bow is, so nothing was placed. " + how);
            return;
        }

        GameObject cannon;

        if (current != null)
        {
            cannon = current.gameObject;
        }
        else
        {
            cannon = Object.Instantiate(
                template.gameObject, template.transform.parent);
            cannon.name = FrontCannonName;
            cannon.transform.localRotation = template.transform.localRotation;
            cannon.transform.localScale = template.transform.localScale;
        }

        cannon.transform.localPosition = bowLocal;

        // Point it along the hull instead of broadside. The template is a
        // side-firing cannon, so an unedited copy faces UP -- which is what
        // made the bow cannon aim off the ship.
        SerializedObject serialized =
            new SerializedObject(cannon.GetComponent<ShipCannon>());
        SerializedProperty facing = serialized.FindProperty("facing");

        if (facing != null)
        {
            facing.vector2Value = bowFacing;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorUtility.SetDirty(cannon);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log(
            $"[Kraken Front Cannon] {(current != null ? "Moved" : "Added")} " +
            $"{FrontCannonName} to local {bowLocal} facing {bowFacing}. " +
            how + " The arena ship now has " +
            $"{shipRoot.GetComponentsInChildren<ShipCannon>(true).Length} " +
            "cannons.");
    }

    public static void BuildAllFromCommandLine()
    {
        BuildAll();
    }

    /// <summary>
    /// Works out where THIS ship's bow is by measuring its own deck, instead of
    /// reusing the boat level's local position.
    ///
    /// The first attempt copied that position across and put the cannon off the
    /// deck: the two ships are not the same size, and the sign of x does not
    /// tell you which end is the bow. The deck fence (the EdgeCollider2D the
    /// boarding code also uses) is the ship's real walkable extent, and the bow
    /// is whichever end of it the existing broadside cannons are NOT clustered
    /// at -- they sit amidships, so the longer run of empty deck is the bow.
    /// </summary>
    private static bool TryFindBowSpot(
        Transform shipRoot,
        ShipCannon template,
        out Vector3 bowLocal,
        out Vector2 bowFacing,
        out string how)
    {
        bowLocal = Vector3.zero;
        bowFacing = Vector2.right;

        EdgeCollider2D deck = shipRoot.GetComponentInChildren<EdgeCollider2D>(true);

        if (deck == null)
        {
            how = "No EdgeCollider2D deck fence under the ship.";
            return false;
        }

        Bounds world = deck.bounds;

        // Deck extent in the ship's own space, which is what localPosition uses.
        Vector3 minLocal = shipRoot.InverseTransformPoint(
            new Vector3(world.min.x, world.min.y, 0f));
        Vector3 maxLocal = shipRoot.InverseTransformPoint(
            new Vector3(world.max.x, world.max.y, 0f));

        float deckMinX = Mathf.Min(minLocal.x, maxLocal.x);
        float deckMaxX = Mathf.Max(minLocal.x, maxLocal.x);

        // Where the existing cannons sit, so the bow end is the far one.
        float cannonMinX = float.PositiveInfinity;
        float cannonMaxX = float.NegativeInfinity;

        foreach (ShipCannon cannon in
            shipRoot.GetComponentsInChildren<ShipCannon>(true))
        {
            if (cannon.name == FrontCannonName)
            {
                continue;
            }

            float x = cannon.transform.localPosition.x;
            cannonMinX = Mathf.Min(cannonMinX, x);
            cannonMaxX = Mathf.Max(cannonMaxX, x);
        }

        if (float.IsInfinity(cannonMinX))
        {
            how = "No existing cannons to locate amidships from.";
            return false;
        }

        float roomAhead = deckMaxX - cannonMaxX;
        float roomBehind = cannonMinX - deckMinX;
        bool bowIsPositiveX = roomAhead >= roomBehind;

        // Sit inside the fence rather than on it, so the stand point stays on
        // the deck and taking the cannon cannot clip the player overboard.
        float inset = Mathf.Max(1f, (deckMaxX - deckMinX) * 0.06f);

        float bowX = bowIsPositiveX
            ? deckMaxX - inset
            : deckMinX + inset;

        // Sit on the CENTRELINE, not on a broadside row. The hull tapers to a
        // point at the bow, so the far end of an upper or lower row hangs off
        // the ship -- the widest deck there is the middle. The boat level's own
        // bow cannon says the same thing: its rows are at y 16.0 and 9.5 and it
        // sits at 12.6, between them.
        float rowSum = 0f;
        int rowCount = 0;

        foreach (ShipCannon cannon in
            shipRoot.GetComponentsInChildren<ShipCannon>(true))
        {
            if (cannon.name == FrontCannonName)
            {
                continue;
            }

            rowSum += cannon.transform.localPosition.y;
            rowCount++;
        }

        float bowY = rowCount > 0
            ? rowSum / rowCount
            : template.transform.localPosition.y;

        bowLocal = new Vector3(
            bowX, bowY, template.transform.localPosition.z);

        bowFacing = bowIsPositiveX ? Vector2.right : Vector2.left;

        how =
            $"Deck x spans {deckMinX:0.00}..{deckMaxX:0.00}, cannons occupy " +
            $"{cannonMinX:0.00}..{cannonMaxX:0.00}, so room ahead=" +
            $"{roomAhead:0.00} vs behind={roomBehind:0.00} puts the bow at " +
            $"{(bowIsPositiveX ? "+x" : "-x")}.";

        return true;
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
