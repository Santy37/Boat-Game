using UnityEngine;

/// <summary>
/// Code-driven melee swing arc so every sword attack has visible motion even
/// before hand-authored attack animations exist. The blade sprite is generated
/// procedurally at runtime, so no asset references are required, and the same
/// visual plays for the local owner and for every remote client.
/// </summary>
public sealed class MeleeSwingVisual : MonoBehaviour
{
    private const float SwingSeconds = 0.16f;
    private const float ArcDegrees = 140f;
    private const float BladeDistance = 0.55f;

    private static Sprite bladeSprite;

    private SpriteRenderer bladeRenderer;
    private Transform pivot;
    private float swingStartTime = float.NegativeInfinity;
    private float baseAngle;

    /// <summary>
    /// Creates (or returns) the swing visual child under the given player.
    /// </summary>
    public static MeleeSwingVisual CreateFor(Transform parent)
    {
        Transform existing = parent.Find("MeleeSwingVisual");
        if (existing != null &&
            existing.TryGetComponent(out MeleeSwingVisual reused))
        {
            return reused;
        }

        GameObject root = new GameObject("MeleeSwingVisual");
        root.transform.SetParent(parent, false);

        MeleeSwingVisual visual = root.AddComponent<MeleeSwingVisual>();
        visual.BuildHierarchy();
        return visual;
    }

    /// <summary>Plays one swing sweeping across the aim direction.</summary>
    public void Play(Vector2 direction)
    {
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = Vector2.down;
        }

        baseAngle =
            Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        swingStartTime = Time.time;
        bladeRenderer.enabled = true;
    }

    private void BuildHierarchy()
    {
        pivot = new GameObject("SwingPivot").transform;
        pivot.SetParent(transform, false);

        GameObject blade = new GameObject("Blade");
        blade.transform.SetParent(pivot, false);
        blade.transform.localPosition =
            new Vector3(BladeDistance, 0f, 0f);
        blade.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);

        bladeRenderer = blade.AddComponent<SpriteRenderer>();
        bladeRenderer.sprite = GetBladeSprite();
        bladeRenderer.sortingOrder = 25;
        bladeRenderer.enabled = false;
    }

    private void Update()
    {
        float elapsed = Time.time - swingStartTime;
        if (elapsed > SwingSeconds)
        {
            if (bladeRenderer.enabled)
            {
                bladeRenderer.enabled = false;
            }

            return;
        }

        float progress = Mathf.Clamp01(elapsed / SwingSeconds);

        // Ease-out sweep from one side of the aim direction to the other.
        float eased = 1f - (1f - progress) * (1f - progress);
        float angle = baseAngle +
            Mathf.Lerp(ArcDegrees * 0.5f, -ArcDegrees * 0.5f, eased);

        pivot.localRotation = Quaternion.Euler(0f, 0f, angle);

        Color color = bladeRenderer.color;
        color.a = 1f - progress * progress;
        bladeRenderer.color = color;
    }

    private static Sprite GetBladeSprite()
    {
        if (bladeSprite != null)
        {
            return bladeSprite;
        }

        const int width = 6;
        const int height = 22;

        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            hideFlags = HideFlags.HideAndDontSave,
        };

        Color steel = new Color(0.92f, 0.95f, 1f, 1f);
        Color edge = new Color(0.55f, 0.62f, 0.78f, 1f);
        Color guard = new Color(0.45f, 0.30f, 0.14f, 1f);
        Color clear = Color.clear;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color pixel;

                if (y < 3)
                {
                    // Hilt and guard at the base.
                    pixel = x >= 1 && x <= width - 2 ? guard : clear;
                }
                else if (y >= height - 3)
                {
                    // Tapered tip.
                    pixel = x >= 2 && x <= 3 ? steel : clear;
                }
                else
                {
                    pixel = x == 0 || x == width - 1 ? edge : steel;
                }

                texture.SetPixel(x, y, pixel);
            }
        }

        texture.Apply();

        bladeSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, width, height),
            new Vector2(0.5f, 0f),
            22f
        );
        bladeSprite.hideFlags = HideFlags.HideAndDontSave;

        return bladeSprite;
    }
}
