using System.Collections.Generic;
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
        private static readonly List<EnemyShipHullContact> ActiveHulls =
            new List<EnemyShipHullContact>();

        /// <summary>
        /// Every enemy-ship hull currently alive in the scene. This component
        /// already marks "the collider that IS an enemy ship's hull", so it
        /// doubles as the registry ShipHelm reads to push the player's ship
        /// back out of a hull it has steered into. Enemy ships are spawned at
        /// runtime, so there is nothing to wire in the Inspector -- and a
        /// per-frame FindObjectsByType would be far more expensive than a
        /// list kept up to date by OnEnable/OnDisable.
        /// </summary>
        public static IReadOnlyList<EnemyShipHullContact> Active => ActiveHulls;

        private int contactCount;

        public bool IsTouchingPlayerShip => contactCount > 0;

        /// <summary>
        /// The hull collider this component listens on -- the same collider
        /// that must stay a trigger for cannonball hits and engagement to
        /// work.
        /// </summary>
        public Collider2D Hull { get; private set; }

        private void Awake()
        {
            Hull = GetComponent<Collider2D>();

            if (Hull == null)
            {
                Debug.LogError(
                    $"[Enemy Ship Hull Contact] '{name}' has no Collider2D on " +
                    "this same object. Put this component on the ship's hull " +
                    "trigger (e.g. ShipHitBox).",
                    this
                );
            }
        }

        private void OnEnable()
        {
            if (!ActiveHulls.Contains(this))
            {
                ActiveHulls.Add(this);
            }
        }

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
            ActiveHulls.Remove(this);
        }
    }
}
