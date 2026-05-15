using Godot;

namespace Isekai.World;

public static class HexTileDebugExporter
{
    public const string DefaultTerrainPath = "res://world/generated/hex_tiles_terrain_debug.png";

    public static void ExportTerrainMap(HexTileMap tileMap, string path = DefaultTerrainPath)
    {
        var image = Image.CreateEmpty(tileMap.GridSize.X, tileMap.GridSize.Y, false, Image.Format.Rgb8);

        for (var y = 0; y < tileMap.GridSize.Y; y++)
        {
            for (var x = 0; x < tileMap.GridSize.X; x++)
            {
                var index = tileMap.GetIndexUnchecked(x, y);
                image.SetPixel(x, y, GetTerrainColor((TerrainKind)tileMap.Terrain[index], tileMap.IsCoastal[index] != 0));
            }
        }

        var error = image.SavePng(path);

        if (error != Error.Ok)
        {
            WorldMapDebugLogger.Warn($"Failed to save hex tile terrain debug image '{path}': {error}");
        }
    }

    private static Color GetTerrainColor(TerrainKind terrain, bool isCoastal)
    {
        if (isCoastal && terrain == TerrainKind.Ocean)
        {
            return new Color(0.12f, 0.42f, 0.70f);
        }

        return terrain switch
        {
            TerrainKind.Ocean => new Color(0.04f, 0.18f, 0.40f),
            TerrainKind.Coast => new Color(0.84f, 0.74f, 0.44f),
            TerrainKind.Plains => new Color(0.34f, 0.58f, 0.22f),
            TerrainKind.Forest => new Color(0.10f, 0.31f, 0.15f),
            TerrainKind.Desert => new Color(0.76f, 0.59f, 0.28f),
            TerrainKind.Tundra => new Color(0.68f, 0.74f, 0.72f),
            TerrainKind.Hills => new Color(0.45f, 0.42f, 0.27f),
            TerrainKind.Mountains => new Color(0.50f, 0.49f, 0.48f),
            _ => Colors.Magenta
        };
    }
}
