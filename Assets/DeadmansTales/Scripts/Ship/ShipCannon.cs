using UnityEngine;

/// <summary>
/// A fixed cannon a player mans. Walk into the trigger and press YOUR interact
/// key to take it: you are snapped to the stand point, faced, frozen, and the
/// camera zooms out. Your movement keys then aim the target reticle, and your
/// fire key launches a ball from the muzzle toward it.
/// </summary>
public class ShipCannon : MonoBehaviour
{
    [Header("Player Placement")]
    [Tooltip("Where the player is snapped while manning the cannon.")]
    [SerializeField] private Transform standPoint;
    [Tooltip("Direction the player faces while manning (e.g. (0,1) = up).")]
    [SerializeField] private Vector2 facing = Vector2.up;

    [Header("Cannon")]
    [Tooltip("The cannon mouth: where the ball spawns.")]
    [SerializeField] private Transform muzzle;
    [Tooltip("The target/reticle sprite. Hidden until the cannon is manned.")]
    [SerializeField] private Transform target;
    [SerializeField] private Cannonball cannonballPrefab;
    [SerializeField] private float ballSpeed = 12f;
    [SerializeField] private float cooldown = 1f;
    [Tooltip("Max distance the reticle can be from the cannon.")]
    [SerializeField] private float aimRange = 8f;
    [Tooltip("How fast the reticle moves with the aim keys (units/sec).")]
    [SerializeField] private float aimSpeed = 6f;

    [Header("Camera")]
    [Tooltip("Camera size while manning (larger = zoomed out).")]
    [SerializeField] private float cannonCameraZoom = 16f;

    [Header("Fallback Keys")]
    [Tooltip("Used only if the player has no key bindings (networked player).")]
    [SerializeField] private KeyCode fallbackManKey = KeyCode.E;
    [SerializeField] private KeyCode fallbackFireKey = KeyCode.Space;

    private PlayerCharacter playerInRange;
    private PlayerCharacter operatorPlayer;
    private float nextFireTime;
    private LocalCoopCamera coopCamera;

    private bool Manned => operatorPlayer != null;

    private void Awake()
    {
        Collider2D area = GetComponent<Collider2D>();
        if (area == null || !area.isTrigger)
        {
            Debug.LogError(
                $"[ShipCannon] '{name}' needs a Collider2D with Is Trigger " +
                "enabled on this same object to act as the interaction area.",
                this);
        }

        coopCamera = FindFirstObjectByType<LocalCoopCamera>();

        if (target != null)
        {
            target.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        // Whoever is manning it can leave, aim and fire.
        if (Manned)
        {
            if (InteractPressed(operatorPlayer))
            {
                Leave();
                return;
            }

            AimTarget();

            if (FirePressed(operatorPlayer) && Time.time >= nextFireTime)
            {
                Fire();
            }

            return;
        }

        if (playerInRange != null && InteractPressed(playerInRange))
        {
            Man(playerInRange);
        }
    }

    private bool InteractPressed(PlayerCharacter player)
    {
        if (player == null)
        {
            return false;
        }

        return player.Bindings != null
            ? player.InteractDown
            : Input.GetKeyDown(fallbackManKey);
    }

    private bool FirePressed(PlayerCharacter player)
    {
        if (player == null)
        {
            return false;
        }

        return player.Bindings != null
            ? player.FireDown
            : Input.GetKeyDown(fallbackFireKey);
    }

    // The reticle is moved by the operator's own movement keys.
    private void AimTarget()
    {
        if (target == null)
        {
            return;
        }

        Vector2 from = muzzle != null
            ? (Vector2)muzzle.position
            : (Vector2)transform.position;

        Vector2 move = operatorPlayer.Bindings != null
            ? operatorPlayer.RawMoveInput
            : new Vector2(
                Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));

        Vector2 offset = (Vector2)target.position - from
            + move * (aimSpeed * Time.deltaTime);

        target.position = from + Vector2.ClampMagnitude(offset, aimRange);
    }

    private void Fire()
    {
        nextFireTime = Time.time + cooldown;

        Vector2 from = muzzle != null
            ? (Vector2)muzzle.position
            : (Vector2)transform.position;

        Vector2 to = target != null
            ? (Vector2)target.position
            : from + facing;

        Vector2 direction = (to - from).normalized;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        if (cannonballPrefab == null)
        {
            Debug.LogWarning("[ShipCannon] No cannonball prefab assigned.", this);
            return;
        }

        Cannonball ball = Instantiate(
            cannonballPrefab, from, Quaternion.Euler(0f, 0f, angle));

        ball.Launch(direction * ballSpeed);
    }

    private void Man(PlayerCharacter player)
    {
        operatorPlayer = player;

        if (target != null)
        {
            // Start the reticle at the muzzle (where the ball spawns from).
            Transform origin = muzzle != null ? muzzle : transform;
            target.position = origin.position;
            target.gameObject.SetActive(true);
        }

        Vector3 seat = standPoint != null
            ? standPoint.position
            : transform.position;

        player.EnterStation(seat, facing);

        if (coopCamera != null)
        {
            coopCamera.SetZoomOverride(cannonCameraZoom);
        }
    }

    private void Leave()
    {
        if (target != null)
        {
            target.gameObject.SetActive(false);
        }

        if (operatorPlayer != null)
        {
            operatorPlayer.ExitStation();
        }

        operatorPlayer = null;

        if (coopCamera != null)
        {
            coopCamera.ClearZoomOverride();
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerCharacter player = other.GetComponentInParent<PlayerCharacter>();

        if (player != null && player.IsControlledHere)
        {
            playerInRange = player;
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        PlayerCharacter player = other.GetComponentInParent<PlayerCharacter>();

        // Don't drop the operator: seating teleports them out of the trigger.
        if (player != null && player == playerInRange && !Manned)
        {
            playerInRange = null;
        }
    }

    private void OnGUI()
    {
        if (playerInRange == null && !Manned)
        {
            return;
        }

        const float width = 400f;
        const float height = 46f;

        Rect rect = new Rect(
            (Screen.width - width) * 0.5f,
            Screen.height - 150f,
            width,
            height);

        if (Manned)
        {
            KeyBindings keys = operatorPlayer.Bindings;
            string fire = keys != null
                ? keys.fire.ToString() : fallbackFireKey.ToString();
            string leave = keys != null
                ? keys.interact.ToString() : fallbackManKey.ToString();

            GUI.Box(rect, $"Aim: move keys   |   {fire}: Fire   |   {leave}: Leave");
        }
        else
        {
            KeyBindings keys = playerInRange.Bindings;
            string use = keys != null
                ? keys.interact.ToString() : fallbackManKey.ToString();

            GUI.Box(rect, $"Press {use} to Man Cannon");
        }
    }
}
