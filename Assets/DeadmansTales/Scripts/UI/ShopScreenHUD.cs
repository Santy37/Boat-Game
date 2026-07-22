using DeadmansTales.Networking;
using Unity.Netcode;
using UnityEngine;

namespace DeadmansTales.UI
{
    /// <summary>
    /// The stall counter window: opens whenever the local player is standing
    /// at a <see cref="NetworkShopVendor"/>, and lists what that trader sells,
    /// what it costs THIS player, and what is in their purse.
    ///
    /// Drawn with OnGUI to match CoinPurseHUD and the interaction prompts.
    /// That is a deliberate trade: a canvas prefab would look better, but
    /// every scene in this project is produced by an idempotent editor
    /// builder, and a prefab full of RectTransforms is far harder for a
    /// builder to author and re-author than a component with no wiring.
    ///
    /// Buying still goes through the ordinary interaction request, so the
    /// server remains the only thing that can move coins or grant goods —
    /// this class renders state and forwards a button press, nothing more.
    /// </summary>
    public sealed class ShopScreenHUD : MonoBehaviour
    {
        private const float PanelWidth = 560f;
        private const float PanelHeight = 232f;
        private const float BottomMargin = 24f;

        /// <summary>
        /// Empty counter space. The stalls sell one line each for now; these
        /// show the crew that the shop is meant to grow, and give Shay a
        /// concrete layout to design real slots against.
        /// </summary>
        private static readonly string[] PlaceholderSlots =
        {
            "Powder & Shot",
            "Charts",
        };

        private NetworkInteractionInput2D cachedInput;

        private GUIStyle panelStyle;
        private GUIStyle titleStyle;
        private GUIStyle itemNameStyle;
        private GUIStyle detailStyle;
        private GUIStyle priceStyle;
        private GUIStyle placeholderStyle;
        private GUIStyle iconStyle;
        private GUIStyle buyStyle;
        private GUIStyle buyDisabledStyle;

        private void OnGUI()
        {
            if (PauseMenu.InputBlocked)
            {
                return;
            }

            NetworkInteractionInput2D input = ResolveInput();

            if (input == null)
            {
                return;
            }

            NetworkShopVendor vendor =
                input.CurrentTarget as NetworkShopVendor;

            if (vendor == null)
            {
                return;
            }

            EnsureStyles();
            DrawPanel(input, vendor);
        }

        private void DrawPanel(
            NetworkInteractionInput2D input,
            NetworkShopVendor vendor
        )
        {
            Rect panel = new Rect(
                (Screen.width - PanelWidth) * 0.5f,
                Screen.height - PanelHeight - BottomMargin,
                PanelWidth,
                PanelHeight
            );

            GUI.Box(panel, GUIContent.none, panelStyle);

            float x = panel.x + 18f;
            float width = panel.width - 36f;
            float y = panel.y + 14f;

            GUI.Label(
                new Rect(x, y, width, 26f),
                vendor.VendorName,
                titleStyle
            );

            GUI.Label(
                new Rect(x, y, width, 26f),
                $"Coins: {vendor.LocalCoins}",
                priceStyle
            );

            y += 34f;

            bool soldOut = vendor.IsSoldOutForLocalPlayer;
            bool affordable = vendor.LocalPlayerCanAfford;
            int price = vendor.LocalPrice;

            // --- the real stock line ---
            Rect row = new Rect(x, y, width, 66f);
            GUI.Box(row, GUIContent.none);

            GUI.Box(
                new Rect(row.x + 8f, row.y + 8f, 50f, 50f),
                Initial(vendor.StockDisplayName),
                iconStyle
            );

            GUI.Label(
                new Rect(row.x + 68f, row.y + 8f, row.width - 210f, 22f),
                vendor.StockDisplayName,
                itemNameStyle
            );

            GUI.Label(
                new Rect(row.x + 68f, row.y + 32f, row.width - 210f, 30f),
                vendor.StockDescription,
                detailStyle
            );

            string priceText = soldOut ? "Sold out" : $"{price} coins";

            GUI.Label(
                new Rect(row.xMax - 240f, row.y + 10f, 100f, 22f),
                priceText,
                priceStyle
            );

            bool buyable = !soldOut && affordable;

            string label = soldOut
                ? "Sold out"
                : affordable ? "Buy  (E)" : "Too dear";

            // Drawn as a Box and hit-tested by hand rather than using
            // GUI.Button. Interactive IMGUI controls allocate control IDs
            // that must line up across the Layout, MouseDown and Repaint
            // passes of a frame; this panel appears and disappears with the
            // player's proximity, so that count is not stable, and a
            // mismatch is exactly the kind of thing that wedges the editor.
            // A Box allocates nothing, so no mismatch is possible.
            Rect buyRect = new Rect(row.xMax - 128f, row.y + 14f, 116f, 38f);
            GUI.Box(buyRect, label, buyable ? buyStyle : buyDisabledStyle);

            if (
                buyable &&
                Event.current.type == EventType.MouseDown &&
                Event.current.button == 0 &&
                buyRect.Contains(Event.current.mousePosition)
            )
            {
                Event.current.Use();
                input.RequestInteractionWithCurrentTarget();
            }

            y += 74f;

            // --- placeholder counter space ---
            float slotWidth = (width - 10f) / PlaceholderSlots.Length;

            for (int index = 0; index < PlaceholderSlots.Length; index++)
            {
                Rect slot = new Rect(
                    x + index * (slotWidth + 10f),
                    y,
                    slotWidth,
                    44f
                );

                GUI.Box(slot, GUIContent.none);
                GUI.Label(
                    slot,
                    $"{PlaceholderSlots[index]}\n(coming soon)",
                    placeholderStyle
                );
            }

            y += 50f;

            GUI.Label(
                new Rect(x, y, width, 20f),
                "Press E to buy  ·  walk away to leave the stall",
                detailStyle
            );
        }

        /// <summary>
        /// Placeholder art: the first letter of the goods in a tinted box.
        /// Replaced the moment there are real item icons to show.
        /// </summary>
        private static string Initial(string stockName)
        {
            return string.IsNullOrEmpty(stockName)
                ? "?"
                : stockName.Substring(0, 1).ToUpperInvariant();
        }

        private void EnsureStyles()
        {
            if (panelStyle != null)
            {
                return;
            }

            panelStyle = new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset(0, 0, 0, 0),
            };

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
            };

            itemNameStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
            };

            detailStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
            };

            priceStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
            };

            placeholderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };

            iconStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };

            buyStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };

            buyDisabledStyle = new GUIStyle(buyStyle);
            buyDisabledStyle.normal.textColor = new Color(1f, 1f, 1f, 0.45f);
        }

        private NetworkInteractionInput2D ResolveInput()
        {
            if (cachedInput != null)
            {
                return cachedInput;
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

            cachedInput = manager.LocalClient.PlayerObject
                .GetComponent<NetworkInteractionInput2D>();

            return cachedInput;
        }
    }
}
