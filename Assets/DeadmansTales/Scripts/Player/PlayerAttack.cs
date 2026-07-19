using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using Unity.Netcode;

public class PlayerAttack : MonoBehaviour
{
    [SerializeField] private Animator anim;
    [SerializeField] private float meleeSpeed;
    [SerializeField] private float damage;
    [Tooltip("How long the sword can deal damage after a swing starts.")]
    [SerializeField] private float hitActiveTime = 0.2f;

    private float timeUntilMelee;
    private float hitActiveTimer;
    private NetworkObject playerNetworkObject;

    private readonly HashSet<Enemy> hitThisSwing =
        new HashSet<Enemy>();

    private void Awake()
    {
        playerNetworkObject = GetComponentInParent<NetworkObject>();
    }

    private void Update()
    {
        if (playerNetworkObject == null ||
            !playerNetworkObject.IsOwner)
        {
            return;
        }

        if (timeUntilMelee > 0f)
        {
            timeUntilMelee -= Time.deltaTime;
        }

        if (hitActiveTimer > 0f)
        {
            hitActiveTimer -= Time.deltaTime;
        }

        if (EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        if (timeUntilMelee <= 0f &&
            Input.GetMouseButtonDown(1))
        {
            StartAttack();
        }
    }

    private void StartAttack()
    {
        if (anim != null)
        {
            anim.SetTrigger("Attack");
        }

        timeUntilMelee = meleeSpeed;
        hitActiveTimer = hitActiveTime;
        hitThisSwing.Clear();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryHit(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryHit(other);
    }

    private void TryHit(Collider2D other)
    {
        if (hitActiveTimer <= 0f)
        {
            return;
        }

        Enemy enemy = other.GetComponentInParent<Enemy>();

        if (enemy == null || !hitThisSwing.Add(enemy))
        {
            return;
        }

        enemy.TakeDamage(damage);
    }
}
