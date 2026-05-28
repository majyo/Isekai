using System;
using System.IO;
using System.Text;
using Godot;

namespace Isekai.World;

[GlobalClass]
public sealed partial class HexTileTerrainRenderer : TileMapLayer
{
    public const string DefaultRenderReportPath = "res://world/generated/tilemap_terrain_render_report.txt";
    public const string DefaultTileAtlasPath = "res://world/tilesets/world_hex_tiles.png";

    private const int TerrainSourceId = 0;
    private const int TerrainTileCount = 8;
    private const int TileSetVersion = 2;
    private const string TileSetVersionMeta = "isekai_tile_set_version";
    private const float Sqrt3 = 1.7320508f;

    public int LastRenderedTileCount { get; private set; }
    public int MissingTileCount { get; private set; }
    public bool TileSetReady { get; private set; }
    public string LastRenderDetail { get; private set; } = "not rendered";

    public void Render(
        TerrainInfoMap infoMap,
        HexTileMap tileMap,
        WorldMapConfig config,
        string reportPath = DefaultRenderReportPath)
    {
        Clear();
        LastRenderedTileCount = 0;
        MissingTileCount = 0;
        TileSetReady = false;
        LastRenderDetail = "render started";

        if (tileMap == null || config == null)
        {
            LastRenderDetail = "tile map or config missing";
            SaveReport(config, tileMap, reportPath, string.Empty);
            return;
        }

        TileSet = LoadOrCreateTileSet(config);
        TileSetReady = TileSet != null
            && TileSet.TileShape == TileSet.TileShapeEnum.Hexagon
            && TileSet.GetSource(TerrainSourceId) is TileSetAtlasSource;
        Material = BuildTerrainMaterial();

        if (!TileSetReady)
        {
            LastRenderDetail = "hex TileSet missing or invalid";
            SaveReport(config, tileMap, reportPath, string.Empty);
            return;
        }

        var terrainCounts = new int[TerrainTileCount];

        for (var tileIndex = 0; tileIndex < tileMap.TileCount; tileIndex++)
        {
            var terrain = (TerrainKind)tileMap.Terrain[tileIndex];

            if (!TryGetTerrainAtlasCoords(terrain, out var atlasCoords))
            {
                MissingTileCount++;
                continue;
            }

            var cell = GetCellCoords(tileMap, tileIndex);
            SetCell(cell, TerrainSourceId, atlasCoords);
            LastRenderedTileCount++;

            var terrainIndex = (int)terrain;

            if (terrainIndex >= 0 && terrainIndex < terrainCounts.Length)
            {
                terrainCounts[terrainIndex]++;
            }
        }

        AlignLayerToHexTileMap(tileMap, config);
        UpdateInternals();
        LastRenderDetail = $"rendered={LastRenderedTileCount}, expected={tileMap.TileCount}, missing={MissingTileCount}";
        SaveReport(config, tileMap, reportPath, BuildTerrainCoverageReport(terrainCounts));
        WorldMapDebugLogger.LogSystem($"Rendered 2D TileMap terrain: {LastRenderDetail}.");
    }

    public static bool TryGetTerrainAtlasCoords(TerrainKind terrain, out Vector2I atlasCoords)
    {
        var terrainIndex = (int)terrain;

        if (terrainIndex < 0 || terrainIndex >= TerrainTileCount)
        {
            atlasCoords = Vector2I.Zero;
            return false;
        }

        atlasCoords = new Vector2I(terrainIndex, 0);
        return true;
    }

    public static bool HasCompleteTerrainAtlasMapping()
    {
        foreach (var terrain in Enum.GetValues<TerrainKind>())
        {
            if (!TryGetTerrainAtlasCoords(terrain, out _))
            {
                return false;
            }
        }

        return true;
    }

    private static TileSet LoadOrCreateTileSet(WorldMapConfig config)
    {
        var path = string.IsNullOrWhiteSpace(config.VisualTileSetPath)
            ? WorldMapConfig.DefaultVisualTileSetPath
            : config.VisualTileSetPath;

        var tileSet = ResourceLoader.Exists(path)
            ? ResourceLoader.Load<TileSet>(path)
            : null;

        var expectedTileSize = CalculateTileSize(config);

        if (tileSet != null
            && tileSet.TileShape == TileSet.TileShapeEnum.Hexagon
            && tileSet.TileSize == expectedTileSize
            && tileSet.HasMeta(TileSetVersionMeta)
            && tileSet.GetMeta(TileSetVersionMeta).AsInt32() == TileSetVersion
            && tileSet.GetSource(TerrainSourceId) is TileSetAtlasSource)
        {
            return tileSet;
        }

        tileSet = BuildPlaceholderTileSet(config);
        EnsureResourceDirectory(path);
        var saveResult = ResourceSaver.Save(tileSet, path);

        if (saveResult != Error.Ok)
        {
            WorldMapDebugLogger.Warn($"Failed to save generated TileSet '{path}': {saveResult}");
        }

        return tileSet;
    }

    private static TileSet BuildPlaceholderTileSet(WorldMapConfig config)
    {
        var tileSize = CalculateTileSize(config);
        var image = Image.CreateEmpty(tileSize.X * TerrainTileCount, tileSize.Y, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);

        for (var terrainIndex = 0; terrainIndex < TerrainTileCount; terrainIndex++)
        {
            DrawHexTile(image, (TerrainKind)terrainIndex, terrainIndex, tileSize, GetTerrainColor((TerrainKind)terrainIndex));
        }

        EnsureResourceDirectory(DefaultTileAtlasPath);
        var atlasSaveResult = image.SavePng(ProjectSettings.GlobalizePath(DefaultTileAtlasPath));

        if (atlasSaveResult != Error.Ok)
        {
            WorldMapDebugLogger.Warn($"Failed to save generated TileMap atlas '{DefaultTileAtlasPath}': {atlasSaveResult}");
        }

        var texture = ImageTexture.CreateFromImage(image);
        var atlasSource = new TileSetAtlasSource
        {
            Texture = texture,
            TextureRegionSize = tileSize,
            UseTexturePadding = false
        };

        for (var terrainIndex = 0; terrainIndex < TerrainTileCount; terrainIndex++)
        {
            atlasSource.CreateTile(new Vector2I(terrainIndex, 0));
        }

        var tileSet = new TileSet
        {
            TileShape = TileSet.TileShapeEnum.Hexagon,
            TileLayout = TileSet.TileLayoutEnum.Stacked,
            TileOffsetAxis = TileSet.TileOffsetAxisEnum.Horizontal,
            TileSize = tileSize
        };

        tileSet.AddSource(atlasSource, TerrainSourceId);
        tileSet.SetMeta(TileSetVersionMeta, TileSetVersion);
        return tileSet;
    }

    private static void DrawHexTile(Image image, TerrainKind terrain, int tileIndex, Vector2I tileSize, Color baseColor)
    {
        var originX = tileIndex * tileSize.X;
        var center = new Vector2(originX + tileSize.X * 0.5f, tileSize.Y * 0.5f);
        var radius = MathF.Min(tileSize.X / Sqrt3, tileSize.Y * 0.5f) - 1.0f;
        var borderColor = baseColor.Darkened(0.34f);
        var highlightColor = baseColor.Lightened(0.12f);

        for (var y = 0; y < tileSize.Y; y++)
        {
            for (var x = 0; x < tileSize.X; x++)
            {
                var pixel = new Vector2(originX + x + 0.5f, y + 0.5f);
                var distance = GetPointyHexDistance(pixel - center, radius);

                if (distance > 1.0f)
                {
                    continue;
                }

                var u = x / MathF.Max(1.0f, tileSize.X - 1.0f);
                var v = y / MathF.Max(1.0f, tileSize.Y - 1.0f);
                var color = distance > 0.90f
                    ? borderColor
                    : baseColor.Lerp(highlightColor, Mathf.Clamp((0.72f - distance) * 0.20f, 0.0f, 0.08f));

                color = ApplyTerrainDetail(terrain, color, u, v, distance, x, y);
                image.SetPixel(originX + x, y, color);
            }
        }
    }

    private static Color ApplyTerrainDetail(
        TerrainKind terrain,
        Color color,
        float u,
        float v,
        float distance,
        int x,
        int y)
    {
        var noise = Hash01(x, y, (int)terrain * 97 + 13);
        var broad = 0.5f + MathF.Sin((u * 7.0f + v * 4.0f + (int)terrain) * MathF.Tau) * 0.5f;

        return terrain switch
        {
            TerrainKind.Ocean => ApplyOceanDetail(color, u, v, broad),
            TerrainKind.Coast => ApplyCoastDetail(color, u, v, distance),
            TerrainKind.Plains => color.Lerp(new Color(0.40f, 0.66f, 0.28f), noise > 0.78f ? 0.18f : 0.0f),
            TerrainKind.Forest => color.Lerp(new Color(0.03f, 0.20f, 0.09f), noise > 0.64f ? 0.35f : 0.0f),
            TerrainKind.Desert => color.Lerp(new Color(0.90f, 0.74f, 0.36f), noise > 0.80f ? 0.16f : 0.0f),
            TerrainKind.Tundra => color.Lerp(new Color(0.82f, 0.88f, 0.86f), broad > 0.74f ? 0.16f : 0.0f),
            TerrainKind.Hills => color.Lerp(new Color(0.33f, 0.31f, 0.20f), MathF.Abs(broad - 0.5f) * 0.22f),
            TerrainKind.Mountains => ApplyMountainDetail(color, u, v, broad),
            _ => color
        };
    }

    private static Color ApplyOceanDetail(Color color, float u, float v, float broad)
    {
        var wave = MathF.Sin((u * 16.0f + v * 5.0f) * MathF.Tau) * 0.5f + 0.5f;
        return color.Lerp(new Color(0.12f, 0.36f, 0.62f), wave > 0.74f ? 0.22f : broad * 0.04f);
    }

    private static Color ApplyCoastDetail(Color color, float u, float v, float distance)
    {
        var waterEdge = Mathf.Clamp((distance - 0.70f) / 0.25f, 0.0f, 1.0f);
        var diagonalFoam = MathF.Sin((u * 4.0f + v * 5.0f) * MathF.Tau) > 0.32f ? 0.10f : 0.0f;
        return color.Lerp(new Color(0.10f, 0.38f, 0.58f), waterEdge * 0.34f)
            .Lerp(new Color(0.92f, 0.86f, 0.62f), diagonalFoam);
    }

    private static Color ApplyMountainDetail(Color color, float u, float v, float broad)
    {
        var ridge = MathF.Sin((u * 5.0f - v * 8.0f) * MathF.Tau);
        var shadow = ridge > 0.35f ? 0.28f : broad * 0.08f;
        var snow = v < 0.34f && ridge < -0.15f ? 0.22f : 0.0f;
        return color.Darkened(shadow).Lerp(new Color(0.84f, 0.86f, 0.84f), snow);
    }

    private static float Hash01(int x, int y, int seed)
    {
        var hash = unchecked((uint)(x * 374761393 + y * 668265263 + seed * 1442695041));
        hash = (hash ^ (hash >> 13)) * 1274126177u;
        return (hash ^ (hash >> 16)) / (float)uint.MaxValue;
    }

    private static float GetPointyHexDistance(Vector2 point, float radius)
    {
        var q = (Sqrt3 / 3.0f * point.X - 1.0f / 3.0f * point.Y) / radius;
        var r = (2.0f / 3.0f * point.Y) / radius;
        var s = -q - r;
        return MathF.Max(MathF.Abs(q), MathF.Max(MathF.Abs(r), MathF.Abs(s)));
    }

    private void AlignLayerToHexTileMap(HexTileMap tileMap, WorldMapConfig config)
    {
        if (tileMap.TileCount == 0)
        {
            Position = Vector2.Zero;
            Scale = Vector2.One;
            return;
        }

        var cellZero = Vector2I.Zero;
        var cellRight = tileMap.GridSize.X > 1 ? new Vector2I(1, 0) : cellZero;
        var cellDown = tileMap.GridSize.Y > 1 ? new Vector2I(0, 1) : cellZero;
        var localZero = MapToLocal(cellZero);
        var localRight = MapToLocal(cellRight);
        var localDown = MapToLocal(cellDown);
        var targetZero = GetTileWorldPosition(tileMap, 0);
        var targetRight = tileMap.GridSize.X > 1 ? GetTileWorldPosition(tileMap, 1) : targetZero + new Vector2(Sqrt3 * config.HexRadius, 0.0f);
        var targetDown = tileMap.GridSize.Y > 1 ? GetTileWorldPosition(tileMap, tileMap.GridSize.X) : targetZero + new Vector2(0.0f, config.HexRadius * 1.5f);
        var localRightDelta = localRight - localZero;
        var localDownDelta = localDown - localZero;
        var targetRightDelta = targetRight - targetZero;
        var targetDownDelta = targetDown - targetZero;
        var scaleX = MathF.Abs(localRightDelta.X) <= 0.001f ? 1.0f : targetRightDelta.X / localRightDelta.X;
        var scaleY = MathF.Abs(localDownDelta.Y) <= 0.001f ? 1.0f : targetDownDelta.Y / localDownDelta.Y;

        if (!float.IsFinite(scaleX) || MathF.Abs(scaleX) <= 0.001f)
        {
            scaleX = 1.0f;
        }

        if (!float.IsFinite(scaleY) || MathF.Abs(scaleY) <= 0.001f)
        {
            scaleY = 1.0f;
        }

        Scale = new Vector2(scaleX, scaleY);
        Position = targetZero - localZero * Scale;
    }

    private static Vector2I CalculateTileSize(WorldMapConfig config)
    {
        var radius = MathF.Max(4.0f, config?.HexRadius ?? 16.0f);
        return new Vector2I(
            Math.Max(8, Mathf.RoundToInt(Sqrt3 * radius)),
            Math.Max(8, Mathf.RoundToInt(radius * 2.0f)));
    }

    private static Vector2I GetCellCoords(HexTileMap tileMap, int tileIndex)
    {
        return new Vector2I(tileIndex % tileMap.GridSize.X, tileIndex / tileMap.GridSize.X);
    }

    private static Vector2 GetTileWorldPosition(HexTileMap tileMap, int tileIndex)
    {
        return new Vector2(tileMap.WorldCenterX[tileIndex], tileMap.WorldCenterZ[tileIndex]);
    }

    private static string BuildTerrainCoverageReport(int[] terrainCounts)
    {
        var builder = new StringBuilder();

        for (var terrainIndex = 0; terrainIndex < terrainCounts.Length; terrainIndex++)
        {
            builder.AppendLine($"- {(TerrainKind)terrainIndex}: {terrainCounts[terrainIndex]}");
        }

        return builder.ToString().TrimEnd();
    }

    private static void SaveReport(WorldMapConfig config, HexTileMap tileMap, string path, string terrainCoverage)
    {
        var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);

        if (file == null)
        {
            WorldMapDebugLogger.Warn($"Failed to open TileMap terrain render report '{path}' for writing.");
            return;
        }

        file.StoreLine("TileMap Terrain Render Report");
        file.StoreLine($"Seed: {config?.Seed}");
        file.StoreLine($"VisualTerrain: TileMap2D");
        file.StoreLine($"TileSetPath: {config?.VisualTileSetPath}");
        file.StoreLine($"TileAtlasPath: {DefaultTileAtlasPath}");
        file.StoreLine($"TargetHexGridSize: {config?.TargetHexGridSize}");
        file.StoreLine($"TileCount: {tileMap?.TileCount ?? 0}");
        file.StoreLine("TerrainCoverage:");
        file.StoreLine(string.IsNullOrWhiteSpace(terrainCoverage) ? "- none" : terrainCoverage);
        file.Close();
    }

    private static void EnsureResourceDirectory(string resPath)
    {
        var directory = Path.GetDirectoryName(ProjectSettings.GlobalizePath(resPath));

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static Color GetTerrainColor(TerrainKind terrain)
    {
        return terrain switch
        {
            TerrainKind.Ocean => new Color(0.05f, 0.23f, 0.46f),
            TerrainKind.Coast => new Color(0.82f, 0.73f, 0.42f),
            TerrainKind.Plains => new Color(0.32f, 0.58f, 0.24f),
            TerrainKind.Forest => new Color(0.08f, 0.34f, 0.17f),
            TerrainKind.Desert => new Color(0.78f, 0.60f, 0.28f),
            TerrainKind.Tundra => new Color(0.66f, 0.74f, 0.72f),
            TerrainKind.Hills => new Color(0.44f, 0.40f, 0.26f),
            TerrainKind.Mountains => new Color(0.53f, 0.52f, 0.50f),
            _ => Colors.Magenta
        };
    }

    private static ShaderMaterial BuildTerrainMaterial()
    {
        var shader = new Shader
        {
            Code = """
                shader_type canvas_item;

                void fragment() {
                    vec4 tex = texture(TEXTURE, UV) * COLOR;
                    float water = smoothstep(0.16, 0.34, tex.b - max(tex.r, tex.g));
                    float wave = sin((UV.x * 96.0 + UV.y * 64.0) + TIME * 1.8) * 0.035;
                    tex.rgb += water * vec3(wave, wave * 1.4, wave * 1.8);
                    COLOR = tex;
                }
                """
        };

        return new ShaderMaterial
        {
            Shader = shader
        };
    }
}
