using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Swaps the hotbar's sword icon to match the local player's weapon tier,
/// using the "2D Pixel Art Icons Swords" sheet. Tier 0 (the blade the crew
/// starts with) shows Sword2; each sword shop purchase steps the icon up
/// one sprite, capping at Sword15 (NetworkPlayerLoadout.MaxWeaponTier) once
/// the blade is fully upgraded.
/// </summary>
public sealed class SwordIconHUD : MonoBehaviour
{
    [SerializeField]
    private Image swordIcon;

    [Tooltip(
        "Sword2 through Sword15, in that order -- index 0 is the tier-0 " +
        "starting blade, and the array should have exactly " +
        "NetworkPlayerLoadout.MaxWeaponTier + 1 entries."
    )]
    [SerializeField]
    private Sprite[] swordSprites;

    private NetworkPlayerLoadout cachedLoadout;

    private void Update()
    {
        if (swordIcon == null || swordSprites == null || swordSprites.Length == 0)
        {
            return;
        }

        NetworkPlayerLoadout loadout = ResolveLocalLoadout();
        int tier = loadout != null ? loadout.WeaponTier.Value : 0;
        int index = Mathf.Clamp(tier, 0, swordSprites.Length - 1);

        Sprite target = swordSprites[index];

        if (target != null && swordIcon.sprite != target)
        {
            swordIcon.sprite = target;
        }
    }

    private NetworkPlayerLoadout ResolveLocalLoadout()
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
}
