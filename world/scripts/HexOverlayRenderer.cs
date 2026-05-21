using Godot;

namespace Isekai.World;

public sealed partial class HexOverlayRenderer : Node3D
{
    private const string GridNodeName = "hex_grid_lines";
    private const string TerrainModeNodeName = "terrain_map_mode_mesh";
    private const string PoliticalModeNodeName = "political_map_mode_mesh";
    private const string HoverHighlightNodeName = "hover_highlight";
    private const string SelectionHighlightNodeName = "selection_highlight";

    [Export] public NodePath MapModeOverlayPath { get; set; } = "../map_mode_overlay";
    [Export] public HexMapMode InitialMapMode { get; set; } = HexMapMode.Terrain;

    [Export(PropertyHint.Range, "0,16,0.05")]
    public float OverlayHeightOffset { get; set; } = 1.25f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float GridAlpha { get; set; } = 0.42f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float FillAlpha { get; set; } = 0.24f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float HighlightAlpha { get; set; } = 0.72f;

    [Export] public bool HexOverlayVisibleOnRender { get; set; } = true;

    private TerrainInfoMap _infoMap;
    private HexTileMap _tileMap;
    private WorldMapConfig _config;
    private Node3D _mapModeRoot;
    private MeshInstance3D _gridMeshInstance;
    private MeshInstance3D _terrainModeMeshInstance;
    private MeshInstance3D _politicalModeMeshInstance;
    private MeshInstance3D _hoverHighlightMeshInstance;
    private MeshInstance3D _selectionHighlightMeshInstance;
    private HexMapMode _currentMapMode;
    private bool _isHexOverlayVisible = true;

    public int LastRenderedTileCount { get; private set; }
    public bool HasRenderedGrid => _gridMeshInstance?.Mesh != null;
    public bool HasTerrainMapMode => _terrainModeMeshInstance?.Mesh != null;
    public bool HasPoliticalMapMode => _politicalModeMeshInstance?.Mesh != null;
    public bool HasHighlightMeshes => _hoverHighlightMeshInstance != null && _selectionHighlightMeshInstance != null;
    public bool IsHexOverlayVisible => _isHexOverlayVisible;

    public void Render(TerrainInfoMap infoMap, HexTileMap tileMap, WorldMapConfig config)
    {
        _infoMap = infoMap;
        _tileMap = tileMap;
        _config = config;
        _mapModeRoot = GetNodeOrNull<Node3D>(MapModeOverlayPath);
        _currentMapMode = InitialMapMode;
        _isHexOverlayVisible = HexOverlayVisibleOnRender;
        LastRenderedTileCount = 0;
        _gridMeshInstance = null;
        _terrainModeMeshInstance = null;
        _politicalModeMeshInstance = null;
        _hoverHighlightMeshInstance = null;
        _selectionHighlightMeshInstance = null;

        ClearChildren(this);

        if (_mapModeRoot != null)
        {
            ClearChildren(_mapModeRoot);
        }

        _gridMeshInstance = BuildGridMeshInstance();
        AddChild(_gridMeshInstance);

        _hoverHighlightMeshInstance = BuildHighlightMeshInstance(HoverHighlightNodeName, new Color(1.0f, 0.92f, 0.35f, HighlightAlpha));
        _selectionHighlightMeshInstance = BuildHighlightMeshInstance(SelectionHighlightNodeName, new Color(0.30f, 0.82f, 1.0f, HighlightAlpha));
        AddChild(_hoverHighlightMeshInstance);
        AddChild(_selectionHighlightMeshInstance);

        if (_mapModeRoot != null)
        {
            _terrainModeMeshInstance = BuildMapModeMeshInstance(TerrainModeNodeName, HexMapMode.Terrain);
            _politicalModeMeshInstance = BuildMapModeMeshInstance(PoliticalModeNodeName, HexMapMode.Political);
            _mapModeRoot.AddChild(_terrainModeMeshInstance);
            _mapModeRoot.AddChild(_politicalModeMeshInstance);
            SetMapMode(InitialMapMode);
        }
        else
        {
            ApplyHexOverlayVisibility();
        }

        LastRenderedTileCount = tileMap.TileCount;
        WorldMapDebugLogger.LogSystem($"Rendered hex overlay for {tileMap.TileCount} tiles.");
    }

    public void SetMapMode(HexMapMode mapMode)
    {
        _currentMapMode = mapMode;
        ApplyHexOverlayVisibility();
    }

    public void SetHexOverlayVisible(bool visible)
    {
        if (_isHexOverlayVisible == visible)
        {
            return;
        }

        _isHexOverlayVisible = visible;
        ApplyHexOverlayVisibility();
        WorldMapDebugLogger.LogSystem($"Hex overlay {(visible ? "shown" : "hidden")}.");
    }

    public void ToggleHexOverlayVisible()
    {
        SetHexOverlayVisible(!_isHexOverlayVisible);
    }

    private void ApplyHexOverlayVisibility()
    {
        if (_gridMeshInstance != null)
        {
            _gridMeshInstance.Visible = _isHexOverlayVisible;
        }

        if (_terrainModeMeshInstance != null)
        {
            _terrainModeMeshInstance.Visible = _isHexOverlayVisible && _currentMapMode == HexMapMode.Terrain;
        }

        if (_politicalModeMeshInstance != null)
        {
            _politicalModeMeshInstance.Visible = _isHexOverlayVisible && _currentMapMode == HexMapMode.Political;
        }

        if (_hoverHighlightMeshInstance != null)
        {
            _hoverHighlightMeshInstance.Visible = _isHexOverlayVisible && _hoverHighlightMeshInstance.Mesh != null;
        }

        if (_selectionHighlightMeshInstance != null)
        {
            _selectionHighlightMeshInstance.Visible = _isHexOverlayVisible && _selectionHighlightMeshInstance.Mesh != null;
        }
    }

    public void SetHoveredTile(int tileIndex)
    {
        SetHighlightTile(_hoverHighlightMeshInstance, tileIndex, new Color(1.0f, 0.92f, 0.35f, HighlightAlpha), OverlayHeightOffset + 2.0f);
    }

    public void SetSelectedTile(int tileIndex)
    {
        SetHighlightTile(_selectionHighlightMeshInstance, tileIndex, new Color(0.30f, 0.82f, 1.0f, HighlightAlpha), OverlayHeightOffset + 2.5f);
    }

    public void ClearHoveredTile()
    {
        SetHoveredTile(-1);
    }

    public void ClearSelectedTile()
    {
        SetSelectedTile(-1);
    }

    private MeshInstance3D BuildGridMeshInstance()
    {
        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Lines);

        var color = new Color(0.88f, 0.94f, 1.0f, GridAlpha);

        for (var tileIndex = 0; tileIndex < _tileMap.TileCount; tileIndex++)
        {
            for (var corner = 0; corner < 6; corner++)
            {
                surfaceTool.SetColor(color);
                surfaceTool.AddVertex(GetCornerPosition(tileIndex, corner, OverlayHeightOffset));
                surfaceTool.SetColor(color);
                surfaceTool.AddVertex(GetCornerPosition(tileIndex, (corner + 1) % 6, OverlayHeightOffset));
            }
        }

        return new MeshInstance3D
        {
            Name = GridNodeName,
            Mesh = surfaceTool.Commit(),
            MaterialOverride = BuildOverlayMaterial()
        };
    }

    private MeshInstance3D BuildMapModeMeshInstance(string nodeName, HexMapMode mapMode)
    {
        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        for (var tileIndex = 0; tileIndex < _tileMap.TileCount; tileIndex++)
        {
            var color = mapMode == HexMapMode.Political
                ? GetPoliticalColor(_tileMap.OwnerId[tileIndex])
                : GetTerrainColor((TerrainKind)_tileMap.Terrain[tileIndex]);

            color.A = FillAlpha;

            for (var corner = 0; corner < 6; corner++)
            {
                surfaceTool.SetColor(color);
                surfaceTool.AddVertex(GetTileCenterPosition(tileIndex, OverlayHeightOffset + 0.25f));
                surfaceTool.SetColor(color);
                surfaceTool.AddVertex(GetCornerPosition(tileIndex, corner, OverlayHeightOffset + 0.25f));
                surfaceTool.SetColor(color);
                surfaceTool.AddVertex(GetCornerPosition(tileIndex, (corner + 1) % 6, OverlayHeightOffset + 0.25f));
            }
        }

        surfaceTool.GenerateNormals();

        return new MeshInstance3D
        {
            Name = nodeName,
            Mesh = surfaceTool.Commit(),
            MaterialOverride = BuildOverlayMaterial()
        };
    }

    private MeshInstance3D BuildHighlightMeshInstance(string nodeName, Color color)
    {
        return new MeshInstance3D
        {
            Name = nodeName,
            Visible = false,
            MaterialOverride = BuildHighlightMaterial(color)
        };
    }

    private void SetHighlightTile(MeshInstance3D meshInstance, int tileIndex, Color color, float heightOffset)
    {
        if (meshInstance == null || _tileMap == null || tileIndex < 0 || tileIndex >= _tileMap.TileCount)
        {
            if (meshInstance != null)
            {
                meshInstance.Mesh = null;
                meshInstance.Visible = false;
            }

            return;
        }

        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        for (var corner = 0; corner < 6; corner++)
        {
            surfaceTool.SetColor(color);
            surfaceTool.AddVertex(GetTileCenterPosition(tileIndex, heightOffset));
            surfaceTool.SetColor(color);
            surfaceTool.AddVertex(GetCornerPosition(tileIndex, corner, heightOffset));
            surfaceTool.SetColor(color);
            surfaceTool.AddVertex(GetCornerPosition(tileIndex, (corner + 1) % 6, heightOffset));
        }

        surfaceTool.GenerateNormals();
        meshInstance.Mesh = surfaceTool.Commit();
        meshInstance.MaterialOverride = BuildHighlightMaterial(color);
        meshInstance.Visible = _isHexOverlayVisible;
    }

    private Vector3 GetTileCenterPosition(int tileIndex, float heightOffset)
    {
        var worldXz = new Vector2(_tileMap.WorldCenterX[tileIndex], _tileMap.WorldCenterZ[tileIndex]);
        var height = _infoMap.SampleHeightUv(WorldMapCoordinateUtility.WorldXzToUv(worldXz, _config));
        return WorldMapCoordinateUtility.WorldXzToWorldPosition(worldXz, height + heightOffset);
    }

    private Vector3 GetCornerPosition(int tileIndex, int cornerIndex, float heightOffset)
    {
        var centerWorldXz = new Vector2(_tileMap.WorldCenterX[tileIndex], _tileMap.WorldCenterZ[tileIndex]);
        var cornerWorldXz = WorldMapCoordinateUtility.GetHexCornerWorldXz(centerWorldXz, _config.HexRadius, cornerIndex);
        var height = _infoMap.SampleHeightUv(WorldMapCoordinateUtility.WorldXzToUv(cornerWorldXz, _config));
        return WorldMapCoordinateUtility.WorldXzToWorldPosition(cornerWorldXz, height + heightOffset);
    }

    private static StandardMaterial3D BuildOverlayMaterial()
    {
        return new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            NoDepthTest = false
        };
    }

    private static StandardMaterial3D BuildHighlightMaterial(Color color)
    {
        return new StandardMaterial3D
        {
            AlbedoColor = color,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            NoDepthTest = false
        };
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

    private static void ClearChildren(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.Free();
        }
    }
}
