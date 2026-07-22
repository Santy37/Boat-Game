using Unity.Netcode;
using UnityEngine;

namespace DeadmansTales.Networking
{
    /// <summary>
    /// What a stallholder sells.
    /// </summary>
    public enum ShopStock
    {
        WeaponTier,
        Upgrade,
        FullHeal,
    }

    /// <summary>
    /// A shop-island stallholder. Press E to buy; the server checks the
    /// buyer's purse, spends it, and grants the goods, so a client cannot
    /// buy what it cannot afford.
    ///
    /// Prices rise per purchase (<see cref="priceIncreasePerPurchase"/>) and
    /// are tracked PER PLAYER, not per stall: in a 2-4 player crew one
    /// player buying three swords must not inflate the price for everyone
    /// else, and a shared counter would also let one rich player price the
    /// rest of the crew out of the shop.
    /// </summary>
    public sealed class NetworkShopVendor : NetworkInteractable2D
    {
        [Header("Stall")]
        [SerializeField]
        private string vendorName = "Trader";

        [SerializeField]
        private ShopStock stock = ShopStock.Upgrade;

        [Header("Pricing")]
        [SerializeField]
        [Min(0)]
        private int basePrice = 20;

        [SerializeField]
        [Min(0)]
        private int priceIncreasePerPurchase = 10;

        [Tooltip("0 means this stall never runs out for a given player.")]
        [SerializeField]
        [Min(0)]
        private int purchaseLimitPerPlayer;

        /// <summary>
        /// Purchases made by each client at this stall. Server-side only —
        /// it drives price and stock decisions the server alone makes.
        /// </summary>
        private readonly System.Collections.Generic.Dictionary<ulong, int>
            purchasesByClient =
                new System.Collections.Generic.Dictionary<ulong, int>();

        public string VendorName => vendorName;

        /// <summary>This stall shows its own counter panel.</summary>
        public override bool DrawsOwnScreen => true;

        public ShopStock Stock => stock;

        /// <summary>Shop-window name of the goods, e.g. "Sharpen Blade".</summary>
        public string StockDisplayName => StockLabel();

        /// <summary>One line of flavour and effect for the shop window.</summary>
        public string StockDescription
        {
            get
            {
                switch (stock)
                {
                    case ShopStock.WeaponTier:
                        return "Hone your cutlass. +1 blade tier.";

                    case ShopStock.FullHeal:
                        return "A hot meal and a sit down. Full health.";

                    default:
                        return "Ship's kit. A lasting crew upgrade.";
                }
            }
        }

        /// <summary>
        /// What this stall charges the LOCAL player right now. Prices are
        /// per-buyer, so the shop window must ask for the local purse rather
        /// than show a single shared number.
        /// </summary>
        public int LocalPrice
        {
            get
            {
                NetworkPlayerLoadout loadout = FindLocalLoadout();

                return loadout == null
                    ? basePrice
                    : PriceFor(GetPurchaseCount(loadout.OwnerClientId));
            }
        }

        public bool IsSoldOutForLocalPlayer
        {
            get
            {
                NetworkPlayerLoadout loadout = FindLocalLoadout();

                return loadout != null &&
                    IsSoldOut(GetPurchaseCount(loadout.OwnerClientId));
            }
        }

        /// <summary>Coins the local player is carrying, or 0 before they exist.</summary>
        public int LocalCoins
        {
            get
            {
                NetworkPlayerLoadout loadout = FindLocalLoadout();
                return loadout == null ? 0 : loadout.Coins.Value;
            }
        }

        public bool LocalPlayerCanAfford => LocalCoins >= LocalPrice;

        public override string InteractionPrompt
        {
            get
            {
                NetworkPlayerLoadout localLoadout = FindLocalLoadout();

                // Before the local player exists there is nothing personal to
                // report, so quote the opening price.
                if (localLoadout == null)
                {
                    return $"{vendorName}: {StockLabel()} - {basePrice} coins";
                }

                int purchases = GetPurchaseCount(
                    localLoadout.OwnerClientId
                );

                if (IsSoldOut(purchases))
                {
                    return $"{vendorName}: Sold Out";
                }

                int price = PriceFor(purchases);
                int coins = localLoadout.Coins.Value;

                if (coins < price)
                {
                    return
                        $"{vendorName}: {StockLabel()} - {price} coins " +
                        $"(you have {coins})";
                }

                return
                    $"Press E - {vendorName}: {StockLabel()} for " +
                    $"{price} coins (you have {coins})";
            }
        }

        protected override bool CanInteractServer(
            NetworkInteractionController2D interactor
        )
        {
            NetworkPlayerLoadout loadout =
                interactor.GetComponent<NetworkPlayerLoadout>();

            if (loadout == null)
            {
                return false;
            }

            PlayerHealth health = interactor.GetComponent<PlayerHealth>();
            if (health != null && !health.IsAlive)
            {
                return false;
            }

            int purchases = GetPurchaseCount(interactor.OwnerClientId);

            if (IsSoldOut(purchases))
            {
                return false;
            }

            // A full-health player buying a heal would burn coins for
            // nothing, so the stall refuses the sale.
            if (
                stock == ShopStock.FullHeal &&
                health != null &&
                health.CurrentHealth.Value >= health.MaximumHealth
            )
            {
                return false;
            }

            return loadout.Coins.Value >= PriceFor(purchases);
        }

        protected override void PerformInteractionServer(
            NetworkInteractionController2D interactor
        )
        {
            NetworkPlayerLoadout loadout =
                interactor.GetComponent<NetworkPlayerLoadout>();

            if (loadout == null)
            {
                return;
            }

            ulong clientId = interactor.OwnerClientId;
            int purchases = GetPurchaseCount(clientId);
            int price = PriceFor(purchases);

            // Spend first: if the purse cannot cover it nothing is granted.
            if (!loadout.TrySpendCoinsServer(price))
            {
                return;
            }

            bool delivered = GrantStockServer(loadout, interactor);

            if (!delivered)
            {
                // Never take coins for goods that were not handed over.
                loadout.AddCoinsServer(price);
                return;
            }

            purchasesByClient[clientId] = purchases + 1;

            Debug.Log(
                $"[Shop] Client {clientId} bought {StockLabel()} from " +
                $"{vendorName} for {price} coins " +
                $"(purse now {loadout.Coins.Value}).",
                this
            );
        }

        private bool GrantStockServer(
            NetworkPlayerLoadout loadout,
            NetworkInteractionController2D interactor
        )
        {
            switch (stock)
            {
                case ShopStock.WeaponTier:
                    return loadout.GrantWeaponServer();

                case ShopStock.Upgrade:
                    return loadout.GrantUpgradeServer();

                case ShopStock.FullHeal:
                    PlayerHealth health =
                        interactor.GetComponent<PlayerHealth>();

                    if (health == null)
                    {
                        return false;
                    }

                    health.Heal(health.MaximumHealth);
                    return true;

                default:
                    return false;
            }
        }

        private int PriceFor(int purchases)
        {
            return Mathf.Max(
                0,
                basePrice + priceIncreasePerPurchase * Mathf.Max(0, purchases)
            );
        }

        private bool IsSoldOut(int purchases)
        {
            return purchaseLimitPerPlayer > 0 &&
                purchases >= purchaseLimitPerPlayer;
        }

        private int GetPurchaseCount(ulong clientId)
        {
            return purchasesByClient.TryGetValue(clientId, out int count)
                ? count
                : 0;
        }

        private string StockLabel()
        {
            switch (stock)
            {
                case ShopStock.WeaponTier:
                    return "Sharpen Blade";

                case ShopStock.FullHeal:
                    return "Hot Meal";

                default:
                    return "Ship Upgrade";
            }
        }

        /// <summary>
        /// The prompt is drawn locally, so it needs the local player's purse
        /// rather than the server's view of whoever last interacted.
        /// </summary>
        private static NetworkPlayerLoadout FindLocalLoadout()
        {
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

            return manager.LocalClient.PlayerObject
                .GetComponent<NetworkPlayerLoadout>();
        }
    }
}
