using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Cuts finished market props out of the raw RPG-Maker sheets in
/// Art_Pixel/Market and writes them as single sprites.
///
/// The sheets store a stall as separate awning and counter pieces spread
/// across a 16px grid, which is convenient for a tile palette and useless
/// for placing "one market stall" in a scene. Compositing each prop once,
/// here, means the island builder places one GameObject per stall instead
/// of stitching five tiles together every run and hoping they line up.
///
/// Everything is imported at 32 pixels-per-unit: these sheets are drawn
/// for ~32px tall characters, and the game's player is ~1 world unit, so
/// two source cells to a unit keeps props in proportion with the crew.
/// </summary>
public static class MarketArtBuilder
{
    private const string MenuPath = "Deadman's Tales/Build Market Art";

    private const string MarketFolder =
        "Assets/DeadmansTales/Art_Pixel/Market";

    private const string StallsSourcePath =
        MarketFolder + "/market_stalls_src.png";
    private const string ForgeSourcePath =
        MarketFolder + "/forge_src.png";
    private const string MeatrackSourcePath =
        MarketFolder + "/meatrack_src.png";
    private const string BreakablesSourcePath =
        MarketFolder + "/breakables_src.png";

    /// <summary>
    /// The player renders motw_10 at 32 px/unit, and that character is 68
    /// drawn pixels tall — so the crew stand about 2.1 world units, NOT
    /// the ~1 unit the prefab's placeholder sword icon suggests. These
    /// market sheets are drawn for 32px characters, so they must be
    /// enlarged by roughly 68/32 to sit at the same visual scale.
    ///
    /// 16 px/unit does that and matches the rest of the world: the beach
    /// terrain is 32px cells at 32 px/unit and the openRPG tiles are 16px
    /// cells at 16 px/unit, so on every sheet in the game one source cell
    /// is exactly one world unit. A stall is then 3x4 units beside a 2.1
    /// unit player — counter at chest height, awning above head height.
    /// </summary>
    public const int MarketPixelsPerUnit = 16;

    private const int Cell = 16;

    /// <summary>
    /// One piece cut from a sheet, and where it belongs in the finished
    /// prop. Source and destination are tracked separately on purpose: a
    /// stall's awning and counter sit in different corners of the sheet
    /// but must end up stacked, so their layout cannot be inferred from
    /// where the artist happened to store them.
    /// </summary>
    private readonly struct PropPart
    {
        public PropPart(RectInt source, Vector2Int destination)
        {
            Source = source;
            Destination = destination;
        }

        /// <summary>Cell rect in sheet space, rows counted from the top.</summary>
        public RectInt Source { get; }

        /// <summary>Top-left cell of this piece within the finished prop.</summary>
        public Vector2Int Destination { get; }
    }

    private sealed class PropRecipe
    {
        public PropRecipe(
            string name,
            string sourcePath,
            params PropPart[] parts
        )
        {
            Name = name;
            SourcePath = sourcePath;
            Parts = parts;
        }

        public string Name { get; }
        public string SourcePath { get; }
        public PropPart[] Parts { get; }
    }

    private static PropPart Part(
        int sourceColumn,
        int sourceRow,
        int columns,
        int rows,
        int destinationColumn = 0,
        int destinationRow = 0
    )
    {
        return new PropPart(
            new RectInt(sourceColumn, sourceRow, columns, rows),
            new Vector2Int(destinationColumn, destinationRow)
        );
    }

    private static readonly PropRecipe[] Recipes =
    {
        // Stalls: rows 3-6 of the sheet are COMPLETE stalls — the artist
        // already drew awning, posts and counter together as one 3x4 cell
        // unit, aligned and lit consistently.
        //
        // These were previously welded together from a row-0 awning and an
        // unrelated counter strip. That put a transparent seam between the
        // two halves (60px of art in an 80px canvas) and, with the
        // bottom-centre pivot, floated the whole stall above the ground.
        // Take the artist's version instead of inventing one.
        new PropRecipe(
            "stall_red",
            StallsSourcePath,
            Part(0, 3, 3, 4)
        ),
        new PropRecipe(
            "stall_blue",
            StallsSourcePath,
            Part(3, 3, 3, 4)
        ),
        new PropRecipe(
            "stall_green",
            StallsSourcePath,
            Part(6, 3, 3, 4)
        ),
        new PropRecipe(
            "stall_counter",
            StallsSourcePath,
            Part(3, 7, 3, 2)
        ),
        new PropRecipe(
            "market_sign",
            StallsSourcePath,
            Part(1, 7, 1, 2)
        ),

        new PropRecipe(
            "forge_stone",
            ForgeSourcePath,
            Part(0, 0, 3, 4)
        ),
        new PropRecipe(
            "forge_copper",
            ForgeSourcePath,
            Part(3, 0, 3, 4)
        ),

        new PropRecipe(
            "meatrack",
            MeatrackSourcePath,
            Part(0, 0, 5, 2)
        ),
        new PropRecipe(
            "meatrack_tall",
            MeatrackSourcePath,
            Part(0, 2, 5, 2)
        ),

        new PropRecipe(
            "crate",
            BreakablesSourcePath,
            Part(1, 0, 1, 2)
        ),
        new PropRecipe(
            "barrel",
            BreakablesSourcePath,
            Part(4, 0, 1, 2)
        ),
        new PropRecipe(
            "pot",
            BreakablesSourcePath,
            Part(7, 0, 1, 2)
        ),
        new PropRecipe(
            "vase",
            BreakablesSourcePath,
            Part(10, 0, 1, 2)
        ),
    };

    [MenuItem(MenuPath)]
    public static void BuildAll()
    {
        foreach (PropRecipe recipe in Recipes)
        {
            BuildProp(recipe);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            $"[Market Art] Composited {Recipes.Length} market props."
        );
    }

    public static void BuildAllFromCommandLine()
    {
        BuildAll();
    }

    public static string PropPath(string propName)
    {
        return $"{MarketFolder}/{propName}.png";
    }

    private static void BuildProp(PropRecipe recipe)
    {
        Texture2D sheet = LoadSourceTexture(recipe.SourcePath);

        try
        {
            // Output size comes from where the parts are PLACED, not from
            // where they were cut.
            int columns = 0;
            int rows = 0;

            foreach (PropPart part in recipe.Parts)
            {
                columns = Mathf.Max(
                    columns,
                    part.Destination.x + part.Source.width
                );
                rows = Mathf.Max(
                    rows,
                    part.Destination.y + part.Source.height
                );
            }

            int width = columns * Cell;
            int height = rows * Cell;

            Color32[] output = new Color32[width * height];

            foreach (PropPart part in recipe.Parts)
            {
                CopyPart(sheet, part, width, height, output);
            }

            TrimToInk(ref output, ref width, ref height);

            WriteTexture(recipe.Name, width, height, output);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(sheet);
        }

        ImportProp(recipe.Name);
    }

    /// <summary>
    /// Crops the finished prop down to the pixels actually drawn.
    ///
    /// These sheets pad their art inside the cell grid — a stall block is
    /// 48x64 but the stall itself only occupies rows 4..55, and a pot uses
    /// 15 rows of a 32 row cell. Every prop is imported with a bottom-centre
    /// pivot, so that padding is not cosmetic: it lifts the prop off the
    /// ground by exactly as many pixels as the sheet left empty, which is
    /// half a world unit for a stall and a full unit for a pot. Trimming to
    /// the ink makes the pivot the thing's actual base.
    /// </summary>
    private static void TrimToInk(
        ref Color32[] pixels,
        ref int width,
        ref int height
    )
    {
        int minX = width;
        int maxX = -1;
        int minY = height;
        int maxY = -1;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (pixels[y * width + x].a <= 4)
                {
                    continue;
                }

                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }
        }

        if (maxX < 0)
        {
            throw new InvalidOperationException(
                "A market prop composited to nothing but transparency; " +
                "its source cells are probably wrong."
            );
        }

        int trimmedWidth = maxX - minX + 1;
        int trimmedHeight = maxY - minY + 1;

        if (trimmedWidth == width && trimmedHeight == height)
        {
            return;
        }

        Color32[] trimmed = new Color32[trimmedWidth * trimmedHeight];

        for (int y = 0; y < trimmedHeight; y++)
        {
            for (int x = 0; x < trimmedWidth; x++)
            {
                trimmed[y * trimmedWidth + x] =
                    pixels[(minY + y) * width + (minX + x)];
            }
        }

        pixels = trimmed;
        width = trimmedWidth;
        height = trimmedHeight;
    }

    /// <summary>
    /// Copies one cell rect into the output buffer. Sheet rows are counted
    /// from the top (how the art reads), while Unity textures are stored
    /// bottom-up, so both source and destination rows are flipped here.
    /// </summary>
    private static void CopyPart(
        Texture2D sheet,
        PropPart part,
        int width,
        int height,
        Color32[] output
    )
    {
        Color32[] sheetPixels = sheet.GetPixels32();

        int destinationLeft = part.Destination.x * Cell;
        int destinationTop = part.Destination.y * Cell;

        for (int y = 0; y < part.Source.height * Cell; y++)
        {
            int sourceRowFromTop = part.Source.yMin * Cell + y;
            int sourceY = sheet.height - 1 - sourceRowFromTop;

            if (sourceY < 0 || sourceY >= sheet.height)
            {
                continue;
            }

            int destinationRowFromTop = destinationTop + y;
            int destinationY = height - 1 - destinationRowFromTop;

            if (destinationY < 0 || destinationY >= height)
            {
                continue;
            }

            for (int x = 0; x < part.Source.width * Cell; x++)
            {
                int sourceX = part.Source.xMin * Cell + x;

                if (sourceX < 0 || sourceX >= sheet.width)
                {
                    continue;
                }

                Color32 pixel =
                    sheetPixels[sourceY * sheet.width + sourceX];

                if (pixel.a <= 4)
                {
                    continue;
                }

                output[destinationY * width + destinationLeft + x] = pixel;
            }
        }
    }

    private static Texture2D LoadSourceTexture(string assetPath)
    {
        string fullPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            assetPath.Replace('/', Path.DirectorySeparatorChar)
        );

        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException(
                $"Market source art is missing: {assetPath}"
            );
        }

        Texture2D texture =
            new Texture2D(2, 2, TextureFormat.RGBA32, false);

        // Read the file directly so the sheet's own import settings
        // (compression, read/write) cannot corrupt the crop.
        texture.LoadImage(File.ReadAllBytes(fullPath));
        return texture;
    }

    private static void WriteTexture(
        string propName,
        int width,
        int height,
        Color32[] pixels
    )
    {
        Texture2D texture =
            new Texture2D(width, height, TextureFormat.RGBA32, false);

        try
        {
            texture.SetPixels32(pixels);
            texture.Apply();

            File.WriteAllBytes(
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    PropPath(propName)
                        .Replace('/', Path.DirectorySeparatorChar)
                ),
                texture.EncodeToPNG()
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static void ImportProp(string propName)
    {
        string path = PropPath(propName);
        AssetDatabase.ImportAsset(path);

        TextureImporter importer =
            (TextureImporter)AssetImporter.GetAtPath(path);

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = MarketPixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression =
            TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;

        // Bottom-centre pivot: props are positioned by where they stand on
        // the ground, matching the feet-at-origin convention the character
        // art already uses.
        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.BottomCenter;
        importer.SetTextureSettings(settings);

        importer.SaveAndReimport();
    }
}
