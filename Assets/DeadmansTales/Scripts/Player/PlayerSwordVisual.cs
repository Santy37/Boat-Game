using UnityEngine;

/// <summary>
/// Keeps the player's world-space equipped-sword sprite -- the "Sword/GFX"
/// child shown during a swing -- in sync with the player's current weapon
/// tier, using the same "2D Pixel Art Icons Swords" sheet as the hotbar
/// icon (see SwordIconHUD). This lives directly on the player prefab next
/// to NetworkPlayerLoadout, so unlike the HUD icon (which only matters for
/// the local player) this runs for every player's replicated instance --
/// everyone should see everyone else's blade upgrade, not just their own.
///
/// Runs in LateUpdate so it always wins over whatever the swing animation's
/// Transform/Enabled curves did earlier in the frame; the swing clips move
/// and toggle this renderer but do not keyframe which sprite it shows.
/// </summary>
[RequireComponent(typeof(NetworkPlayerLoadout))]
public sealed class PlayerSwordVisual : MonoBehaviour
{
    [Tooltip("The Sword/GFX child's SpriteRenderer -- assign explicitly; " +
        "the prefab has several other SpriteRenderers (gun, health bar, " +
        "body) that GetComponentInChildren could grab by mistake.")]
    [SerializeField]
    private SpriteRenderer swordRenderer;

    [Tooltip(
        "Sword2 through Sword15, in that order -- index 0 is the tier-0 " +
        "starting blade, and the array should have exactly " +
        "NetworkPlayerLoadout.MaxWeaponTier + 1 entries."
    )]
    [SerializeField]
    private Sprite[] swordSprites;

    private NetworkPlayerLoadout loadout;

    private void Awake()
    {
        loadout = GetComponent<NetworkPlayerLoadout>();
    }

    private void LateUpdate()
    {
        if (
            swordRenderer == null ||
            swordSprites == null ||
            swordSprites.Length == 0 ||
            loadout == null
        )
        {
            return;
        }

        int index = Mathf.Clamp(
            loadout.WeaponTier.Value,
            0,
            swordSprites.Length - 1
        );

        Sprite target = swordSprites[index];

        if (target != null && swordRenderer.sprite != target)
        {
            swordRenderer.sprite = target;
        }
    }
}
