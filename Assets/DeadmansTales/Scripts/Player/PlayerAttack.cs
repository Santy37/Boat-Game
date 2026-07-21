using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

/// <summary>
/// Owner-requested, server-validated melee combat.
///
/// The owning client sends intent only. The server enforces cooldown, checks
/// that the player is alive, performs hit detection, and changes enemy health.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public sealed class PlayerAttack : NetworkBehaviour
{
    private static readonly int AttackTrigger = Animator.StringToHash("Attack");

    [SerializeField]
    private Animator anim;

    [SerializeField]
    [Min(0.05f)]
    [FormerlySerializedAs("meleeSpeed")]
    private float attackCooldown = 0.4f;

    [SerializeField]
    [Min(0f)]
    private float inputBufferSeconds = 0.12f;

    [SerializeField]
    [Min(0f)]
    private float damage = 10f;

    [Header("Server Hit Validation")]
    [SerializeField]
    [Min(0.1f)]
    private float attackRadius = 1.35f;

    [SerializeField]
    private Vector2 attackOffset = new Vector2(0f, 0.35f);

    [SerializeField]
    [Min(1)]
    private int maximumTargetsPerSwing = 4;

    private float nextLocalAttackTime;
    private float nextServerAttackTime;
    private float bufferedAttackUntil = float.NegativeInfinity;
    private NetworkPlayerLoadout loadout;
    private MeleeSwingVisual swingVisual;
    private Vector2 lastAimDirection = Vector2.down;

    private void Awake()
    {
        if (anim == null)
        {
            anim = GetComponentInChildren<Animator>(true);
        }

        loadout = GetComponent<NetworkPlayerLoadout>();
    }

    private void Update()
    {
        if (!IsSpawned || !IsOwner || PauseMenu.InputBlocked)
        {
            return;
        }

        if (
            EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject()
        )
        {
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            bufferedAttackUntil =
                Time.unscaledTime + Mathf.Max(0f, inputBufferSeconds);
        }

        if (
            Time.unscaledTime < nextLocalAttackTime ||
            Time.unscaledTime > bufferedAttackUntil
        )
        {
            return;
        }

        TryAttack();
    }

    /// <summary>
    /// Starts an owner attack immediately when the local cooldown allows it.
    /// Input code and automated runtime validation share this same path.
    /// </summary>
    public bool TryAttack()
    {
        if (
            !IsSpawned ||
            !IsOwner ||
            PauseMenu.InputBlocked ||
            Time.unscaledTime < nextLocalAttackTime
        )
        {
            return false;
        }

        bufferedAttackUntil = float.NegativeInfinity;
        nextLocalAttackTime =
            Time.unscaledTime + Mathf.Max(0.05f, attackCooldown);

        Vector2 aimDirection = GetOwnerAimDirection();

        // Anticipate only the animation locally. Damage and cooldown validation
        // remain authoritative on the server.
        PlayAttackAnimation(aimDirection);
        RequestAttackRpc(aimDirection);
        return true;
    }

    private Vector2 GetOwnerAimDirection()
    {
        Camera viewCamera = Camera.main;
        if (viewCamera != null)
        {
            Vector3 mouseWorld = viewCamera.ScreenToWorldPoint(
                Input.mousePosition
            );
            Vector2 toMouse =
                (Vector2)(mouseWorld - transform.position);

            if (toMouse.sqrMagnitude > 0.01f)
            {
                lastAimDirection = toMouse.normalized;
            }
        }

        return lastAimDirection;
    }

    [Rpc(SendTo.Server)]
    private void RequestAttackRpc(
        Vector2 aimDirection,
        RpcParams rpcParams = default
    )
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            return;
        }

        PlayerHealth health = GetComponent<PlayerHealth>();
        if (health != null && !health.IsAlive)
        {
            return;
        }

        if (Time.unscaledTime < nextServerAttackTime)
        {
            return;
        }

        nextServerAttackTime =
            Time.unscaledTime + Mathf.Max(0.05f, attackCooldown);

        aimDirection = SanitizeAimDirection(aimDirection);
        PlayAttackAnimationRpc(aimDirection, OwnerClientId);
        ApplyServerHits(aimDirection);
    }

    private static Vector2 SanitizeAimDirection(Vector2 direction)
    {
        return direction.sqrMagnitude < 0.0001f
            ? Vector2.down
            : direction.normalized;
    }

    [Rpc(SendTo.Everyone)]
    private void PlayAttackAnimationRpc(
        Vector2 aimDirection,
        ulong anticipatingClientId
    )
    {
        // The attacking owner already played this frame immediately on input.
        // Everyone else receives the server-confirmed visual.
        if (
            NetworkManager != null &&
            NetworkManager.LocalClientId == anticipatingClientId
        )
        {
            return;
        }

        PlayAttackAnimation(aimDirection);
    }

    private void PlayAttackAnimation(Vector2 aimDirection)
    {
        if (swingVisual == null)
        {
            swingVisual = MeleeSwingVisual.CreateFor(transform);
        }

        swingVisual.Play(SanitizeAimDirection(aimDirection));

        if (anim == null)
        {
            return;
        }

        // Javier's four directional swing clips. The state is chosen from
        // the aim vector rather than from PlayerAnimation2D's local facing
        // enum, because this method also runs on every remote client from
        // PlayAttackAnimationRpc — the aim direction is networked, a
        // remote player's local facing is not, so this is what makes
        // everyone see the same swing.
        string directionalState = ResolveAttackState(aimDirection);

        if (directionalState != null && HasState(directionalState))
        {
            anim.Play(directionalState, 0, 0f);
            return;
        }

        anim.ResetTrigger(AttackTrigger);
        anim.SetTrigger(AttackTrigger);
    }

    private static string ResolveAttackState(Vector2 aimDirection)
    {
        Vector2 aim = SanitizeAimDirection(aimDirection);

        if (Mathf.Abs(aim.x) >= Mathf.Abs(aim.y))
        {
            return aim.x >= 0f ? "Attack_Right" : "Attack_Left";
        }

        return aim.y >= 0f ? "Attack_Up" : "Attack_Down";
    }

    /// <summary>
    /// Guards against controllers that predate the directional clips, so
    /// a rig still on the single "Attack" trigger keeps working instead of
    /// silently playing nothing.
    /// </summary>
    private bool HasState(string stateName)
    {
        return anim.HasState(0, Animator.StringToHash(stateName));
    }

    private void ApplyServerHits(Vector2 aimDirection)
    {
        // The swing reaches forward toward the aim so attacks feel
        // directional instead of a uniform circle around the player.
        Vector2 center =
            (Vector2)transform.position +
            attackOffset +
            aimDirection * (Mathf.Max(0.1f, attackRadius) * 0.45f);
        Collider2D[] overlaps = Physics2D.OverlapCircleAll(
            center,
            Mathf.Max(0.1f, attackRadius)
        );

        float totalDamage = Mathf.Max(0f, damage) +
            (loadout != null ? loadout.BonusDamage : 0f);

        HashSet<Enemy> hitEnemies = new HashSet<Enemy>();
        int maximumTargets = Mathf.Max(1, maximumTargetsPerSwing);

        foreach (Collider2D overlap in overlaps)
        {
            Enemy enemy = overlap.GetComponentInParent<Enemy>();

            if (
                enemy == null ||
                !enemy.IsAlive ||
                !hitEnemies.Add(enemy)
            )
            {
                continue;
            }

            enemy.TakeDamage(totalDamage);

            if (hitEnemies.Count >= maximumTargets)
            {
                break;
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(
            (Vector2)transform.position + attackOffset,
            Mathf.Max(0.1f, attackRadius)
        );
    }
}
