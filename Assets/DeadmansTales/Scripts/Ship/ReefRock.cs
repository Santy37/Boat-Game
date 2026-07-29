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

    public void Bind(ScrollingReef owner, int gate, int rock)
    {
        reef = owner;
        gateIndex = gate;
        rockIndex = rock;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (reef == null)
        {
            return;
        }

        NetworkCannonball ball = other.GetComponentInParent<NetworkCannonball>();

        if (ball == null)
        {
            return;
        }

        // Server decides. Clients do nothing here and simply wait to be told,
        // otherwise each peer would break rocks on its own and the reef would
        // drift apart between machines.
        if (!ball.IsServer)
        {
            return;
        }

        if (!reef.RegisterCannonHitServer(gateIndex, rockIndex))
        {
            return;
        }

        // The generation is read at the moment of the break and travels with the
        // message, so a receiver whose gate has already recycled can discard it.
        ball.BreakReefRockServer(
            gateIndex, rockIndex, reef.GenerationOf(gateIndex));
    }
}
