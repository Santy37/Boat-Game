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

            // Grant the new health immediately so the reward feels real.
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
}
