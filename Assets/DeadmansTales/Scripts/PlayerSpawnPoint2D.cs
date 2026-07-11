using UnityEngine;

public class PlayerSpawnPoint2D : MonoBehaviour
{
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;

        Gizmos.DrawWireSphere(
            transform.position,
            0.35f
        );

        Gizmos.DrawLine(
            transform.position + Vector3.left * 0.45f,
            transform.position + Vector3.right * 0.45f
        );

        Gizmos.DrawLine(
            transform.position + Vector3.down * 0.45f,
            transform.position + Vector3.up * 0.45f
        );
    }
}