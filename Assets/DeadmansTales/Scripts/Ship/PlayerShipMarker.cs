using UnityEngine;

namespace DeadmansTales.Ship
{
    /// <summary>
    /// Marks the player's own ship so enemy-ship systems (cannons, approach
    /// behavior, boarding) can find it specifically. Enemy ships carry their
    /// own NetworkShipHealth/NetworkShipSinkMeter too, so a plain
    /// FindFirstObjectByType search for those component types alone could
    /// just as easily return an enemy ship's own copy. Place this component
    /// once, on the player's Ship root, alongside NetworkShipHealth.
    /// </summary>
    public sealed class PlayerShipMarker : MonoBehaviour
    {
        [Tooltip(
            "The ship's actual hull trigger (e.g. ShipHitBox). Enemy " +
            "cannons aim at this collider's bounds center instead of this " +
            "GameObject's own Transform -- on both the player's Ship and " +
            "EnemyShip prefabs, the root Transform's pivot can sit well " +
            "away from where the hull collider actually is (traced deck/" +
            "hull shapes carry their own offset independent of the root), " +
            "so aiming at transform.position alone can miss the real hull " +
            "entirely. Wire this once, pointing to this same ship's own " +
            "hitbox."
        )]
        [SerializeField]
        private Collider2D hitbox;

        public Collider2D Hitbox => hitbox;

        /// <summary>
        /// Best aim/distance reference point for this ship: the hull
        /// collider's actual bounds center if wired, otherwise a fallback to
        /// this GameObject's own Transform (better than nothing if Hitbox
        /// was never assigned).
        /// </summary>
        public Vector2 AimPoint => hitbox != null
            ? (Vector2)hitbox.bounds.center
            : (Vector2)transform.position;
    }
}
