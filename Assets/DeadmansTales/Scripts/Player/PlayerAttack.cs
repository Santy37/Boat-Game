using UnityEngine;
using UnityEngine.EventSystems;
using Unity.Netcode;

public class PlayerAttack : MonoBehaviour
{
    [SerializeField] private Animator anim;
    [SerializeField] private float meleeSpeed;
    [SerializeField] private float damage;
    [SerializeField] private BoxCollider2D swordCollider;
    private float timeUntilMelee;
    private NetworkObject playerNetworkObject;
    private PlayerAnimation2D playerAnimation;

    private void Awake()
    {
        playerNetworkObject = GetComponentInParent<NetworkObject>();
        playerAnimation = GetComponent<PlayerAnimation2D>();
    }

    private void Start() {
        swordCollider.enabled = false;
    }

    private void Update()
    {
        if (playerNetworkObject == null ||
            !playerNetworkObject.IsOwner)
        {
            return;
        }

        if (EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        if (timeUntilMelee <= 0f)
        {
            if (Input.GetMouseButtonDown(0))
            {
                SetAttackDirection();
                //anim.SetTrigger("Attack");
                timeUntilMelee = meleeSpeed;
            }
        }
        else
        {
            timeUntilMelee -= Time.deltaTime;
        }
    }

    public void EnableHitbox()
    {
        swordCollider.enabled = true;
    }

    public void DisableHitbox()
    {
        swordCollider.enabled = false;
    }
    private void OnTriggerEnter2D(Collider2D other)
    {
        Enemy enemy = other.GetComponentInParent<Enemy>();

        if (enemy != null)
        {
            enemy.TakeDamage(damage);
        }
    }

    private void SetAttackDirection()
    {
        switch (playerAnimation.CurrentFacingDirection)
        {
            case PlayerAnimation2D.FacingDirection.Up:
                Debug.Log("Attacking Up");
                anim.Play("Attack_Up");
                break;

            case PlayerAnimation2D.FacingDirection.Down:
                Debug.Log("Attacking Down");
                anim.Play("Attack_Down");
                break;

            case PlayerAnimation2D.FacingDirection.Left:
                Debug.Log("Attacking Left");
                anim.Play("Attack_Left");
                break;

            case PlayerAnimation2D.FacingDirection.Right:
                Debug.Log("Attacking Right");
                anim.Play("Attack_Right");
                break;
        }
    }
}