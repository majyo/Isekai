using System;
using Godot;

namespace Isekai.World;

public static class TerrainInfoMapDebugExporter
{
    public const string DefaultOutputDirectory = "res://world/generated";

    public static void ExportAll(TerrainInfoMap infoMap, string outputDirectory = DefaultOutputDirectory)
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(outputDirectory));

        SaveHeightMap(infoMap, $"{outputDirectory}/height_map_debug.png");
        SaveLandMask(infoMap, $"{outputDirectory}/land_mask_debug.png");
        SaveScalarMap(infoMap.Size, infoMap.MoistureMap, $"{outputDirectory}/moisture_map_debug.png", new Color(0.08f, 0.10f, 0.12f), new Color(0.20f, 0.55f, 1.00f));
        SaveScalarMap(infoMap.Size, infoMap.TemperatureMap, $"{outputDirectory}/temperature_map_debug.png", new Color(0.10f, 0.18f, 0.70f), new Color(1.00f, 0.62f, 0.08f));
        SaveBiomeMap(infoMap, $"{outputDirectory}/biome_map_debug.png");
        SaveScalarMap(infoMap.Size, infoMap.RiverFlowMap, $"{outputDirectory}/river_flow_map_debug.png", new Color(0.02f, 0.03f, 0.04f), new Color(0.20f, 0.65f, 1.00f));
    }

    private static void SaveHeightMap(TerrainInfoMap infoMap, string path)
    {
        var image = Image.CreateEmpty(infoMap.Size.X, infoMap.Size.Y, false, Image.Format.Rgb8);

        for (var y = 0; y < infoMap.Size.Y; y++)
        {
            for (var x = 0; x < infoMap.Size.X; x++)
            {
                var height = infoMap.HeightMap[infoMap.GetIndex(x, y)];
                var normalized = Mathf.InverseLerp(-320.0f, infoMap.MaxHeight, height);
                image.SetPixel(x, y, new Color(normalized, normalized, normalized));
            }
        }

        SaveImage(image, path);
    }

    private static void SaveLandMask(TerrainInfoMap infoMap, string path)
    {
        var image = Image.CreateEmpty(infoMap.Size.X, infoMap.Size.Y, false, Image.Format.Rgb8);

        for (var y = 0; y < infoMap.Size.Y; y++)
        {
            for (var x = 0; x < infoMap.Size.X; x++)
            {
                var index = infoMap.GetIndex(x, y);
                image.SetPixel(x, y, infoMap.LandMask[index] == 0 ? new Color(0.05f, 0.16f, 0.34f) : new Color(0.80f, 0.84f, 0.72f));
            }
        }

        SaveImage(image, path);
    }

    private static void SaveScalarMap(Vector2I size, float[] values, string path, Color low, Color high)
    {
        var image = Image.CreateEmpty(size.X, size.Y, false, Image.Format.Rgb8);

        for (var y = 0; y < size.Y; y++)
        {
            for (var x = 0; x < size.X; x++)
            {
                var index = y * size.X + x;
                var value = Math.Clamp(values[index], 0.0f, 1.0f);
                image.SetPixel(x, y, low.Lerp(high, value));
            }
        }

        SaveImage(image, path);
    }

    private static void SaveBiomeMap(TerrainInfoMap infoMap, string path)
    {
        var image = Image.CreateEmpty(infoMap.Size.X, infoMap.Size.Y, false, Image.Format.Rgb8);

        for (var y = 0; y < infoMap.Size.Y; y++)
        {
            for (var x = 0; x < infoMap.Size.X; x++)
            {
                var biome = (BiomeKind)infoMap.BiomeMap[infoMap.GetIndex(x, y)];
                image.SetPixel(x, y, GetBiomeColor(biome));
            }
        }

        SaveImage(image, path);
    }

    private static Color GetBiomeColor(BiomeKind biome)
    {
        return biome switch
        {
            BiomeKind.Ocean => new Color(0.04f, 0.19f, 0.42f),
            BiomeKind.Coast => new Color(0.86f, 0.78f, 0.52f),
            BiomeKind.Grassland => new Color(0.34f, 0.58f, 0.22f),
            BiomeKind.Forest => new Color(0.11f, 0.32f, 0.15f),
            BiomeKind.Desert => new Color(0.78f, 0.61f, 0.29f),
            BiomeKind.Tundra => new Color(0.70f, 0.76f, 0.74f),
            BiomeKind.Hills => new Color(0.46f, 0.44f, 0.28f),
            BiomeKind.Mountain => new Color(0.50f, 0.50f, 0.52f),
            _ => Colors.Magenta
        };
    }

    private static void SaveImage(Image image, string path)
    {
        var error = image.SavePng(path);

        if (error != Error.Ok)
        {
            WorldMapDebugLogger.Warn($"Failed to save debug image '{path}': {error}");
        }
    }
}
