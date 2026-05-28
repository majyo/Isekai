using Godot;

namespace Isekai.World;

public sealed partial class HexOverlayRenderer : Node2D
{
    [Export] public HexMapMode InitialMapMode { get; set; } = HexMapMode.Terrain;

    [Export(PropertyHint.Range, "0.25,8,0.05")]
    public float GridWidth { get; set; } = 1.15f;

    [Export(PropertyHint.Range, "0.25,12,0.05")]
    public float RiverWidth { get; set; } = 2.75f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float GridAlpha { get; set; } = 0.44f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float FillAlpha { get; set; } = 0.18f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float HighlightAlpha { get; set; } = 0.72f;

    [Export] public bool HexOverlayVisibleOnRender { get; set; } = true;

    private TerrainInfoMap _infoMap;
    private HexTileMap _tileMap;
    private WorldMapConfig _config;
    private HexMapMode _currentMapMode;
    private bool _isHexOverlayVisible = true;
    private int _hoveredTileIndex = -1;
    private int _selectedTileIndex = -1;

    public int LastRenderedTileCount { get; private set; }
    public bool HasRenderedGrid => LastRenderedTileCount > 0;
    public bool HasTerrainMapMode => LastRenderedTileCount > 0;
    public bool HasPoliticalMapMode => LastRenderedTileCount > 0;
    public bool HasHighlightMeshes => LastRenderedTileCount > 0;
    public bool IsHexOverlayVisible => _isHexOverlayVisible;

    public void Render(TerrainInfoMap infoMap, HexTileMap tileMap, WorldMapConfig config)
    {
        _infoMap = infoMap;
        _tileMap = tileMap;
        _config = config;
        _currentMapMode = InitialMapMode;
        _isHexOverlayVisible = HexOverlayVisibleOnRender;
        _hoveredTileIndex = -1;
        _selectedTileIndex = -1;
        LastRenderedTileCount = tileMap?.TileCount ?? 0;
        QueueRedraw();
        WorldMapDebugLogger.LogSystem($"Rendered 2D hex overlay for {LastRenderedTileCount} tiles.");
    }

    public void SetMapMode(HexMapMode mapMode)
    {
        _currentMapMode = mapMode;
        QueueRedraw();
    }

    public void SetHexOverlayVisible(bool visible)
    {
        if (_isHexOverlayVisible == visible)
        {
            return;
        }

        _isHexOverlayVisible = visible;
        QueueRedraw();
        WorldMapDebugLogger.LogSystem($"Hex overlay {(visible ? "shown" : "hidden")}.");
    }

    public void ToggleHexOverlayVisible()
    {
        SetHexOverlayVisible(!_isHexOverlayVisible);
    }

    public void SetHoveredTile(int tileIndex)
    {
        _hoveredTileIndex = tileIndex;
        QueueRedraw();
    }

    public void SetSelectedTile(int tileIndex)
    {
        _selectedTileIndex = tileIndex;
        QueueRedraw();
    }

    public void ClearHoveredTile()
    {
        if (_hoveredTileIndex < 0)
        {
            return;
        }

        _hoveredTileIndex = -1;
        QueueRedraw();
    }

    public void ClearSelectedTile()
    {
        if (_selectedTileIndex < 0)
        {
            return;
        }

        _selectedTileIndex = -1;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_isHexOverlayVisible || _tileMap == null || _config == null)
        {
            return;
        }

        DrawMapModeFill();
        DrawRiverEdges();
        DrawGrid();
        DrawHighlight(_hoveredTileIndex, new Color(1.0f, 0.92f, 0.35f, HighlightAlpha));
        DrawHighlight(_selectedTileIndex, new Color(0.30f, 0.82f, 1.0f, HighlightAlpha));
    }

    private void DrawMapModeFill()
    {
        for (var tileIndex = 0; tileIndex < _tileMap.TileCount; tileIndex++)
        {
            var color = _currentMapMode == HexMapMode.Political
                ? GetPoliticalColor(_tileMap.OwnerId[tileIndex])
                : GetTerrainColor((TerrainKind)_tileMap.Terrain[tileIndex]);

            color.A = FillAlpha;
            DrawColoredPolygon(GetHexPolygon(tileIndex), color);
        }
    }

    private void DrawGrid()
    {
        var color = new Color(0.88f, 0.94f, 1.0f, GridAlpha);

        for (var tileIndex = 0; tileIndex < _tileMap.TileCount; tileIndex++)
        {
            var points = GetHexPolygon(tileIndex);
            DrawPolyline([.. points, points[0]], color, GridWidth, true);
        }
    }

    private void DrawRiverEdges()
    {
        for (var tileIndex = 0; tileIndex < _tileMap.TileCount; tileIndex++)
        {
            var axial = new Vector2I(_tileMap.Q[tileIndex], _tileMap.R[tileIndex]);

            for (var direction = 0; direction < WorldMapCoordinateUtility.AxialNeighborDirections.Length; direction++)
            {
                var edge = _tileMap.GetRiverEdge(tileIndex, direction);

                if (edge.Kind == RiverKind.None)
                {
                    continue;
                }

                var neighborAxial = axial + WorldMapCoordinateUtility.AxialNeighborDirections[direction];

                if (_tileMap.TryGetTileIndex(neighborAxial, out var neighborIndex) && neighborIndex < tileIndex)
                {
                    continue;
                }

                var points = GetHexPolygon(tileIndex);
                var startCorner = GetRiverEdgeStartCorner(direction);
                var endCorner = (startCorner + 1) % 6;
                var color = edge.Kind == RiverKind.Major
                    ? new Color(0.16f, 0.52f, 0.88f, 0.88f)
                    : new Color(0.18f, 0.58f, 0.82f, 0.74f);
                var width = edge.Kind == RiverKind.Major ? RiverWidth * 1.35f : RiverWidth;

                DrawLine(points[startCorner], points[endCorner], color, width, true);
            }
        }
    }

    private void DrawHighlight(int tileIndex, Color color)
    {
        if (tileIndex < 0 || tileIndex >= _tileMap.TileCount)
        {
            return;
        }

        var polygon = GetHexPolygon(tileIndex);
        DrawColoredPolygon(polygon, color);
        DrawPolyline([.. polygon, polygon[0]], color.Lightened(0.22f), GridWidth * 2.0f, true);
    }

    private Vector2[] GetHexPolygon(int tileIndex)
    {
        var center = new Vector2(_tileMap.WorldCenterX[tileIndex], _tileMap.WorldCenterZ[tileIndex]);
        var points = new Vector2[6];

        for (var corner = 0; corner < points.Length; corner++)
        {
            points[corner] = WorldMapCoordinateUtility.GetHexCornerWorldXz(center, _config.HexRadius, corner);
        }

        return points;
    }

    private static int GetRiverEdgeStartCorner(int direction)
    {
        return (direction + 5) % 6;
    }

    private static Color GetTerrainColor(TerrainKind terrain)
    {
        return terrain switch
        {
            TerrainKind.Ocean => new Color(0.05f, 0.20f, 0.42f),
            TerrainKind.Coast => new Color(0.86f, 0.76f, 0.44f),
            TerrainKind.Plains => new Color(0.32f, 0.58f, 0.22f),
            TerrainKind.Forest => new Color(0.08f, 0.34f, 0.16f),
            TerrainKind.Desert => new Color(0.78f, 0.60f, 0.28f),
            TerrainKind.Tundra => new Color(0.66f, 0.74f, 0.72f),
            TerrainKind.Hills => new Color(0.44f, 0.40f, 0.26f),
            TerrainKind.Mountains => new Color(0.52f, 0.51f, 0.50f),
            _ => Colors.Magenta
        };
    }

    private static Color GetPoliticalColor(int ownerId)
    {
        if (ownerId <= 0)
        {
            return new Color(0.12f, 0.16f, 0.22f);
        }

        var palette = new[]
        {
            new Color(0.86f, 0.24f, 0.20f),
            new Color(0.16f, 0.48f, 0.86f),
            new Color(0.20f, 0.64f, 0.34f),
            new Color(0.82f, 0.58f, 0.16f),
            new Color(0.58f, 0.30f, 0.78f),
            new Color(0.12f, 0.68f, 0.68f)
        };

        return palette[(ownerId - 1) % palette.Length];
    }
}
