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
        ShipUpgrade,
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
    ///
    /// The purchase count itself lives on the buyer's own
    /// NetworkPlayerLoadout rather than in a Dictionary here, so it carries
    /// between the first and second shop islands -- each stall is its own
    /// scene-placed NetworkObject, so a per-vendor-instance counter would
    /// have quietly reset the price the moment a player walked into the
    /// second island's shop.
    /// </summary>
    public sealed class NetworkShopVendor : NetworkInteractable2D
    {
        [Header("Stall")]
        [SerializeField]
        private string vendorName = "Trader";

        [SerializeField]
        private ShopStock stock = ShopStock.ShipUpgrade;

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

        [Header("Purchase Audio")]
        [Tooltip(
            "Played for the BUYER when a sale actually goes through. Not " +
            "played on a refused sale (cannot afford, sold out, already at " +
            "full health), and not played on the other players' machines -- " +
            "this is the buyer's own receipt, not an announcement to the crew."
        )]
        [SerializeField]
        private AudioClip purchaseSound;

        [SerializeField]
        [Range(0f, 1f)]
        private float purchaseVolume = 1f;

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
                        return
                            $"Reinforce the hull. +{NetworkRunState.ShipSinkBonusPerUpgrade:0} " +
                            $"patch capacity, +{NetworkRunState.ShipHealthBonusPerUpgrade:0} hull health.";
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
                    : PriceFor(GetPurchaseCount(loadout));
            }
        }

        public bool IsSoldOutForLocalPlayer
        {
            get
            {
                NetworkPlayerLoadout loadout = FindLocalLoadout();

                return loadout != null &&
                    IsSoldOut(loadout, GetPurchaseCount(loadout));
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

                int purchases = GetPurchaseCount(localLoadout);

                if (IsSoldOut(localLoadout, purchases))
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

            int purchases = GetPurchaseCount(loadout);

            if (IsSoldOut(loadout, purchases))
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
            int purchases = GetPurchaseCount(loadout);
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

            PlayPurchaseSoundClientRpc(BuyerOnly(clientId));

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

                case ShopStock.ShipUpgrade:
                    return loadout.GrantShipUpgradeServer();

                case ShopStock.FullHeal:
                    PlayerHealth health =
                        interactor.GetComponent<PlayerHealth>();

                    if (health == null)
                    {
                        return false;
                    }

                    health.Heal(health.MaximumHealth);
                    loadout.RecordHealPurchaseServer();
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

        /// <summary>
        /// The sword shop has a hard cap independent of
        /// <see cref="purchaseLimitPerPlayer"/>: Sword15 is the last sprite
        /// in the sheet, so a maxed-out weapon tier always reads as sold
        /// out here even if the limit field was left at its unlimited 0.
        /// </summary>
        private bool IsSoldOut(NetworkPlayerLoadout loadout, int purchases)
        {
            if (
                stock == ShopStock.WeaponTier &&
                loadout != null &&
                loadout.IsWeaponMaxed
            )
            {
                return true;
            }

            return purchaseLimitPerPlayer > 0 &&
                purchases >= purchaseLimitPerPlayer;
        }

        /// <summary>
        /// How many times this player has already bought THIS stall's
        /// goods. Read straight off the player's own loadout rather than a
        /// per-vendor-instance counter so it carries between shop islands.
        /// </summary>
        private int GetPurchaseCount(NetworkPlayerLoadout loadout)
        {
            if (loadout == null)
            {
                return 0;
            }

            switch (stock)
            {
                case ShopStock.WeaponTier:
                    return loadout.WeaponTier.Value;

                case ShopStock.ShipUpgrade:
                    return loadout.ShipUpgradePurchases.Value;

                case ShopStock.FullHeal:
                    return loadout.HealPurchases.Value;

                default:
                    return 0;
            }
        }

        /// <summary>
        /// The buyer's "cha-ching". Sent to that one client rather than
        /// broadcast, so a four-player crew standing at the same stall does not
        /// all hear each other's receipts.
        /// </summary>
        [ClientRpc]
        private void PlayPurchaseSoundClientRpc(
            ClientRpcParams rpcParams = default
        )
        {
            if (purchaseSound == null)
            {
                return;
            }

            // PlayClipAtPoint rather than an AudioSource on this stall. None of
            // the six vendor objects across the two shop scenes carries one, so
            // a GetComponent<AudioSource>() version finds nothing and the sale
            // is silent. This also matches how every other one-shot in the
            // project is played (NetworkCannonball, DestructibleObstacle) and
            // needs no per-scene wiring.
            //
            // Played AT THE LISTENER, not at the stall: the clip is imported as
            // 3D, and the shop camera is fixed on the island rather than on the
            // player, so a stall-positioned one-shot would be attenuated by
            // however far apart those happen to be. A purchase confirmation is
            // interface feedback -- it should sound the same wherever the stall
            // is standing.
            AudioListener listener = FindFirstObjectByType<AudioListener>();

            Vector3 position = listener != null
                ? listener.transform.position
                : transform.position;

            AudioSource.PlayClipAtPoint(
                purchaseSound,
                position,
                purchaseVolume
            );
        }

        /// <summary>
        /// Addresses a ClientRpc to a single client -- the one that bought
        /// something.
        /// </summary>
        private static ClientRpcParams BuyerOnly(ulong buyerClientId)
        {
            return new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { buyerClientId }
                }
            };
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
