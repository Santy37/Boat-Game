using UnityEngine;

/// <summary>
/// Purely cosmetic drifting ghost soul for the kraken arena: rises slowly,
/// sways side to side, and wraps back to the bottom once it floats off the top.
/// No collider, no damage -- just atmosphere.
/// </summary>
public class SoulDrift : MonoBehaviour
{
    [SerializeField] private float riseSpeed = 0.7f;
    [SerializeField] private float swayAmount = 0.9f;
    [SerializeField] private float swaySpeed = 0.6f;
    [SerializeField] private float wrapTopY = 34f;
    [SerializeField] private float wrapBottomY = -26f;

    private float baseX;
    private float phase;

    private void Start()
    {
        baseX = transform.position.x;
        phase = Random.value * Mathf.PI * 2f;
    }

    private void Update()
    {
        Vector3 p = transform.position;
        p.y += riseSpeed * Time.deltaTime;
        if (p.y > wrapTopY)
        {
            p.y = wrapBottomY;
        }
        p.x = baseX + Mathf.Sin(Time.time * swaySpeed + phase) * swayAmount;
        transform.position = p;
    }
}
