using UnityEngine;

/// <summary>
/// Seamless "treadmill" scroller for a tiled water sprite.
///
/// Put this on a GameObject that has a <see cref="SpriteRenderer"/> whose
/// Draw Mode is set to "Tiled" and whose sprite is a seamless, repeating
/// water tile (e.g. parallax_water_a/b/c/d). The renderer's tiled size
/// should be a little larger than the camera view so no edge is ever shown.
///
/// The sprite is nudged every frame and snapped back by exactly one tile
/// once it has travelled a full tile. Because the tile repeats seamlessly,
/// that snap is invisible and the water appears to scroll forever.
///
/// Stack several of these (one per parallax layer) with different
/// <see cref="scrollSpeed"/> values to get a sense of depth.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class ScrollingWater : MonoBehaviour
{
    [Header("Scrolling")]
    [Tooltip("World units per second the water drifts. " +
             "Negative X scrolls left, positive X scrolls right.")]
    [SerializeField] private Vector2 scrollSpeed = new Vector2(0.4f, 0f);

    [Header("Tiling")]
    [Tooltip("Size of ONE water tile in world units. With a 32px tile " +
             "imported at 32 pixels-per-unit this is 1. The scroll wraps " +
             "over this distance so the loop stays seamless.")]
    [SerializeField] private Vector2 tileSize = new Vector2(1f, 1f);

    [Header("Pixel Snapping")]
    [Tooltip("Snap movement to the pixel grid so pixels stay crisp. " +
             "Matches Camera2DFollow's snapping.")]
    [SerializeField] private bool snapToPixels = true;
    [SerializeField] private float pixelsPerUnit = 32f;

    private Vector3 startPosition;
    private Vector2 scrollOffset;

    private void Awake()
    {
        startPosition = transform.localPosition;
    }

    private void Update()
    {
        scrollOffset += scrollSpeed * Time.deltaTime;

        // Keep the offset inside a single tile so we never drift far from
        // the start position; the wrap is invisible on a seamless tile.
        float x = Mathf.Repeat(scrollOffset.x, Mathf.Max(0.0001f, tileSize.x));
        float y = Mathf.Repeat(scrollOffset.y, Mathf.Max(0.0001f, tileSize.y));

        Vector3 p = startPosition + new Vector3(x, y, 0f);

        if (snapToPixels && pixelsPerUnit > 0f)
        {
            float unitsPerPixel = 1f / pixelsPerUnit;
            p.x = Mathf.Round(p.x / unitsPerPixel) * unitsPerPixel;
            p.y = Mathf.Round(p.y / unitsPerPixel) * unitsPerPixel;
        }

        transform.localPosition = p;
    }
}
