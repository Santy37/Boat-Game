using UnityEngine;

public class PlayerAnimation2D : MonoBehaviour
{
    public enum FacingDirection
    {
        Down,
        Left,
        Right,
        Up
    }
    public FacingDirection CurrentFacingDirection => facingDirection;

    [Header("References")]
    [SerializeField] private Animator animator;

    [Header("Movement Detection")]
    [SerializeField]
    private float movementThreshold = 0.0005f;

    private Vector3 previousPosition;

    private FacingDirection facingDirection =
        FacingDirection.Down;

    private int currentStateHash;

    private void Awake()
    {
        if (animator == null)
        {
            animator =
                GetComponentInChildren<Animator>(true);
        }

        if (animator == null)
        {
            Debug.LogError(
                "[Player Animation] No Animator was found " +
                "on the player or its children.",
                this
            );

            enabled = false;
            return;
        }

        animator.applyRootMotion = false;
    }

    private void OnEnable()
    {
        previousPosition = transform.position;
        currentStateHash = 0;

        PlayCurrentState(false);
    }

    private void LateUpdate()
    {
        Vector3 currentPosition =
            transform.position;

        Vector2 movementDelta =
            currentPosition - previousPosition;

        previousPosition = currentPosition;

        bool isMoving =
            movementDelta.sqrMagnitude >
            movementThreshold * movementThreshold;

        if (isMoving)
        {
            UpdateFacingDirection(movementDelta);
        }

        PlayCurrentState(isMoving);
    }

    private void UpdateFacingDirection(
        Vector2 movementDelta
    )
    {
        if (Mathf.Abs(movementDelta.y) > movementThreshold)
        {
            facingDirection =
                movementDelta.y > 0f
                    ? FacingDirection.Up
                    : FacingDirection.Down;
        }
        else
        {
            facingDirection =
                movementDelta.x > 0f
                    ? FacingDirection.Right
                    : FacingDirection.Left;
        }
    }

    private void PlayCurrentState(bool isMoving)
    {
        string stateName =
            isMoving
                ? $"Walk_{facingDirection}"
                : $"Idle_{facingDirection}";

        int stateHash =
            Animator.StringToHash(
                $"Base Layer.{stateName}"
            );

        if (stateHash == currentStateHash)
        {
            return;
        }

        animator.Play(
            stateHash,
            0,
            0f
        );

        currentStateHash = stateHash;
    }
}