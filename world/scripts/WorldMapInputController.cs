using System;
using Godot;

namespace Isekai.World;

public sealed partial class WorldMapInputController : Node
{
    [Export] public NodePath CameraPath { get; set; } = "../camera_controller/camera";

    [Export(PropertyHint.Range, "256,20000,1")]
    public float RaycastMaxDistance { get; set; } = 12000.0f;

    [Export(PropertyHint.Range, "4,256,1")]
    public float RaycastStepDistance { get; set; } = 48.0f;

    [Export(PropertyHint.Range, "1,16,1")]
    public int RaycastRefineIterations { get; set; } = 8;

    private WorldMapConfig _config;
    private TerrainInfoMap _infoMap;
    private HexTileMap _tileMap;
    private HexOverlayRenderer _overlayRenderer;
    private Camera3D _camera;
    private TileInspectorPanel _inspectorPanel;
    private int _hoveredTileIndex = -1;
    private int _selectedTileIndex = -1;

    public bool IsInitialized => _config != null && _infoMap != null && _tileMap != null && _overlayRenderer != null && _inspectorPanel != null;

    public void Initialize(WorldMapConfig config, TerrainInfoMap infoMap, HexTileMap tileMap, HexOverlayRenderer overlayRenderer)
    {
        _config = config;
        _infoMap = infoMap;
        _tileMap = tileMap;
        _overlayRenderer = overlayRenderer;
        _camera = GetNodeOrNull<Camera3D>(CameraPath) ?? GetViewport().GetCamera3D();

        _inspectorPanel ??= new TileInspectorPanel();

        if (_inspectorPanel.GetParent() == null)
        {
            AddChild(_inspectorPanel);
        }

        _inspectorPanel.ShowEmpty();

        WorldMapDebugLogger.LogSystem("World map input controller initialized.");
    }

    public override void _Ready()
    {
        _camera = GetNodeOrNull<Camera3D>(CameraPath) ?? GetViewport().GetCamera3D();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_tileMap == null || _hoveredTileIndex < 0)
        {
            return;
        }

        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            return;
        }

        SelectTile(_hoveredTileIndex);
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (_config == null || _infoMap == null || _tileMap == null || _overlayRenderer == null)
        {
            return;
        }

        if (Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            ClearHover();
            return;
        }

        UpdateHoverFromMouse();
    }

    private void UpdateHoverFromMouse()
    {
        if (_camera == null)
        {
            _camera = GetNodeOrNull<Camera3D>(CameraPath) ?? GetViewport().GetCamera3D();
        }

        if (_camera == null)
        {
            ClearHover();
            return;
        }

        var mousePosition = GetViewport().GetMousePosition();
        var rayOrigin = _camera.ProjectRayOrigin(mousePosition);
        var rayDirection = _camera.ProjectRayNormal(mousePosition).Normalized();

        if (!TrySampleTerrainHit(rayOrigin, rayDirection, out var hitWorldXz))
        {
            ClearHover();
            return;
        }

        var axial = WorldMapCoordinateUtility.WorldXzToAxial(hitWorldXz, _config);

        if (!_tileMap.TryGetTileIndex(axial, out var tileIndex))
        {
            ClearHover();
            return;
        }

        if (tileIndex == _hoveredTileIndex)
        {
            return;
        }

        _hoveredTileIndex = tileIndex;
        _overlayRenderer.SetHoveredTile(tileIndex);
    }

    private bool TrySampleTerrainHit(Vector3 rayOrigin, Vector3 rayDirection, out Vector2 hitWorldXz)
    {
        var previousDistance = 0.0f;
        var previousDelta = GetHeightDelta(rayOrigin);

        for (var distance = RaycastStepDistance; distance <= RaycastMaxDistance; distance += RaycastStepDistance)
        {
            var point = rayOrigin + rayDirection * distance;
            var delta = GetHeightDelta(point);

            if (delta <= 0.0f && previousDelta >= 0.0f)
            {
                var hitDistance = RefineTerrainHit(rayOrigin, rayDirection, previousDistance, distance);
                var hit = rayOrigin + rayDirection * hitDistance;
                hitWorldXz = WorldMapCoordinateUtility.WorldPositionToWorldXz(hit);
                return IsWithinWorld(hitWorldXz);
            }

            previousDistance = distance;
            previousDelta = delta;
        }

        hitWorldXz = Vector2.Zero;
        return false;
    }

    private float RefineTerrainHit(Vector3 rayOrigin, Vector3 rayDirection, float nearDistance, float farDistance)
    {
        for (var iteration = 0; iteration < RaycastRefineIterations; iteration++)
        {
            var midDistance = (nearDistance + farDistance) * 0.5f;
            var midPoint = rayOrigin + rayDirection * midDistance;

            if (GetHeightDelta(midPoint) > 0.0f)
            {
                nearDistance = midDistance;
            }
            else
            {
                farDistance = midDistance;
            }
        }

        return farDistance;
    }

    private float GetHeightDelta(Vector3 worldPosition)
    {
        var worldXz = WorldMapCoordinateUtility.WorldPositionToWorldXz(worldPosition);
        var terrainHeight = _infoMap.SampleHeightUv(WorldMapCoordinateUtility.WorldXzToUv(worldXz, _config));
        return worldPosition.Y - terrainHeight;
    }

    private bool IsWithinWorld(Vector2 worldXz)
    {
        var halfSize = _config.WorldSize * 0.5f;
        return worldXz.X >= -halfSize.X
            && worldXz.X <= halfSize.X
            && worldXz.Y >= -halfSize.Y
            && worldXz.Y <= halfSize.Y;
    }

    private void SelectTile(int tileIndex)
    {
        _selectedTileIndex = tileIndex;
        _overlayRenderer.SetSelectedTile(tileIndex);
        _inspectorPanel?.ShowTile(_tileMap.GetTile(tileIndex), _tileMap);
    }

    private void ClearHover()
    {
        if (_hoveredTileIndex < 0)
        {
            return;
        }

        _hoveredTileIndex = -1;
        _overlayRenderer.ClearHoveredTile();
    }

    private sealed partial class TileInspectorPanel : CanvasLayer
    {
        private readonly Label _titleLabel = new();
        private readonly Label _bodyLabel = new();

        public TileInspectorPanel()
        {
            Name = "tile_inspector_panel";
            Layer = 32;

            var margin = new MarginContainer
            {
                Name = "tile_inspector_margin",
                AnchorLeft = 1.0f,
                AnchorTop = 0.0f,
                AnchorRight = 1.0f,
                AnchorBottom = 0.0f,
                OffsetLeft = -340.0f,
                OffsetTop = 18.0f,
                OffsetRight = -18.0f,
                OffsetBottom = 0.0f
            };

            var panel = new PanelContainer
            {
                Name = "tile_inspector"
            };

            panel.CustomMinimumSize = new Vector2(322.0f, 228.0f);

            var padding = new MarginContainer
            {
                Name = "tile_inspector_padding"
            };

            padding.AddThemeConstantOverride("margin_left", 14);
            padding.AddThemeConstantOverride("margin_top", 12);
            padding.AddThemeConstantOverride("margin_right", 14);
            padding.AddThemeConstantOverride("margin_bottom", 12);

            var content = new VBoxContainer
            {
                Name = "tile_inspector_content"
            };

            content.AddThemeConstantOverride("separation", 8);

            _titleLabel.Name = "tile_title";
            _titleLabel.Text = "Tile";
            _titleLabel.AddThemeFontSizeOverride("font_size", 18);

            _bodyLabel.Name = "tile_body";
            _bodyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _bodyLabel.AddThemeFontSizeOverride("font_size", 14);

            content.AddChild(_titleLabel);
            content.AddChild(_bodyLabel);
            padding.AddChild(content);
            panel.AddChild(padding);
            margin.AddChild(panel);
            AddChild(margin);
        }

        public void ShowEmpty()
        {
            _titleLabel.Text = "Tile";
            _bodyLabel.Text = "No tile selected";
        }

        public void ShowTile(HexTile tile, HexTileMap tileMap)
        {
            _titleLabel.Text = $"Hex {tile.Q}, {tile.R}";
            _bodyLabel.Text =
                $"Terrain: {tile.Terrain}\n" +
                $"Biome: {tile.Biome}\n" +
                $"Height: {tile.AverageHeight:0.0} m  ({tile.MinHeight:0.0} - {tile.MaxHeight:0.0})\n" +
                $"Slope: {tile.Slope:0.00}\n" +
                $"Water: {FormatBool(tile.IsWater)}\n" +
                $"Coast: {FormatBool(tile.IsCoastal)}\n" +
                $"Movement: {tile.MovementCost:0.00}\n" +
                $"Owner: {tile.OwnerId}\n" +
                $"Region: {tile.RegionId}\n" +
                $"Rivers: {tile.RiverEdgeCount}\n" +
                $"World XZ: {tile.WorldCenterXz.X:0.0}, {tile.WorldCenterXz.Y:0.0}";
        }

        private static string FormatBool(bool value)
        {
            return value ? "yes" : "no";
        }
    }
}
