using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative boss health for the kraken, mirroring
/// DeadmansTales.Ship.NetworkShipHealth's pattern: the server owns
/// CurrentHealth, every peer's HUD/flash reacts to the replicated value
/// changing, and only the server ever actually despawns the kraken.
///
/// Previously this was a plain, non-networked int -- damage only ever
/// applied on whichever single peer's local copy of this component a
/// cannonball happened to hit, so a dedicated server plus remote clients
/// would never agree on the kraken's health or see it die together. Fine
/// for one person hosting and playing (the normal classroom test setup,
/// where host and client are the same process), but not correct in general.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class KrakenHealth : NetworkBehaviour
{
    [SerializeField]
    [Min(1)]
    private int maximumHealth = 20;

    [SerializeField]
    private float flashSeconds = 0.1f;

    public readonly NetworkVariable<int> CurrentHealth =
        new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private SpriteRenderer sprite;
    private float flashUntil;
    private Color baseColor;

    public int MaximumHealth => Mathf.Max(1, maximumHealth);

    public bool IsDead => IsSpawned && CurrentHealth.Value <= 0;

    /// <summary>0..1 for a health bar or HUD.</summary>
    public float HealthFraction =>
        Mathf.Clamp01((float)CurrentHealth.Value / MaximumHealth);

    /// <summary>Raised on every peer once the kraken reaches zero.</summary>
    public event System.Action Defeated;

    private void Awake()
    {
        sprite = GetComponentInChildren<SpriteRenderer>();
        if (sprite != null)
        {
            baseColor = sprite.color;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        CurrentHealth.OnValueChanged += HandleHealthChanged;

        if (IsServer && CurrentHealth.Value <= 0)
        {
            CurrentHealth.Value = MaximumHealth;
        }
    }

    public override void OnNetworkDespawn()
    {
        CurrentHealth.OnValueChanged -= HandleHealthChanged;

        base.OnNetworkDespawn();
    }

    /// <summary>
    /// Server-only: called by a cannonball that hits the kraken. Mirrors
    /// NetworkShipHealth.TakeDamageServer -- a plain server-gated method
    /// rather than an RPC, since the only callers (NetworkCannonball's
    /// trigger handling) already run server-side only.
    /// </summary>
    public bool TakeHitServer(int damage)
    {
        if (!IsSpawned || !IsServer || IsDead || damage <= 0)
        {
            return false;
        }

        int newHealth = Mathf.Max(0, CurrentHealth.Value - Mathf.Max(1, damage));
        CurrentHealth.Value = newHealth;

        Debug.Log($"[Kraken] Hit for {damage}. Health {newHealth}/{MaximumHealth}.");

        if (newHealth <= 0)
        {
            // Every peer's own HandleHealthChanged below already fires
            // Defeated the instant the replicated value hits 0 -- this only
            // needs to remove the object, and only the server may do that.
            NetworkObject.Despawn(true);
        }

        return true;
    }

    private void Update()
    {
        if (sprite == null)
        {
            return;
        }

        sprite.color = Time.time < flashUntil
            ? Color.red
            : baseColor;
    }

    /// <summary>
    /// Runs on every peer (server and clients alike) whenever the
    /// replicated CurrentHealth changes -- this is what actually drives the
    /// visible hit-flash and the Defeated event, so every peer reacts in
    /// step instead of only whichever machine happened to resolve the hit.
    /// </summary>
    private void HandleHealthChanged(int previousValue, int newValue)
    {
        if (newValue < previousValue)
        {
            flashUntil = Time.time + flashSeconds;
        }

        if (previousValue > 0 && newValue <= 0)
        {
            Debug.Log("[Kraken] Defeated.");
            Defeated?.Invoke();
        }
    }
}
