using System.Collections.Generic;
using UnityEngine;

namespace DeadmansTales.Ship
{
    /// <summary>
    /// Carries boarded enemies along as the player's ship moves.
    ///
    /// Boarders cannot simply be parented to the ship: every enemy has its own
    /// NetworkObject (Enemy requires one) and NGO does not support nested
    /// spawned NetworkObjects -- the same constraint that forces
    /// <see cref="EnemyShipApproach"/> to carry its own crew by hand. So this
    /// does the mirror of that job on the player's side: it measures the ship's
    /// own movement each frame and shifts every boarder by the same amount,
    /// including their AI's cached home/wander positions.
    ///
    /// Server-only in effect: it is only ever handed boarders by
    /// EnemyShipApproach, which is server-side. Added automatically to the
    /// player's ship the first time somebody boards, so it needs no scene
    /// wiring -- deliberate, because the boat scene is being rewritten on
    /// several branches at once.
    /// </summary>
    public sealed class ShipBoarderCarrier : MonoBehaviour
    {
        private readonly List<Enemy> boarders = new List<Enemy>();
        private Vector3 lastPosition;
        private bool hasLastPosition;

        /// <summary>
        /// Finds the carrier on the player's ship, adding it if this is the
        /// first boarder.
        /// </summary>
        public static ShipBoarderCarrier ResolveFor(PlayerShipMarker marker)
        {
            if (marker == null)
            {
                return null;
            }

            ShipBoarderCarrier carrier =
                marker.GetComponent<ShipBoarderCarrier>();

            return carrier != null
                ? carrier
                : marker.gameObject.AddComponent<ShipBoarderCarrier>();
        }

        public void Add(Enemy boarder)
        {
            if (boarder == null || boarders.Contains(boarder))
            {
                return;
            }

            boarders.Add(boarder);

            // Start measuring from where the ship is NOW, so the first frame
            // after a boarding does not shunt the new arrival by a delta that
            // accumulated before it was aboard.
            lastPosition = transform.position;
            hasLastPosition = true;
        }

        public void Remove(Enemy boarder)
        {
            boarders.Remove(boarder);
        }

        // LateUpdate, to run alongside ShipHelm's own position write. The delta
        // is measured against this component's last reading rather than against
        // anything ShipHelm reports, so script execution order does not matter:
        // if the helm happens to run after this, the carry is one frame behind
        // instead of wrong, and nothing accumulates either way.
        private void LateUpdate()
        {
            if (!hasLastPosition)
            {
                lastPosition = transform.position;
                hasLastPosition = true;
                return;
            }

            Vector2 delta = transform.position - lastPosition;
            lastPosition = transform.position;

            if (boarders.Count == 0)
            {
                return;
            }

            for (int i = boarders.Count - 1; i >= 0; i--)
            {
                Enemy boarder = boarders[i];

                // Killed or despawned: stop carrying a corpse around.
                if (boarder == null || !boarder.IsAlive)
                {
                    boarders.RemoveAt(i);
                    continue;
                }

                if (delta == Vector2.zero)
                {
                    continue;
                }

                Rigidbody2D body = boarder.GetComponent<Rigidbody2D>();

                if (body != null)
                {
                    body.position += delta;
                }
                else
                {
                    boarder.transform.position += (Vector3)delta;
                }

                // Shift the AI's cached absolute positions too, or the boarder
                // will keep walking back toward where the deck used to be.
                ShipEnemyAI ai = boarder.GetComponent<ShipEnemyAI>();
                ai?.ApplyExternalDelta(delta);
            }
        }
    }
}
