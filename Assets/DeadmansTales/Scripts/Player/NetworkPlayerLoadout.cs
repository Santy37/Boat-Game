using Unity.Netcode;
using UnityEngine;

/// <summary>
/// PLACEHOLDER loadout system: synchronized weapon tier and run upgrades.
///
/// This gives weapon and upgrade chests a real, network-synchronized effect
/// until the final inventory/upgrade system replaces it. Server-authoritative:
/// only the server may grant rewards; every client renders the same values.
///
/// Effects:
///  - Weapon tier: +<see cref="DamagePerWeaponTier"/> melee damage per tier.
///  - Speed upgrade: +<see cref="MoveSpeedPerUpgrade"/> movement per stack.
///  - Health upgrade: +<see cref="MaxHealthPerUpgrade"/> max health per stack.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public sealed class NetworkPlayerLoadout : NetworkBehaviour
{
    public const float DamagePerWeaponTier = 5f;
    public const float MoveSpeedPerUpgrade = 0.1f;
    public const float MaxHealthPerUpgrade = 25f;
    public const int MaxFood = 5;

    /// <summary>
    /// Weapon tier 0 shows Sword2 from the "2D Pixel Art Icons Swords"
    /// sheet (the blade the crew already starts with) through tier
    /// <see cref="MaxWeaponTier"/>, which shows Sword15 -- the last sprite
    /// in that sheet. The sword shop can never sell past that point.
    /// </summary>
    public const int BaseSwordSpriteNumber = 2;
    public const int MaxSwordSpriteNumber = 15;
    public const int MaxWeaponTier = MaxSwordSpriteNumber - BaseSwordSpriteNumber;
    [Header("Food")]
    [SerializeField]
    [Min(1f)]
    private float foodHealAmount = 25f;

    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip eatingSound;

    public readonly NetworkVariable<int> WeaponTier =
        new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    public readonly NetworkVariable<int> SpeedUpgrades =
        new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    public readonly NetworkVariable<int> HealthUpgrades =
        new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    /// <summary>
    /// Plunder carried by this player, spent at the shop island's stalls.
    /// Server-authoritative like every other loadout value, so a client
    /// cannot mint coins by editing its own copy.
    /// </summary>
    public readonly NetworkVariable<int> Coins =
        new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );
    public readonly NetworkVariable<int> FoodCount =
    new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// How many times THIS player has bought the ship shop's hull/patch
    /// upgrade. Tracked per player (like every other shop price) so the
    /// price keeps climbing for them specifically, and on the player's own
    /// NetworkObject so it carries between the first and second shop
    /// islands rather than resetting with each stall's own scene instance.
    /// The actual bonus this buys lives on NetworkRunState, since it
    /// upgrades the crew's one shared ship, not this player individually.
    /// </summary>
    public readonly NetworkVariable<int> ShipUpgradePurchases =
        new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    /// <summary>How many hot meals this player has bought at the shop.</summary>
    public readonly NetworkVariable<int> HealPurchases =
        new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    /// <summary>
    /// Running total of coins this player has ever spent, across every
    /// stall on every shop island. Purely informational for now (stats/UI);
    /// never decremented.
    /// </summary>
    public readonly NetworkVariable<int> TotalCoinsSpent =
        new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    public float BonusDamage =>
        Mathf.Max(0, WeaponTier.Value) * DamagePerWeaponTier;

    public float MoveSpeedMultiplier =>
        1f + Mathf.Max(0, SpeedUpgrades.Value) * MoveSpeedPerUpgrade;

    public float BonusMaxHealth =>
        Mathf.Max(0, HealthUpgrades.Value) * MaxHealthPerUpgrade;

    /// <summary>True once the sword shop has nothing left to sell this player.</summary>
    public bool IsWeaponMaxed => WeaponTier.Value >= MaxWeaponTier;

    /// <summary>
    /// Which sprite in the "2D Pixel Art Icons Swords" sheet the HUD should
    /// show for this player's current weapon tier.
    /// </summary>
    public int CurrentSwordSpriteNumber => Mathf.Clamp(
        BaseSwordSpriteNumber + WeaponTier.Value,
        BaseSwordSpriteNumber,
        MaxSwordSpriteNumber
    );

    /// <summary>Server-only: adds plundered coins to this player.</summary>
    public bool AddCoinsServer(int amount)
    {
        if (!IsSpawned || !IsServer || amount <= 0)
        {
            return false;
        }

        Coins.Value += amount;
        return true;
    }

    public bool AddFoodServer(int amount = 1)
    {
        if (
            !IsSpawned ||
            !IsServer ||
            amount <= 0 ||
            FoodCount.Value >= MaxFood
        )
        {
            return false;
        }

        FoodCount.Value = Mathf.Min(
            MaxFood,
            FoodCount.Value + amount
        );

        Debug.Log(
            $"[Food Inventory] {name} now has " +
            $"{FoodCount.Value}/{MaxFood} food.",
            this
        );

        return true;
    }


    [Rpc(SendTo.Owner)]
    private void PlayEatingSoundRpc()
    {
        if (audioSource != null && eatingSound != null)
        {
            audioSource.PlayOneShot(eatingSound);
        }
    }
    /// <summary>
    /// Server-only: spends coins if the player can afford it. Returns false
    /// and changes nothing when they cannot, so callers can drive this
    /// straight from a purchase without checking the balance twice.
    /// </summary>
    public bool TrySpendCoinsServer(int price)
    {
        if (!IsSpawned || !IsServer || price < 0 || Coins.Value < price)
        {
            return false;
        }

        Coins.Value -= price;
        TotalCoinsSpent.Value += price;
        return true;
    }

    /// <summary>
    /// Server-only: raises this player's weapon tier by one, up to
    /// <see cref="MaxWeaponTier"/> (Sword15, the last blade in the sheet).
    /// Returns false once already maxed so a vendor never takes coins for
    /// a sharpen that has nothing left to do.
    /// </summary>
    public bool GrantWeaponServer()
    {
        if (!IsSpawned || !IsServer || IsWeaponMaxed)
        {
            return false;
        }

        WeaponTier.Value++;

        Debug.Log(
            $"[Loadout] {name} reached weapon tier {WeaponTier.Value} " +
            $"(Sword{CurrentSwordSpriteNumber}, +{BonusDamage} damage).",
            this
        );
        return true;
    }

    /// <summary>
    /// Server-only: funds a ship shop purchase. The price this player pays
    /// climbs with <see cref="ShipUpgradePurchases"/> like any other stall,
    /// but the actual hull/patch capacity this buys is shared crew-wide on
    /// <see cref="DeadmansTales.Networking.NetworkRunState"/> rather than on
    /// this player, since there is only one ship.
    /// </summary>
    public bool GrantShipUpgradeServer()
    {
        if (!IsSpawned || !IsServer)
        {
            return false;
        }

        ShipUpgradePurchases.Value++;

        DeadmansTales.Networking.NetworkRunState runState =
            DeadmansTales.Networking.NetworkRunState.Instance;

        if (runState != null && runState.IsSpawned && runState.IsServer)
        {
            runState.GrantShipUpgradeServer();
        }

        Debug.Log(
            $"[Loadout] {name} funded a ship upgrade " +
            $"(purchase #{ShipUpgradePurchases.Value}).",
            this
        );
        return true;
    }

    /// <summary>Server-only: records a hot meal bought at the shop, for pricing.</summary>
    public void RecordHealPurchaseServer()
    {
        if (!IsSpawned || !IsServer)
        {
            return;
        }

        HealPurchases.Value++;
    }

    /// <summary>
    /// Server-only: grants the next run upgrade. Alternates deterministically
    /// between movement speed and maximum health so stacks stay balanced.
    /// </summary>
    public bool GrantUpgradeServer()
    {
        if (!IsSpawned || !IsServer)
        {
            return false;
        }

        if (SpeedUpgrades.Value <= HealthUpgrades.Value)
        {
            SpeedUpgrades.Value++;

            Debug.Log(
                $"[Loadout] {name} gained a speed upgrade " +
                $"(x{MoveSpeedMultiplier:0.0} movement).",
                this
            );
        }
        else
        {
            HealthUpgrades.Value++;

            // Give the player the newly added health immediately.
            PlayerHealth health = GetComponent<PlayerHealth>();

            if (health != null)
            {
                health.Heal(MaxHealthPerUpgrade);
            }

            Debug.Log(
                $"[Loadout] {name} gained a max-health upgrade " +
                $"(+{BonusMaxHealth} total).",
                this
            );
        }

        return true;
    }

    /// <summary>
    /// Called by the owning player when they attempt to consume food.
    /// </summary>
    public bool TryUseFood()
    {
        if (
            !IsSpawned ||
            !IsOwner ||
            FoodCount.Value <= 0
        )
        {
            return false;
        }

        RequestUseFoodRpc();
        return true;
    }

    /// <summary>
    /// Server validates the request, heals the player, and removes one food.
    /// </summary>
    [Rpc(SendTo.Server)]
    private void RequestUseFoodRpc(
        RpcParams rpcParams = default
    )
    {
        // Make sure the request came from this player's owning client.
        if (
            rpcParams.Receive.SenderClientId != OwnerClientId ||
            FoodCount.Value <= 0
        )
        {
            return;
        }

        PlayerHealth health = GetComponent<PlayerHealth>();

        // Do not consume food when dead or already at full health.
        if (
            health == null ||
            !health.IsAlive ||
            health.CurrentHealth.Value >= health.MaximumHealth
        )
        {
            return;
        }

        bool healed = health.Heal(
            Mathf.Max(1f, foodHealAmount)
        );

        if (!healed)
        {
            return;
        }
        FoodCount.Value--;

        PlayEatingSoundRpc();

        Debug.Log(
            $"[Food Inventory] {name} used food. " +
            $"{FoodCount.Value} remaining.",
            this
        );
    }
}
