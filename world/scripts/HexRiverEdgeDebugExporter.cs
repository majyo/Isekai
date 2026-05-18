using Godot;

namespace Isekai.World;

public static class HexRiverEdgeDebugExporter
{
    public const string DefaultDebugPath = "res://world/generated/hex_river_edges_debug.png";

    public static void ExportRiverEdgeMap(HexTileMap tileMap, string path = DefaultDebugPath)
    {
        var image = Image.CreateEmpty(tileMap.GridSize.X, tileMap.GridSize.Y, false, Image.Format.Rgb8);

        for (var y = 0; y < tileMap.GridSize.Y; y++)
        {
            for (var x = 0; x < tileMap.GridSize.X; x++)
            {
                var tileIndex = tileMap.GetIndexUnchecked(x, y);
                image.SetPixel(x, y, GetTileRiverColor(tileMap, tileIndex));
            }
        }

        var error = image.SavePng(path);

        if (error != Error.Ok)
        {
            WorldMapDebugLogger.Warn($"Failed to save hex river edge debug image '{path}': {error}");
        }
    }

    private static Color GetTileRiverColor(HexTileMap tileMap, int tileIndex)
    {
        var maxFlow = 0.0f;
        var hasMajorRiver = false;
        var hasSmallRiver = false;

        for (var direction = 0; direction < WorldMapCoordinateUtility.AxialNeighborDirections.Length; direction++)
        {
            var edge = tileMap.GetRiverEdge(tileIndex, direction);

            if (edge.Kind == RiverKind.None)
            {
                continue;
            }

            maxFlow = Mathf.Max(maxFlow, edge.Flow);
            hasMajorRiver |= edge.Kind == RiverKind.Major;
            hasSmallRiver |= edge.Kind == RiverKind.Small;
        }

        if (hasMajorRiver)
        {
            return new Color(0.12f, 0.82f, 1.0f).Lerp(Colors.White, Mathf.Clamp(maxFlow, 0.0f, 1.0f) * 0.35f);
        }

        if (hasSmallRiver)
        {
            return new Color(0.08f, 0.36f, 0.72f).Lerp(new Color(0.2f, 0.7f, 1.0f), Mathf.Clamp(maxFlow, 0.0f, 1.0f));
        }

        return tileMap.IsWater[tileIndex] != 0
            ? new Color(0.03f, 0.09f, 0.18f)
            : new Color(0.05f, 0.07f, 0.06f);
    }
}
