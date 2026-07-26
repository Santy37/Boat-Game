using DeadmansTales.Ship;
using UnityEngine;
using UnityEngine.UI;

namespace DeadmansTales.UI
{
    /// <summary>
    /// A floating health + sink meter that lives under a single ship in
    /// world space, following that ship around. Distinct from
    /// <see cref="ShipHealthHUD"/>/<see cref="ShipSinkMeterHUD"/>, which are
    /// screen-space HUD elements that only ever show the player's own ship
    /// (via a "find the PlayerShipMarker" search) -- this instead reads
    /// whichever ship it happens to be parented under, via
    /// GetComponentInParent, so the exact same component works unmodified on
    /// the player's ship and on every enemy ship. Each ship gets its own
    /// instance of this as a child object; there's no global lookup here at
    /// all, on purpose.
    ///
    /// Non-networked by design: it only ever reads NetworkVariables that are
    /// already replicated to every client (CurrentHealth, CurrentSinkLevel),
    /// so plain polling in Update -- same pattern the screen-space HUD
    /// scripts already use -- is enough. No NetworkBehaviour/NetworkObject
    /// needed, which also sidesteps the "no nested NetworkObjects" NGO
    /// limitation that crew already have to work around.
    /// </summary>
    public sealed class ShipStatusOverlay : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField]
        private Slider healthSlider;

        [SerializeField]
        private Slider sinkSlider;

        [SerializeField]
        private Text label;

        [Header("Behaviour")]
        [Tooltip(
            "Zero out this object's rotation every frame so the bar stays " +
            "upright and readable even if the ship it's parented under " +
            "ever rotates. Safe to leave on even for ships that never " +
            "rotate today."
        )]
        [SerializeField]
        private bool keepUpright = true;

        private NetworkShipHealth shipHealth;
        private NetworkShipSinkMeter sinkMeter;

        private void Start()
        {
            // GetComponentInParent, not GetComponent -- this object is a
            // child of the ship (so it can sit positioned under it and
            // travel with it for free via normal Transform parenting), not
            // a component directly on the ship itself.
            shipHealth = GetComponentInParent<NetworkShipHealth>();
            sinkMeter = GetComponentInParent<NetworkShipSinkMeter>();
        }

        private void Update()
        {
            if (keepUpright)
            {
                transform.rotation = Quaternion.identity;
            }

            bool hasHealth = shipHealth != null && shipHealth.IsSpawned;
            bool hasSink = sinkMeter != null && sinkMeter.IsSpawned;

            if (healthSlider != null && hasHealth)
            {
                healthSlider.minValue = 0f;
                healthSlider.maxValue = 1f;
                healthSlider.value = shipHealth.HealthFraction;
            }

            if (sinkSlider != null && hasSink)
            {
                sinkSlider.minValue = 0f;
                sinkSlider.maxValue = 1f;
                sinkSlider.value = sinkMeter.SinkFraction;
            }

            if (label == null)
            {
                return;
            }

            string healthText = hasHealth
                ? $"HP {shipHealth.CurrentHealth.Value:0}/{shipHealth.MaximumHealth:0}"
                : string.Empty;

            string sinkText = hasSink
                ? $"Sink {sinkMeter.CurrentSinkLevel.Value:0}/{sinkMeter.MaximumSinkLevel:0}"
                : string.Empty;

            label.text = healthText.Length > 0 && sinkText.Length > 0
                ? $"{healthText}   {sinkText}"
                : healthText + sinkText;
        }
    }
}
