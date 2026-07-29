using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public sealed class HotbarUI : MonoBehaviour
{
    public static int SelectedSlot { get; private set; } = 1;

    [Header("Hotbar Slots")]
    [SerializeField] private RectTransform swordSlot;
    [SerializeField] private RectTransform gunSlot;
    [SerializeField] private RectTransform foodSlot;

    [Header("Selection Borders")]
    [SerializeField] private GameObject swordSelectedFrame;
    [SerializeField] private GameObject gunSelectedFrame;
    [SerializeField] private GameObject foodSelectedFrame;

    [Header("Food")]
    [SerializeField]
    private TextMeshProUGUI foodCountText;

    [Header("Gun Cooldown")]
    [SerializeField]
    private Image gunCooldownOverlay;

    [Header("Sword Cooldown")]
    [SerializeField]
    private Image swordCooldownOverlay;

    private NetworkPlayerLoadout cachedLoadout;
    private PlayerGun cachedGun;
    private PlayerAttack cachedAttack;
    private void Start()
    {
        SelectSlot(1);
    }

    private void Update()
    {
        RefreshFoodCount();
        RefreshGunCooldown();
        RefreshSwordCooldown();

        if (PauseMenu.InputBlocked)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            SelectSlot(1);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            SelectSlot(2);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            SelectSlot(3);
        }

        if (
            SelectedSlot == 3 &&
            Input.GetMouseButtonDown(0)
        )
        {
            NetworkPlayerLoadout loadout = ResolveLocalLoadout();

            if (loadout != null)
            {
                loadout.TryUseFood();
            }
        }
    }
    private void RefreshFoodCount()
    {
        NetworkPlayerLoadout loadout = ResolveLocalLoadout();

        if (foodCountText == null)
        {
            return;
        }

        foodCountText.text = loadout != null
            ? $"x{loadout.FoodCount.Value}"
            : "x0";
    }
    private void RefreshSwordCooldown()
    {
        if (swordCooldownOverlay == null)
        {
            return;
        }

        PlayerAttack attack = ResolveLocalAttack();

        float remaining = attack != null
            ? attack.CooldownRemainingNormalized
            : 0f;

        swordCooldownOverlay.fillAmount = remaining;

        swordCooldownOverlay.gameObject.SetActive(
            remaining > 0.001f
        );
    }

    private PlayerAttack ResolveLocalAttack()
    {
        if (cachedAttack != null)
        {
            return cachedAttack;
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

        cachedAttack = manager.LocalClient.PlayerObject
            .GetComponent<PlayerAttack>();

        return cachedAttack;
    }
    private void RefreshGunCooldown()
    {
        if (gunCooldownOverlay == null)
        {
            return;
        }

        PlayerGun gun = ResolveLocalGun();

        float remaining = gun != null
            ? gun.CooldownRemainingNormalized
            : 0f;

        gunCooldownOverlay.fillAmount = remaining;
        gunCooldownOverlay.gameObject.SetActive(
            remaining > 0.001f
        );
    }

    private PlayerGun ResolveLocalGun()
    {
        if (cachedGun != null)
        {
            return cachedGun;
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

        cachedGun = manager.LocalClient.PlayerObject
            .GetComponent<PlayerGun>();

        return cachedGun;
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
    public void SelectSlot(int slotNumber)
    {
        RectTransform selectedSlot = GetSlot(slotNumber);

        if (selectedSlot == null)
        {
            return;
        }

        SelectedSlot = slotNumber;

        selectedSlot.SetSiblingIndex(1);

        swordSelectedFrame.SetActive(slotNumber == 1);
        gunSelectedFrame.SetActive(slotNumber == 2);
        foodSelectedFrame.SetActive(slotNumber == 3);
    }

    private RectTransform GetSlot(int slotNumber)
    {
        switch (slotNumber)
        {
            case 1:
                return swordSlot;

            case 2:
                return gunSlot;

            case 3:
                return foodSlot;

            default:
                return null;
        }
    }

    public static bool IsSelected(int slotNumber)
    {
        return SelectedSlot == slotNumber;
    }
}