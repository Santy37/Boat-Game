using DeadmansTales.Ship;
using UnityEngine;
using UnityEngine.UI;

namespace DeadmansTales.UI
{
    /// <summary>
    /// Displays the shared ship hull health on the HUD (GDD: the HUD must
    /// clearly show ship health). Polls the synchronized value so it works
    /// on host and clients regardless of spawn order.
    /// </summary>
    public sealed class ShipHealthHUD : MonoBehaviour
    {
        [SerializeField]
        private Slider healthSlider;

        [SerializeField]
        private Text label;

        private NetworkShipHealth shipHealth;

        private void Update()
        {
            if (shipHealth == null || !shipHealth.IsSpawned)
            {
                shipHealth = FindFirstObjectByType<NetworkShipHealth>();

                if (shipHealth == null)
                {
                    SetVisible(false);
                    return;
                }
            }

            SetVisible(true);

            if (healthSlider != null)
            {
                healthSlider.minValue = 0f;
                healthSlider.maxValue = 1f;
                healthSlider.value = shipHealth.HealthFraction;
            }

            if (label != null)
            {
                label.text =
                    $"Ship {shipHealth.CurrentHealth.Value:0}/" +
                    $"{shipHealth.MaximumHealth:0}";
            }
        }

        private void SetVisible(bool visible)
        {
            if (
                healthSlider != null &&
                healthSlider.gameObject.activeSelf != visible
            )
            {
                healthSlider.gameObject.SetActive(visible);
            }

            if (label != null && label.gameObject.activeSelf != visible)
            {
                label.gameObject.SetActive(visible);
            }
        }
    }
}
