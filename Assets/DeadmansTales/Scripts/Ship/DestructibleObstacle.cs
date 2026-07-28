using DeadmansTales.Ship;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A hazard in the water. It spawns at an obstacle point, locks a straight
/// heading toward the ship, and drifts in - if it reaches the ship it damages
/// the hull and is gone.
///
/// If <see cref="destructible"/> is ON, cannonballs whittle down its health and
/// pop it, and a small health bar floats above it. If OFF, it cannot be broken
/// (players just steer clear) and shows no bar.
///
/// Server-authoritative; the host owns it. The player's Cannonball has no
/// collider, so this hit-tests the balls against its own collider each frame.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Collider2D))]
public class DestructibleObstacle : NetworkBehaviour
{
    [Header("Destructible")]
    [Tooltip(
        "ON: cannonballs can destroy it and a health bar shows above it. " +
        "OFF: indestructible, no health bar - it can only be dodged.")]
    [SerializeField] private bool destructible = true;

    [Header("Health")]
    [SerializeField] private int maxHealth = 30;
    [SerializeField] private int cannonballDamage = 10;

    [Header("Ship Damage")]
    [Tooltip("Hull damage dealt if this reaches the ship un-destroyed.")]
    [SerializeField] private float shipDamageOnHit = 15f;

    [Header("Drift")]
    [Tooltip("Units/sec it drifts toward the ship.")]
    [SerializeField] private float driftSpeed = 1.5f;

    [Header("Cleanup")]
    [Tooltip(
        "Safety despawn so a dodged obstacle - especially an indestructible " +
        "one that can only be steered around - can't drift on forever and " +
        "stall the voyage (the progress bar waits for obstacles to clear). " +
        "Seconds after it spawns. Make this longer than it takes to reach " +
        "the ship at Drift Speed.")]
    [SerializeField] private float maxLifetimeSeconds = 30f;

    [Header("Health Bar")]
    [SerializeField] private Vector2 healthBarSize = new Vector2(52f, 8f);
    [Tooltip("How far above the obstacle (world units) the bar floats.")]
    [SerializeField] private float healthBarWorldYOffset = 0.7f;

    // Server writes, everyone reads - so the bar is correct on clients too.
    private readonly NetworkVariable<int> health =
        new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    private Collider2D hitbox;

    // Locked at spawn: a straight line toward where the ship was. It does NOT
    // re-track the ship, so steering the ship away dodges it.
    private Vector2 driftDirection = Vector2.left;

    // Server clock time at which this obstacle safety-despawns if it hasn't
    // already been destroyed or hit the ship.
    private float despawnTime;

    private Texture2D pixel;

    private void Awake()
    {
        hitbox = GetComponent<Collider2D>();

        pixel = new Texture2D(1, 1);
        pixel.SetPixel(0, 0, Color.white);
        pixel.Apply();
    }

    public override void OnDestroy()
    {
        if (pixel != null)
        {
            Destroy(pixel);
        }

        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            return;
        }

        health.Value = Mathf.Max(1, maxHealth);
        despawnTime = Time.time + Mathf.Max(1f, maxLifetimeSeconds);

        // Aim at the ship's current position and keep that heading for good.
        PlayerShipMarker ship = FindFirstObjectByType<PlayerShipMarker>();
        if (ship != null)
        {
            Vector2 toShip =
                (Vector2)(ship.transform.position - transform.position);
            if (toShip.sqrMagnitude > 0.0001f)
            {
                driftDirection = toShip.normalized;
            }
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer)
        {
            return;
        }

        transform.position +=
            (Vector3)(driftDirection * driftSpeed * Time.fixedDeltaTime);
    }

    private void Update()
    {
        if (!IsServer)
        {
            return;
        }

        // Safety despawn: a dodged obstacle (or an indestructible one that was
        // steered around) cleans itself up instead of drifting forever.
        if (Time.time >= despawnTime)
        {
            DespawnSelf();
            return;
        }

        // Indestructible obstacles ignore cannonballs entirely.
        if (!destructible || hitbox == null)
        {
            return;
        }

        // Player cannonballs carry no collider, so trigger events never fire
        // for them - test each ball against this obstacle's hitbox directly.
        foreach (Cannonball ball in
                 FindObjectsByType<Cannonball>(FindObjectsSortMode.None))
        {
            if (ball == null || !hitbox.OverlapPoint(ball.transform.position))
            {
                continue;
            }

            Destroy(ball.gameObject);   // spend the ball
            health.Value -= cannonballDamage;

            if (health.Value <= 0)
            {
                DespawnSelf();
                return;
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsServer)
        {
            return;
        }

        // Reached the player's ship -> hurt the hull and break apart.
        if (other.GetComponentInParent<PlayerShipMarker>() != null)
        {
            DamageShip();
            DespawnSelf();
        }
    }

    private void DamageShip()
    {
        NetworkShipSinkMeter meter = FindFirstObjectByType<NetworkShipSinkMeter>();
        if (meter != null)
        {
            meter.TakeDamageServer(shipDamageOnHit);
            return;
        }

        NetworkShipHealth hull = FindFirstObjectByType<NetworkShipHealth>();
        if (hull != null)
        {
            hull.TakeDamageServer(shipDamageOnHit);
            return;
        }

        if (RunContext.HasActive)
        {
            RunContext.Active.DamageShip(Mathf.CeilToInt(shipDamageOnHit));
        }
    }

    private void DespawnSelf()
    {
        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(true);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // ---------------------------------------------------------------- HUD

    private void OnGUI()
    {
        // No bar for indestructible obstacles.
        if (!destructible || pixel == null)
        {
            return;
        }

        // Only show the bar once it has actually been hit: hidden at full
        // health (and before health is initialized), hidden again once it is
        // destroyed.
        if (health.Value <= 0 || health.Value >= maxHealth)
        {
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        Vector3 screen = cam.WorldToScreenPoint(
            transform.position + Vector3.up * healthBarWorldYOffset);
        if (screen.z <= 0f)
        {
            return;   // behind the camera
        }

        float x = screen.x - healthBarSize.x * 0.5f;
        float y = Screen.height - screen.y - healthBarSize.y * 0.5f;

        float pct = maxHealth > 0
            ? Mathf.Clamp01((float)health.Value / maxHealth)
            : 0f;

        Fill(new Rect(x, y, healthBarSize.x, healthBarSize.y),
            new Color(0f, 0f, 0f, 0.7f));
        Fill(new Rect(x, y, healthBarSize.x * pct, healthBarSize.y),
            new Color(0.3f, 0.85f, 0.4f));
    }

    private void Fill(Rect rect, Color color)
    {
        Color saved = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, pixel);
        GUI.color = saved;
    }
}
