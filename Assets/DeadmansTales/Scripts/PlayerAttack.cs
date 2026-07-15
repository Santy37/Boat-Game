using UnityEngine;
using UnityEngine.EventSystems;
using Unity.Netcode;

public class PlayerAttack : MonoBehaviour
{

    [SerializeField] private Animator anim;

    [SerializeField] private float meleeSpeed;

    [SerializeField] private float damage;

    private float timeUntilMelee;
    private NetworkObject playerNetworkObject;

    private void Awake()
    {
        playerNetworkObject =GetComponentInParent<NetworkObject>();
    }

    private void Update()
    {
        if(playerNetworkObject == null ||  !playerNetworkObject.IsOwner)
        {
            return;
        }
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }


        if (timeUntilMelee <= 0f){
            if (Input.GetMouseButtonDown(0)){
                anim.SetTrigger("Attack");
                timeUntilMelee = meleeSpeed;
            }
        }
        else {
            timeUntilMelee -= Time.deltaTime;
        }
    }

    private void OnTriggerEnter2D(Collider2D other) {
        if (other.tag == "Enemy") {
            //other.GetComponent<Enemy>().TakeDamage(damage);
            Debug.Log("Enemy Hit");
        }
    }
}
