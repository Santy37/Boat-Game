using Unity.Netcode;
using UnityEngine;

namespace DeadmansTales.UI
{
    /// <summary>
    /// Corner readout of the local player's coins and bought upgrades.
    ///
    /// Drawn with OnGUI to match how NetworkInteractionInput2D already
    /// renders interaction prompts — a shop is unusable if you cannot see
    /// what you can afford, and this keeps that feedback in the same visual
    /// language as the prompts without pulling in a canvas prefab.
    /// </summary>
    public sealed class CoinPurseHUD : MonoBehaviour
    {
        [SerializeField]
        private Vector2 screenOffset = new Vector2(18f, 18f);

        [SerializeField]
        private Vector2 size = new Vector2(260f, 56f);

        private NetworkPlayerLoadout cachedLoadout;
        private GUIStyle style;

        private void OnGUI()
        {
            NetworkPlayerLoadout loadout = ResolveLoadout();

            if (loadout == null)
            {
                return;
            }

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontSize = 15,
                    padding = RectInset(),
                };
            }

            Rect area = new Rect(
                screenOffset.x,
                screenOffset.y,
                size.x,
                size.y
            );

            string weapon = loadout.WeaponTier.Value > 0
                ? $"  |  Blade +{loadout.WeaponTier.Value}"
                : string.Empty;

            int upgrades =
                loadout.SpeedUpgrades.Value + loadout.HealthUpgrades.Value;
            string upgradeText = upgrades > 0
                ? $"  |  Upgrades {upgrades}"
                : string.Empty;

            GUI.Box(
                area,
                $"  Coins: {loadout.Coins.Value}{weapon}{upgradeText}",
                style
            );
        }

        private NetworkPlayerLoadout ResolveLoadout()
        {
            if (cachedLoadout != null)
            {
                return cachedLoadout;
            }

            NetworkManager manager = NetworkManager.Singleton;

            if (
                manager == null ||
                !manager.IsListening ||
                manager.LocalClient == null ||
                manager.LocalClient.PlayerObject == null
            )
            {
                return null;
            }

            cachedLoadout = manager.LocalClient.PlayerObject
                .GetComponent<NetworkPlayerLoadout>();

            return cachedLoadout;
        }

        /// <summary>
        /// GUIStyle.padding needs a RectOffset; this keeps the initializer
        /// readable without a separate statement.
        /// </summary>
        private static RectOffset RectInset()
        {
            return new RectOffset(10, 10, 6, 6);
        }
    }
}
