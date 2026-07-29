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
    [Header("Food")]
    [SerializeField]
    [Min(1f)]
    private float foodHealAmount = 25f;

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
    public float BonusDamage =>
        Mathf.Max(0, WeaponTier.Value) * DamagePerWeaponTier;

    public float MoveSpeedMultiplier =>
        1f + Mathf.Max(0, SpeedUpgrades.Value) * MoveSpeedPerUpgrade;

    public float BonusMaxHealth =>
        Mathf.Max(0, HealthUpgrades.Value) * MaxHealthPerUpgrade;

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
        return true;
    }

    /// <summary>Server-only: raises this player's weapon tier by one.</summary>
    public bool GrantWeaponServer()
    {
        if (!IsSpawned || !IsServer)
        {
            return false;
        }

        WeaponTier.Value++;

        Debug.Log(
            $"[Loadout] {name} reached weapon tier {WeaponTier.Value} " +
            $"(+{BonusDamage} damage).",
            this
        );
        return true;
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

        Debug.Log(
            $"[Food Inventory] {name} used food. " +
            $"{FoodCount.Value} remaining.",
            this
        );
    }
}
