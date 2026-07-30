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
    [SerializeField] private float maxLifetimeSeconds = 60f;

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

    // Locked at spawn by SetCourseServer: a straight line toward where the ship
    // was. It does NOT re-track the ship, so steering away dodges it.
    private Vector2 driftDirection = Vector2.left;

    // Server clock time at which this obstacle safety-despawns if it hasn't
    // already been destroyed or hit the ship.
    private float despawnTime;

    // The player ship's hull collider, resolved once and cached.
    private Collider2D shipHitbox;
    private bool shipSearched;

    [Tooltip("Played on every peer when this rock breaks on the ship's hull.")]
    [SerializeField] private AudioClip hullImpactClip;

    [SerializeField]
    [Range(0f, 1f)]
    private float hullImpactVolume = 1f;

    // Set the instant this obstacle hits the ship, so the damage + despawn
    // fire exactly once even if both detection paths notice in the same step.
    private bool hitShip;

    // Server-only: assigned by the generator that spawned this obstacle.
    private BoatObstacleGenerator owningGenerator;

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

        // The heading is handed to us by BoatObstacleGenerator via
        // SetCourseServer right after spawn -- we never aim ourselves.
    }

    /// <summary>
    /// Server-only: locks this obstacle onto a fixed straight-line heading,
    /// set once by the spawner at spawn time. The obstacle drifts along it
    /// forever and never re-aims, so it does not follow the ship -- the line
    /// stays exactly where it was drawn.
    /// </summary>
    public void SetCourseServer(Vector2 direction)
    {
        if (!IsServer)
        {
            return;
        }

        if (direction.sqrMagnitude > 0.0001f)
        {
            driftDirection = direction.normalized;
        }
    }

    /// <summary>
    /// Server-only: overrides the prefab's Drift Speed, so the spawner that
    /// created this obstacle can control how fast it closes on the ship.
    /// </summary>
    /// <summary>
    /// Server-only: the generator that spawned this obstacle, so it can be
    /// asked whether its wave is already clearing before the hull-impact clip
    /// plays. Null for an obstacle placed by hand, which then always plays.
    /// </summary>
    public void SetOwningGeneratorServer(BoatObstacleGenerator generator)
    {
        owningGenerator = generator;
    }

    public void SetSpeedServer(float speed)
    {
        if (!IsServer)
        {
            return;
        }

        if (speed >= 0f)
        {
            driftSpeed = speed;
        }
    }

    /// <summary>
    /// Server-only: applies one cannon hit's worth of damage (this obstacle's
    /// own Cannonball Damage) and despawns it if that brings health to 0.
    /// Called both by the local Cannonball hit-test below and by
    /// NetworkCannonball in multiplayer. Returns true if the hit destroyed it.
    /// </summary>
    public bool ApplyCannonHitServer()
    {
        if (!IsServer || !destructible)
        {
            return false;
        }

        health.Value -= cannonballDamage;

        if (health.Value <= 0)
        {
            DespawnSelf();
            return true;
        }

        return false;
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

        // Reached the player's ship? -> damage the hull and break apart. This
        // is an explicit geometric overlap test rather than trusting
        // OnTriggerEnter2D: the obstacle is moved by writing its Transform each
        // step with no rigidbody velocity, so its Dynamic body falls asleep
        // long before it finishes the slow drift in -- and a sleeping 2D body
        // stops raising trigger callbacks, which silently swallowed the hit.
        // Collider2D.Distance queries the geometry directly, sleep or not.
        if (hitbox != null && OverlapsPlayerShip())
        {
            HitShip();
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

            if (ApplyCannonHitServer())
            {
                return;
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsServer || hitShip)
        {
            return;
        }

        // Reached the player's ship -> hurt the hull and break apart. Kept as a
        // fast path alongside the Update() overlap test; whichever notices
        // first wins, and HitShip() makes sure it only counts once.
        if (other.GetComponentInParent<PlayerShipMarker>() != null)
        {
            HitShip();
        }
    }

    // Applies the hit exactly once: damage the ship, then despawn this obstacle.
    private void HitShip()
    {
        if (hitShip)
        {
            return;
        }

        hitShip = true;
        DamageShip();

        // Told to every peer before the despawn, so the crack of the rock on
        // the hull is heard by the whole crew and not just the server.
        // Skip the clip if this wave is already down to its last rock: the
        // impact would land at the exact instant the progress bar resumes and
        // slides past the rock icon, which reads as the BAR making a
        // destruction noise. The hit and its ship damage still happen.
        if (owningGenerator == null || !owningGenerator.WaveClearing)
        {
            PlayHullImpactClientRpc(transform.position);
        }

        DespawnSelf();
    }

    [ClientRpc]
    private void PlayHullImpactClientRpc(Vector3 position)
    {
        if (hullImpactClip == null)
        {
            return;
        }

        // PlayClipAtPoint, not an AudioSource on this obstacle: it despawns
        // the instant it lands, which would cut its own impact off.
        AudioSource.PlayClipAtPoint(
            hullImpactClip, position, hullImpactVolume);
    }

    // True once this obstacle's hull overlaps the player ship's hull collider.
    private bool OverlapsPlayerShip()
    {
        Collider2D shipBox = ResolveShipHitbox();

        return shipBox != null &&
               shipBox.isActiveAndEnabled &&
               hitbox.Distance(shipBox).isOverlapped;
    }

    // The player ship's hull collider (PlayerShipMarker.Hitbox), found once and
    // cached. Keeps searching until a player ship exists in the scene.
    private Collider2D ResolveShipHitbox()
    {
        if (!shipSearched)
        {
            PlayerShipMarker ship = FindFirstObjectByType<PlayerShipMarker>();
            if (ship != null)
            {
                shipHitbox = ship.Hitbox;
                shipSearched = true;
            }
        }

        return shipHitbox;
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

        // Show the bar the whole time the obstacle is alive. Hidden only
        // before its health is initialized (0) or once it is destroyed.
        if (health.Value <= 0)
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
