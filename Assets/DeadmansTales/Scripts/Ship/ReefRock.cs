using DeadmansTales.Ship;
using UnityEngine;

/// <summary>
/// Identity tag on one pooled rock of a <see cref="ScrollingReef"/>, letting a
/// cannonball break it.
///
/// The reef is a plain MonoBehaviour with no NetworkObject anywhere, and its
/// rocks are pooled Transforms rather than spawned network objects -- which is
/// why the cannonball's normal hit path (NetworkShipSinkMeter, KrakenHealth,
/// DestructibleObstacle, Enemy, all networked) could not see them at all.
///
/// Rather than make the reef networked, a hit is resolved on the server and then
/// broadcast through the CANNONBALL's own NetworkObject, which already exists
/// and already reaches every peer. The rock is named by its (gate, rock) slot in
/// the reef's fixed pools plus the gate's generation, so no networked identity
/// is needed and a late message cannot hide the wrong rock.
///
/// Added at runtime by ScrollingReef, so no rock prefab or scene needs editing.
/// </summary>
[DisallowMultipleComponent]
public sealed class ReefRock : MonoBehaviour
{
    private ScrollingReef reef;
    private int gateIndex = -1;
    private int rockIndex = -1;

    public ScrollingReef Reef => reef;
    public int GateIndex => gateIndex;
    public int RockIndex => rockIndex;

    public void Bind(ScrollingReef owner, int gate, int rock)
    {
        reef = owner;
        gateIndex = gate;
        rockIndex = rock;
    }

    // Deliberately a pure identity tag with no trigger callback of its own.
    //
    // The first attempt resolved hits in OnTriggerEnter2D here, and cannonballs
    // sailed straight through: detection was split across two systems, and only
    // the cannonball's side actually fires. The ball finds its targets through
    // its OWN trigger plus an explicit overlap sweep (it can spawn already
    // overlapping something), and that is the path every other target type is
    // resolved on. A callback on this object is not part of it.
    //
    // So the hit is now resolved in NetworkCannonball.HandlePossibleHit, which
    // reads the indices above. Nothing about the server-authority or the
    // generation guard changes -- only which side notices the contact.
}
