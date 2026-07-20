using UnityEngine;

/// <summary>
/// Drives locomotion animation and sprite facing purely from observed
/// movement. Works identically on server and clients because enemy positions
/// replicate through NetworkTransform — no extra network traffic needed.
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyMotionAnimator : MonoBehaviour
{
    private static readonly int SpeedParameter =
        Animator.StringToHash("Speed");

    [SerializeField]
    private Animator animator;

    [SerializeField]
    private SpriteRenderer facingRenderer;

    [SerializeField]
    [Min(0f)]
    private float speedSmoothing = 12f;

    private Vector3 lastPosition;
    private float smoothedSpeed;
    private bool hasSpeedParameter;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }

        if (facingRenderer == null)
        {
            facingRenderer = GetComponentInChildren<SpriteRenderer>(true);
        }

        if (animator != null)
        {
            foreach (AnimatorControllerParameter parameter in
                animator.parameters)
            {
                if (parameter.nameHash == SpeedParameter)
                {
                    hasSpeedParameter = true;
                    break;
                }
            }
        }

        lastPosition = transform.position;
    }

    private void Update()
    {
        Vector3 delta = transform.position - lastPosition;
        lastPosition = transform.position;

        if (Time.deltaTime <= 0f)
        {
            return;
        }

        float instantaneousSpeed = delta.magnitude / Time.deltaTime;
        smoothedSpeed = Mathf.Lerp(
            smoothedSpeed,
            instantaneousSpeed,
            1f - Mathf.Exp(-speedSmoothing * Time.deltaTime)
        );

        if (animator != null && hasSpeedParameter)
        {
            animator.SetFloat(SpeedParameter, smoothedSpeed);
        }

        if (
            facingRenderer != null &&
            Mathf.Abs(delta.x) > 0.001f
        )
        {
            facingRenderer.flipX = delta.x < 0f;
        }
    }
}
