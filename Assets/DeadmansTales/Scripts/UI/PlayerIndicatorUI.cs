using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public sealed class PlayerIndicatorUI : NetworkBehaviour
{
    [SerializeField]
    private GameObject localPlayerIndicator;

    [SerializeField]
    private Image healthBarFill;

    [SerializeField]
    private Color localPlayerColor = Color.cyan;

    [SerializeField]
    private Color otherPlayerColor = Color.green;

    public override void OnNetworkSpawn()
    {
        if (localPlayerIndicator != null)
        {
            localPlayerIndicator.SetActive(IsOwner);
        }

        if (healthBarFill != null)
        {
            healthBarFill.color = IsOwner
                ? localPlayerColor
                : otherPlayerColor;
        }
    }
}