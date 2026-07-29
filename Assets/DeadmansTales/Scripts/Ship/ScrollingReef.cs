using DeadmansTales.Ship;
using UnityEngine;

/// <summary>
/// A scrolling rock reef for the kraken arena that is ALWAYS passable. Rocks are
/// organised into vertical "gates" that stream right-to-left; every gate leaves
/// a guaranteed clear gap (>= gapHeight) at a drifting height, so there is always
/// a lane the ship can weave into -- no random wall can ever fully block the
/// band. Gates are spaced wider than the ship so only one is over it at a time.
///
/// Damage uses a small hit-CORE around the ship's centre, not the ship's whole
/// (very tall) hull collider -- otherwise the ship would fill the band and no
/// gap could clear it. Kinematic + guarded, in the spirit of ScrollingWater.
/// </summary>
public class ScrollingReef : MonoBehaviour
{
    [Header("Rocks")]
    [SerializeField] private GameObject[] rockPrefabs;

    [Header("Gates (world units)")]
    [Tooltip("Rocks appear at spawnX and recycle once past despawnX.")]
    [SerializeField] private float spawnX = 60f;
    [SerializeField] private float despawnX = -60f;
    [Tooltip("Horizontal distance between gates. Must be wider than the ship so "
        + "only one gate overlaps it at a time.")]
    [SerializeField] private float gateSpacing = 40f;
    [Tooltip("Vertical band the rocks occupy (a gate fills this minus the gap).")]
    [SerializeField] private float laneMinY = -13f;
    [SerializeField] private float laneMaxY = 13f;
    [Tooltip("Guaranteed clear vertical gap in every gate.")]
    [SerializeField] private float gapHeight = 16f;
    [SerializeField] private int rocksPerGate = 3;
    [SerializeField] private float scrollSpeed = 4f;
    [SerializeField] private int seed = 20260724;

    [Header("Contact damage")]
    [Tooltip("The ship hull collider -- only its CENTRE is used, so the damage "
        + "core can be small enough to leave the gaps passable.")]
    [SerializeField] private Collider2D shipHitbox;
    [SerializeField] private Vector2 hitHalfSize = new Vector2(6f, 3f);
    [SerializeField] private int rockDamage = 5;

    [Tooltip(
        "Played where the rock breaks on the hull. Runs on every peer: the "
        + "reef is deterministic and each client detects its own contact, so "
        + "this needs no RPC.")]
    [SerializeField] private AudioClip rockImpactClip;

    [SerializeField]
    [Range(0f, 1f)]
    private float rockImpactVolume = 1f;

    [Header("Cannon fire")]
    [Tooltip("Let cannonballs break reef rocks. Server-authoritative: only the "
        + "host resolves a hit, then it tells every peer to drop the same rock.")]
    [SerializeField] private bool destructibleByCannons = true;

    [Tooltip("Cannonball hits a rock takes before it breaks.")]
    [SerializeField, Min(1)] private int rockHitsToBreak = 2;

    [Tooltip("Radius of the trigger added to each rock so cannonballs can hit "
        + "it. The rocks carry no collider of their own -- contact damage with "
        + "the ship is done by a position check, not by physics.")]
    [SerializeField, Min(0.1f)] private float rockHitRadius = 1.2f;

    private sealed class Gate
    {
        public float x;
        public Transform[] rocks;
        public bool[] hitConsumed;
        // Bumped every time the gate is laid out again. A break message that
        // arrives after its gate recycled names an older generation and is
        // dropped, so it cannot hide a rock that has just come back whole.
        public int generation;
        public int[] hitsTaken;
    }

    private Gate[] gates;
    private System.Random rng;
    private NetworkShipSinkMeter sinkMeter;

    private void Start()
    {
        if (rockPrefabs == null || rockPrefabs.Length == 0)
        {
            Debug.LogWarning("[ScrollingReef] No rock prefabs; nothing to stream.",
                this);
            enabled = false;
            return;
        }

        rng = new System.Random(seed);

        int count = Mathf.Max(1,
            Mathf.CeilToInt((spawnX - despawnX) / Mathf.Max(1f, gateSpacing)) + 1);
        gates = new Gate[count];
        for (int i = 0; i < count; i++)
        {
            Gate g = new Gate
            {
                x = despawnX + i * gateSpacing,
                rocks = new Transform[Mathf.Max(1, rocksPerGate)],
                hitConsumed = new bool[Mathf.Max(1, rocksPerGate)],
                hitsTaken = new int[Mathf.Max(1, rocksPerGate)],
            };
            for (int r = 0; r < g.rocks.Length; r++)
            {
                GameObject go = Instantiate(
                    rockPrefabs[rng.Next(rockPrefabs.Length)], transform);
                g.rocks[r] = go.transform;

                // Every gate and every rocks[] slot is allocated once here and
                // reused forever -- rocks are repositioned, never respawned --
                // so (gate index, rock index) is a stable identity that every
                // peer agrees on with no networked objects involved. That pair
                // is what a break is broadcast by.
                if (destructibleByCannons)
                {
                    MakeRockShootable(go, i, r);
                }
            }
            LayoutGate(g);
            gates[i] = g;
        }
    }

    private void Update()
    {
        float step = scrollSpeed * Time.deltaTime;

        foreach (Gate g in gates)
        {
            g.x -= step;
            foreach (Transform t in g.rocks)
            {
                if (t == null)
                {
                    continue;
                }
                Vector3 p = t.position;
                p.x -= step;
                t.position = p;
            }
        }

        // Recycle any gate that has scrolled off the left to the right edge.
        float maxX = float.NegativeInfinity;
        foreach (Gate g in gates)
        {
            if (g.x > maxX)
            {
                maxX = g.x;
            }
        }
        foreach (Gate g in gates)
        {
            if (g.x < despawnX)
            {
                g.x = maxX + gateSpacing;
                maxX = g.x;
                LayoutGate(g);
            }
        }

        ApplyContactDamage();
    }

    // Choose a fresh drifting gap and scatter the gate's rocks outside it.
    private void LayoutGate(Gate g)
    {
        // Invalidate any break still in flight for this gate's previous run.
        g.generation++;

        float half = gapHeight * 0.5f;
        float gapLo = laneMinY + half;
        float gapHi = laneMaxY - half;
        float gapCenter = gapHi > gapLo
            ? Mathf.Lerp(gapLo, gapHi, (float)rng.NextDouble())
            : (laneMinY + laneMaxY) * 0.5f;

        float clearLo = gapCenter - half;
        float clearHi = gapCenter + half;

        for (int r = 0; r < g.rocks.Length; r++)
        {
            Transform t = g.rocks[r];
            if (t == null)
            {
                continue;
            }

            // A y outside the clear gap, in the band.
            float y = 0f;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                y = Mathf.Lerp(laneMinY, laneMaxY, (float)rng.NextDouble());
                if (y <= clearLo || y >= clearHi)
                {
                    break;
                }
            }

            float sizeMul = 0.85f + 0.25f * (float)rng.NextDouble();
            float faceX = rng.Next(2) == 0 ? 1f : -1f;
            t.localScale = new Vector3(sizeMul * faceX, sizeMul, 1f);
            t.position = new Vector3(g.x, y, 0f);
            g.hitConsumed[r] = false;
            g.hitsTaken[r] = 0;

            // A rock hidden by a previous impact comes back with its gate.
            if (!t.gameObject.activeSelf)
            {
                t.gameObject.SetActive(true);
            }
        }
    }

    // Gives a pooled rock the bits a cannonball needs to notice it: a trigger
    // to be detected by, and a marker carrying its identity. Done at runtime
    // rather than on the rock prefabs so the arena scene and every rock prefab
    // stay untouched -- they are being edited on other branches.
    private void MakeRockShootable(GameObject rock, int gateIndex, int rockIndex)
    {
        if (rock.GetComponent<Collider2D>() == null)
        {
            CircleCollider2D hit = rock.AddComponent<CircleCollider2D>();
            hit.radius = rockHitRadius;
            hit.isTrigger = true;
        }

        ReefRock marker = rock.GetComponent<ReefRock>();

        if (marker == null)
        {
            marker = rock.AddComponent<ReefRock>();
        }

        marker.Bind(this, gateIndex, rockIndex);
    }

    /// <summary>
    /// Current generation of a gate, so a caller can stamp a break with the
    /// generation it observed.
    /// </summary>
    public int GenerationOf(int gateIndex)
    {
        return gates != null && gateIndex >= 0 && gateIndex < gates.Length
            ? gates[gateIndex].generation
            : -1;
    }

    /// <summary>
    /// Server-only: registers a cannonball hit on a rock. Returns true when
    /// that hit broke it, which is the caller's cue to broadcast the break.
    /// </summary>
    public bool RegisterCannonHitServer(int gateIndex, int rockIndex)
    {
        if (!destructibleByCannons || gates == null ||
            gateIndex < 0 || gateIndex >= gates.Length)
        {
            return false;
        }

        Gate g = gates[gateIndex];

        if (rockIndex < 0 || rockIndex >= g.rocks.Length || g.hitConsumed[rockIndex])
        {
            return false;
        }

        g.hitsTaken[rockIndex]++;

        return g.hitsTaken[rockIndex] >= rockHitsToBreak;
    }

    /// <summary>
    /// Applies a break on THIS peer. Called on the server when it resolves a
    /// hit and on every client from the broadcast, so the same logical rock
    /// disappears everywhere.
    ///
    /// The generation guard is what makes this safe to receive late: gates
    /// recycle constantly, and without it a delayed message would blank a rock
    /// that had already come back around whole.
    /// </summary>
    public void ApplyRockBreak(int gateIndex, int rockIndex, int generation)
    {
        if (gates == null || gateIndex < 0 || gateIndex >= gates.Length)
        {
            return;
        }

        Gate g = gates[gateIndex];

        if (rockIndex < 0 || rockIndex >= g.rocks.Length)
        {
            return;
        }

        if (generation >= 0 && generation != g.generation)
        {
            return;
        }

        Transform t = g.rocks[rockIndex];

        if (t == null || g.hitConsumed[rockIndex])
        {
            return;
        }

        g.hitConsumed[rockIndex] = true;

        if (rockImpactClip != null)
        {
            AudioSource.PlayClipAtPoint(
                rockImpactClip, t.position, rockImpactVolume);
        }

        // Hidden, not destroyed: the gate's rocks are a fixed pool and
        // LayoutGate switches this back on when the gate is reused.
        t.gameObject.SetActive(false);
    }

    /// <summary>
    /// Lazily resolved from shipHitbox rather than a scene-wide search, the
    /// same way KrakenAttack does it: shipHitbox is already wired to the
    /// PLAYER's own hull, and an enemy ship carries its own sink meter.
    /// </summary>
    private NetworkShipSinkMeter ResolveSinkMeter()
    {
        if (sinkMeter == null && shipHitbox != null)
        {
            sinkMeter = shipHitbox.GetComponentInParent<NetworkShipSinkMeter>();
        }

        return sinkMeter;
    }

    private void ApplyContactDamage()
    {
        if (shipHitbox == null)
        {
            return;
        }

        Vector2 c = shipHitbox.bounds.center;
        foreach (Gate g in gates)
        {
            for (int r = 0; r < g.rocks.Length; r++)
            {
                Transform t = g.rocks[r];
                if (t == null || g.hitConsumed[r])
                {
                    continue;
                }
                Vector2 p = t.position;
                if (Mathf.Abs(p.x - c.x) < hitHalfSize.x
                    && Mathf.Abs(p.y - c.y) < hitHalfSize.y)
                {
                    // Was RunContext.Active.DamageShip, gated on
                    // RunContext.HasActive -- that is the LOCAL co-op run
                    // manager, which only exists when the game is entered
                    // through StartScene. Coming through the networked route
                    // it is never active, so the whole method returned early
                    // and the reef did nothing at all: rocks passed straight
                    // through the ship. Damage now goes through the same
                    // server-authoritative sink meter KrakenAttack uses. That
                    // call no-ops on clients by itself, so every peer can run
                    // this and only the server actually applies it.
                    NetworkShipSinkMeter resolved = ResolveSinkMeter();
                    if (resolved != null)
                    {
                        resolved.ApplyCannonHitServer(rockDamage, 1f);
                    }

                    // The rock is consumed by the impact. Marking it hit only
                    // stopped it damaging again -- it stayed sitting on the
                    // deck until its gate recycled. Hiding it reads as the
                    // rock actually breaking on the hull. LayoutGate switches
                    // it back on when the gate is reused.
                    g.hitConsumed[r] = true;

                    if (rockImpactClip != null)
                    {
                        AudioSource.PlayClipAtPoint(
                            rockImpactClip, t.position, rockImpactVolume);
                    }

                    t.gameObject.SetActive(false);
                }
            }
        }
    }
}
