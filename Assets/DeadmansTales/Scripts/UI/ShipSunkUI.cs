using DeadmansTales.Ship;
using UnityEngine;

public sealed class ShipSunkUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField]
    private GameObject shipSunkPanel;

    [SerializeField]
    private GameObject hotbar;

    [Header("Pause Menu")]
    [SerializeField]
    private PauseMenu pauseMenu;

    private NetworkShipHealth shipHealth;
    private bool hasShown;

    private void Awake()
    {
        if (shipSunkPanel != null)
        {
            shipSunkPanel.SetActive(false);
        }
    }

    private void Update()
    {
        if (hasShown)
        {
            return;
        }

        if (shipHealth == null || !shipHealth.IsSpawned)
        {
            FindPlayerShip();
        }

        // Backup check in case the ship reached zero before
        // this script subscribed to the ShipSunk event.
        if (
            shipHealth != null &&
            shipHealth.IsSpawned &&
            shipHealth.IsSunk
        )
        {
            HandleShipSunk();
        }
    }

    private void FindPlayerShip()
    {
        PlayerShipMarker playerShip =
            FindFirstObjectByType<PlayerShipMarker>();

        NetworkShipHealth foundHealth = playerShip != null
            ? playerShip.GetComponent<NetworkShipHealth>()
            : null;

        if (foundHealth == shipHealth)
        {
            return;
        }

        UnsubscribeFromShip();

        shipHealth = foundHealth;

        if (shipHealth != null)
        {
            shipHealth.ShipSunk += HandleShipSunk;
        }
    }

    private void HandleShipSunk()
    {
        if (hasShown)
        {
            return;
        }

        hasShown = true;

        if (pauseMenu != null)
        {
            pauseMenu.SetDeathScreenBlocking(true);
        }

        if (hotbar != null)
        {
            hotbar.SetActive(false);
        }

        if (shipSunkPanel != null)
        {
            shipSunkPanel.SetActive(true);
            shipSunkPanel.transform.SetAsLastSibling();
        }
    }

    private void UnsubscribeFromShip()
    {
        if (shipHealth != null)
        {
            shipHealth.ShipSunk -= HandleShipSunk;
        }
    }

    private void OnDestroy()
    {
        UnsubscribeFromShip();
    }
}