using UnityEngine;

namespace DeadmansTales.Ship
{
    /// <summary>
    /// Reports whether this enemy ship's hull is actually touching the
    /// player's ship, using real trigger contact instead of a fixed
    /// center-to-center distance. Put this on the ship's existing hull
    /// trigger collider (e.g. "ShipHitBox") -- it doesn't need its own
    /// collider or Rigidbody2D, it just listens for trigger events already
    /// happening on whatever GameObject it's attached to.
    ///
    /// Distance-based engagement let the two hulls visually overlap/phase
    /// through each other before the ship decided it was "close enough" --
    /// the exact stop point depended on where each ship's root Transform
    /// happened to be, not on the ships' actual size/shape. This reports
    /// the real moment the hulls meet instead.
    /// </summary>
    public sealed class EnemyShipHullContact : MonoBehaviour
    {
        private int contactCount;

        public bool IsTouchingPlayerShip => contactCount > 0;

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other.GetComponentInParent<PlayerShipMarker>() != null)
            {
                contactCount++;
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (other.GetComponentInParent<PlayerShipMarker>() != null)
            {
                contactCount = Mathf.Max(0, contactCount - 1);
            }
        }

        private void OnDisable()
        {
            contactCount = 0;
        }
    }
}
