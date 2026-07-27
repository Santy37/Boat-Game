using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(NetworkObject))]
public sealed class PlayerGun : NetworkBehaviour
{
    [SerializeField]
    private Animator anim;

    [Header("Gun")]
    [SerializeField]
    [Min(0.05f)]
    private float fireCooldown = 0.2f;

    [SerializeField]
    [Min(0f)]
    private float damage = 10f;

    [SerializeField]
    [Min(0.5f)]
    private float range = 10f;

    [SerializeField]
    private Transform muzzle;

    [SerializeField]
    private LayerMask hitMask;

    private float nextLocalFireTime;
    private float nextServerFireTime;

    private Vector2 lastAimDirection = Vector2.right;

    private NetworkPlayerLoadout loadout;

    private static readonly int ShootTrigger =
        Animator.StringToHash("Shoot");

    private void Awake()
    {
        if (anim == null)
            anim = GetComponentInChildren<Animator>(true);

        loadout = GetComponent<NetworkPlayerLoadout>();
    }

    private void Update()
    {
        if (!IsSpawned || !IsOwner || PauseMenu.InputBlocked)
            return;

        if (EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject())
            return;

        if (DeadmansTales.UI.ShopScreenHUD.PointerOverPanel)
            return;

        if (!HotbarUI.IsSelected(2))
        {
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            TryShoot();
        }
    }

    public bool TryShoot()
    {
        if (
            !IsSpawned ||
            !IsOwner ||
            Time.unscaledTime < nextLocalFireTime
        )
        {
            return false;
        }

        nextLocalFireTime =
            Time.unscaledTime + fireCooldown;

        Vector2 aim = GetOwnerAimDirection();

        PlayShootAnimation(aim);

        RequestShootRpc(aim);

        return true;
    }

    private Vector2 GetOwnerAimDirection()
    {
        Camera cam = Camera.main;

        if (cam != null)
        {
            Vector2 mouse =
                cam.ScreenToWorldPoint(Input.mousePosition);

            Vector2 dir =
                mouse - (Vector2)transform.position;

            if (dir.sqrMagnitude > 0.001f)
                lastAimDirection = dir.normalized;
        }

        return lastAimDirection;
    }

    [Rpc(SendTo.Server)]
    private void RequestShootRpc(
        Vector2 aimDirection,
        RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        PlayerHealth health =
            GetComponent<PlayerHealth>();

        if (health != null && !health.IsAlive)
            return;

        if (Time.unscaledTime < nextServerFireTime)
            return;

        nextServerFireTime =
            Time.unscaledTime + fireCooldown;

        aimDirection = aimDirection.normalized;

        PlayShootAnimationRpc(
            aimDirection,
            OwnerClientId);

        Vector2 origin = muzzle.position;

        RaycastHit2D hit = Physics2D.Raycast(
            origin,
            aimDirection,
            range,
            hitMask);

        Debug.DrawRay(
            origin,
            aimDirection * range,
            Color.red,
            0.5f);

        if (!hit)
            return;

        Enemy enemy =
            hit.collider.GetComponentInParent<Enemy>();

        if (enemy == null || !enemy.IsAlive)
            return;

        float totalDamage =
            damage +
            (loadout != null
                ? loadout.BonusDamage
                : 0f);

        enemy.TakeDamage(totalDamage);
    }

    [Rpc(SendTo.Everyone)]
    private void PlayShootAnimationRpc(
        Vector2 aimDirection,
        ulong anticipatingClientId)
    {
        if (
            NetworkManager != null &&
            NetworkManager.LocalClientId ==
            anticipatingClientId
        )
        {
            return;
        }

        PlayShootAnimation(aimDirection);
    }

    private void PlayShootAnimation(Vector2 aimDirection)
    {
        if (anim == null)
            return;

        string state = ResolveShootState(aimDirection);

        if (anim.HasState(
            0,
            Animator.StringToHash(state)))
        {
            anim.Play(state, 0, 0f);
            return;
        }

        anim.ResetTrigger(ShootTrigger);
        anim.SetTrigger(ShootTrigger);
    }

    private static string ResolveShootState(Vector2 aim)
    {
        aim.Normalize();

        if (Mathf.Abs(aim.x) >= Mathf.Abs(aim.y))
        {
            return aim.x >= 0f
                ? "Shoot_Right"
                : "Shoot_Left";
        }

        return aim.y >= 0f
            ? "Shoot_Up"
            : "Shoot_Down";
    }
}