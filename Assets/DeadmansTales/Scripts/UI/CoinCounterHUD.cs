using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Keeps the CoinHUD's on-screen count (PauseMenuUI/Canvas/CoinHUD/
/// CoinCountText) synced to the local player's real coin balance.
///
/// Coins were previously only visible through CoinPurseHUD's OnGUI dev
/// overlay — this is the actual HUD counter players see, driven the same
/// way HotbarUI drives the food count: resolve the local player's
/// NetworkPlayerLoadout and read its server-authoritative NetworkVariable
/// each frame.
/// </summary>
public sealed class CoinCounterHUD : MonoBehaviour
{
    [SerializeField]
    private TextMeshProUGUI coinCountText;

    private NetworkPlayerLoadout cachedLoadout;

    private void Update()
    {
        if (coinCountText == null)
        {
            return;
        }

        NetworkPlayerLoadout loadout = ResolveLocalLoadout();

        coinCountText.text = loadout != null
            ? $"x{loadout.Coins.Value}"
            : "x0";
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
