using UnityEngine;

/// <summary>
/// A fixed cannon the player mans. Press the man key to seat at it (snapped,
/// facing, frozen); a target/reticle sprite then follows the mouse, and the
/// fire key launches a ball from the muzzle toward that target. Self-contained
/// trigger + prompt, same style as the island rowboat interaction.
/// </summary>
public class ShipCannon : MonoBehaviour
{
    [Header("Interaction")]
    [SerializeField] private KeyCode manKey = KeyCode.E;
    [SerializeField] private KeyCode fireKey = KeyCode.Space;

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

    private TopDownNetworkPlayer2D playerInRange;
    private bool manned;
    private float nextFireTime;

    private void Awake()
    {
        // Every collider on the object, not just the first one Unity hands
        // back. A cannon carries two: a solid box for the barrel you bump
        // into, and a trigger offset towards the gunner's standing spot.
        // GetComponent returned the solid one, so this logged an error about
        // a missing trigger that was sitting right beside it -- and five of
        // those on scene load will pause play mode outright if the console
        // has Error Pause on, which reads as the game refusing to start.
        bool hasTriggerArea = false;

        foreach (Collider2D area in GetComponents<Collider2D>())
        {
            if (area != null && area.isTrigger)
            {
                hasTriggerArea = true;
                break;
            }
        }

        if (!hasTriggerArea)
        {
            Debug.LogError(
                $"[ShipCannon] '{name}' needs a Collider2D with Is Trigger " +
                "enabled on this same object to act as the interaction area.",
                this
            );
        }

        if (target != null)
        {
            target.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (playerInRange == null)
        {
            return;
        }

        if (Input.GetKeyDown(manKey))
        {
            if (manned) Leave();
            else Man();
        }

        if (!manned)
        {
            return;
        }

        AimTarget();

        if (Input.GetKeyDown(fireKey) && Time.time >= nextFireTime)
        {
            Fire();
        }
    }

    // The reticle is moved with the aim keys (arrows/WASD), clamped to aimRange.
    private void AimTarget()
    {
        if (target == null)
        {
            return;
        }

        Vector2 from = muzzle != null
            ? (Vector2)muzzle.position
            : (Vector2)transform.position;

        Vector2 move = new Vector2(
            Input.GetAxisRaw("Horizontal"),
            Input.GetAxisRaw("Vertical"));

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

        Cannonball ball = Instantiate(
            cannonballPrefab, from, Quaternion.Euler(0f, 0f, angle));

        ball.Launch(direction * ballSpeed);
    }

    private void Man()
    {
        manned = true;

        if (target != null)
        {
            // Start the reticle at the muzzle (where the ball spawns from).
            Transform origin = muzzle != null ? muzzle : transform;
            target.position = origin.position;
            target.gameObject.SetActive(true);
        }

        Vector2 seat = standPoint != null
            ? (Vector2)standPoint.position
            : (Vector2)transform.position;

        playerInRange.EnterStation(seat, facing);
    }

    private void Leave()
    {
        manned = false;

        if (target != null)
        {
            target.gameObject.SetActive(false);
        }

        playerInRange.ExitStation();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TopDownNetworkPlayer2D player =
            other.GetComponentInParent<TopDownNetworkPlayer2D>();

        // TEMP diagnostic — remove once interaction works.
        Debug.Log(
            $"[ShipCannon] Trigger from '{other.name}'. " +
            $"foundPlayer={player != null}, " +
            $"isOwner={(player != null && player.IsOwner)}",
            this
        );

        if (player != null && player.IsOwner)
        {
            playerInRange = player;
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        TopDownNetworkPlayer2D player =
            other.GetComponentInParent<TopDownNetworkPlayer2D>();

        // Don't drop the gunner while they're manning the cannon — the same
        // guard ShipHelm already has, for the same reason.
        //
        // Manning teleports the player onto the stand point, and this
        // trigger is a 1x1 box while the stand point sits 1.34 units out
        // from the cannon: the seat lands barely inside the box, with the
        // player's own 0.35 collider straddling its edge. Any re-sync of the
        // ship's transform then fires an exit, and without this guard the
        // exit immediately called Leave() and cleared playerInRange -- so
        // pressing the man key seated you, unseated you, and left you
        // standing somewhere you did not choose. It read as the cannon
        // teleporting you off the ship.
        if (player != null && player == playerInRange && !manned)
        {
            playerInRange = null;
        }
    }

    private void OnGUI()
    {
        if (playerInRange == null)
        {
            return;
        }

        const float width = 360f;
        const float height = 50f;

        Rect rect = new Rect(
            (Screen.width - width) * 0.5f,
            Screen.height - 100f,
            width,
            height
        );

        GUI.Box(
            rect,
            manned
                ? $"Aim: WASD/Arrows   |   {fireKey}: Fire   |   {manKey}: Leave"
                : $"Press {manKey} to Man Cannon"
        );
    }
}
