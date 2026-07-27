using DeadmansTales.Ship;
using UnityEngine;
using UnityEngine.UI;

namespace DeadmansTales.UI
{
    /// <summary>
    /// Displays the shared ship's SinkLevel, alongside <see cref="ShipHealthHUD"/>.
    /// Same polling pattern: finds the synchronized NetworkShipSinkMeter so it
    /// works on host and clients regardless of spawn order.
    /// </summary>
    public sealed class ShipSinkMeterHUD : MonoBehaviour
    {
        [SerializeField]
        private Slider sinkSlider;

        [SerializeField]
        private Text label;

        private NetworkShipSinkMeter sinkMeter;

        private void Update()
        {
            if (sinkMeter == null || !sinkMeter.IsSpawned)
            {
                // Same reasoning as ShipHealthHUD: once enemy ships exist
                // they carry their own NetworkShipSinkMeter, so this can't
                // be a blind FindFirstObjectByType search.
                PlayerShipMarker playerShip =
                    FindFirstObjectByType<PlayerShipMarker>();

                sinkMeter = playerShip != null
                    ? playerShip.GetComponent<NetworkShipSinkMeter>()
                    : null;

                if (sinkMeter == null)
                {
                    SetVisible(false);
                    return;
                }
            }

            SetVisible(true);

            if (sinkSlider != null)
            {
                sinkSlider.minValue = 0f;
                sinkSlider.maxValue = 1f;
                sinkSlider.value = sinkMeter.SinkFraction;
            }

            if (label != null)
            {
                label.text =
                    $"Sink O' Meter {sinkMeter.CurrentSinkLevel.Value:0}/" +
                    $"{sinkMeter.MaximumSinkLevel:0}";
            }
        }

        private void SetVisible(bool visible)
        {
            if (
                sinkSlider != null &&
                sinkSlider.gameObject.activeSelf != visible
            )
            {
                sinkSlider.gameObject.SetActive(visible);
            }

            if (label != null && label.gameObject.activeSelf != visible)
            {
                label.gameObject.SetActive(visible);
            }
        }
    }
}
