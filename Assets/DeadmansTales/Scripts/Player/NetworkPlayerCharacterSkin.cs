using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Lets each player pick which available character art their avatar uses
/// (chosen on the main menu's character select screen, saved locally in
/// PlayerPrefs). The choice is server-authoritative and replicated so every
/// client renders the same look for every player, not just their own.
/// </summary>
[DisallowMultipleComponent]
public sealed class NetworkPlayerCharacterSkin : NetworkBehaviour
{
    public const string PlayerPrefsKey = "DeadmansTales.CharacterSkin";
    public const int SoldierSkinIndex = 0;
    public const int OrcSkinIndex = 1;

    [SerializeField]
    private SpriteRenderer gfxRenderer;

    [SerializeField]
    private Animator gfxAnimator;

    [SerializeField]
    private Sprite soldierIdleSprite;

    [SerializeField]
    private RuntimeAnimatorController soldierController;

    [SerializeField]
    private Sprite orcIdleSprite;

    [SerializeField]
    private RuntimeAnimatorController orcController;

    public readonly NetworkVariable<int> SkinIndex =
        new NetworkVariable<int>(
            SoldierSkinIndex,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        SkinIndex.OnValueChanged += HandleSkinChanged;
        ApplySkin(SkinIndex.Value);

        if (IsOwner)
        {
            int localChoice =
                PlayerPrefs.GetInt(PlayerPrefsKey, SoldierSkinIndex);
            RequestSkinRpc(localChoice);
        }
    }

    public override void OnNetworkDespawn()
    {
        SkinIndex.OnValueChanged -= HandleSkinChanged;
        base.OnNetworkDespawn();
    }

    [Rpc(SendTo.Server)]
    private void RequestSkinRpc(
        int skinIndex,
        RpcParams rpcParams = default
    )
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            return;
        }

        SkinIndex.Value = Mathf.Clamp(skinIndex, SoldierSkinIndex, OrcSkinIndex);
    }

    private void HandleSkinChanged(int previousValue, int currentValue)
    {
        ApplySkin(currentValue);
    }

    private void ApplySkin(int skinIndex)
    {
        if (gfxRenderer == null)
        {
            return;
        }

        bool useOrc = skinIndex == OrcSkinIndex;

        Sprite idleSprite = useOrc ? orcIdleSprite : soldierIdleSprite;
        if (idleSprite != null)
        {
            gfxRenderer.sprite = idleSprite;
        }

        if (gfxAnimator != null)
        {
            RuntimeAnimatorController controller =
                useOrc ? orcController : soldierController;

            if (controller != null)
            {
                gfxAnimator.runtimeAnimatorController = controller;
            }
        }
    }
}
