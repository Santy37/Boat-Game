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

    private sealed class Gate
    {
        public float x;
        public Transform[] rocks;
        public bool[] hitConsumed;
    }

    private Gate[] gates;
    private System.Random rng;

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
            };
            for (int r = 0; r < g.rocks.Length; r++)
            {
                GameObject go = Instantiate(
                    rockPrefabs[rng.Next(rockPrefabs.Length)], transform);
                g.rocks[r] = go.transform;
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
        }
    }

    private void ApplyContactDamage()
    {
        if (shipHitbox == null || !RunContext.HasActive)
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
                    RunContext.Active.DamageShip(rockDamage);
                    g.hitConsumed[r] = true;
                }
            }
        }
    }
}
