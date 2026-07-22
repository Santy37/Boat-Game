using UnityEngine;

public class PlayerAnimation2D : MonoBehaviour
{
    private enum FacingDirection
    {
        Down,
        Left,
        Right,
        Up
    }

    [Header("References")]
    [SerializeField] private Animator animator;

    [Header("Movement Detection")]
    [SerializeField]
    private float movementThreshold = 0.0005f;

    private Vector3 previousPosition;

    private FacingDirection facingDirection =
        FacingDirection.Down;

    private int currentStateHash;

    private bool facingLocked;

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
        // While seated at a station, keep the forced facing and ignore movement.
        if (facingLocked)
        {
            PlayCurrentState(false);
            previousPosition = transform.position;
            return;
        }

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
        if (
            Mathf.Abs(movementDelta.x) >
            Mathf.Abs(movementDelta.y)
        )
        {
            facingDirection =
                movementDelta.x > 0f
                    ? FacingDirection.Right
                    : FacingDirection.Left;
        }
        else
        {
            facingDirection =
                movementDelta.y > 0f
                    ? FacingDirection.Up
                    : FacingDirection.Down;
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

    /// <summary>
    /// Force the character to face a direction and stop reacting to movement
    /// (used while seated at a station). Pass a direction such as Vector2.up.
    /// </summary>
    public void LockFacing(Vector2 direction)
    {
        facingLocked = true;

        if (direction.sqrMagnitude > 0.0001f)
        {
            UpdateFacingDirection(direction);
        }

        PlayCurrentState(false);
    }

    /// <summary>Resume normal movement-based facing.</summary>
    public void UnlockFacing()
    {
        facingLocked = false;
        previousPosition = transform.position;
    }
}