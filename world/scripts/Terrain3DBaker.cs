using Godot;

namespace Isekai.World;

public sealed class Terrain3DBaker
{
    private const string TerrainPreviewNodeName = "terrain_preview_mesh";
    private const string WaterPreviewNodeName = "water_preview_plane";
    public const string DefaultBakeReportPath = "res://world/generated/visual_terrain_bake_report.txt";
    public const string DefaultTerrainDirectory = "res://world/terrain";
    public const string DefaultGeneratedTerrainDirectory = "res://world/terrain/generated";
    public const string DefaultTerrain3DDataDirectory = "res://world/terrain/generated/terrain3d_data";
    public const string DefaultTerrain3DReportsDirectory = "res://world/terrain/generated/reports";

    public bool IsTerrain3DPluginAvailable()
    {
        return ClassDB.ClassExists("Terrain3D");
    }

    public void Bake(TerrainInfoMap infoMap, WorldMapConfig config, Node3D terrainRoot, string reportPath = DefaultBakeReportPath)
    {
        ClearTerrainRoot(terrainRoot);

        var pluginAvailable = IsTerrain3DPluginAvailable();

        if (pluginAvailable)
        {
            WorldMapDebugLogger.LogSystem("Terrain3D plugin is available, but direct Terrain3D data writing is not implemented yet. Using preview mesh for this MVP step.");
        }
        else
        {
            WorldMapDebugLogger.LogSystem("Terrain3D plugin is not installed. Using ArrayMesh terrain preview fallback.");
        }

        var previewMesh = BuildPreviewMesh(infoMap, config);
        terrainRoot.AddChild(previewMesh);
        previewMesh.Owner = terrainRoot.Owner;

        var waterPlane = BuildWaterPlane(config);
        terrainRoot.AddChild(waterPlane);
        waterPlane.Owner = terrainRoot.Owner;

        WorldMapDebugLogger.LogBakeStep($"Built visual terrain preview mesh at grid {config.VisualTerrainGridSize}.", config);
        SaveReport(config, pluginAvailable, reportPath);
    }

    private static void ClearTerrainRoot(Node terrainRoot)
    {
        foreach (var child in terrainRoot.GetChildren())
        {
            terrainRoot.RemoveChild(child);
            child.Free();
        }
    }

    private static MeshInstance3D BuildPreviewMesh(TerrainInfoMap infoMap, WorldMapConfig config)
    {
        var gridSize = new Vector2I(
            Mathf.Max(2, config.VisualTerrainGridSize.X),
            Mathf.Max(2, config.VisualTerrainGridSize.Y));

        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        for (var y = 0; y < gridSize.Y - 1; y++)
        {
            for (var x = 0; x < gridSize.X - 1; x++)
            {
                AddVertex(surfaceTool, infoMap, config, gridSize, x, y);
                AddVertex(surfaceTool, infoMap, config, gridSize, x + 1, y);
                AddVertex(surfaceTool, infoMap, config, gridSize, x, y + 1);

                AddVertex(surfaceTool, infoMap, config, gridSize, x + 1, y);
                AddVertex(surfaceTool, infoMap, config, gridSize, x + 1, y + 1);
                AddVertex(surfaceTool, infoMap, config, gridSize, x, y + 1);
            }
        }

        surfaceTool.GenerateNormals();

        var mesh = surfaceTool.Commit();
        var meshInstance = new MeshInstance3D
        {
            Name = TerrainPreviewNodeName,
            Mesh = mesh,
            MaterialOverride = BuildTerrainMaterial()
        };

        return meshInstance;
    }

    private static void AddVertex(SurfaceTool surfaceTool, TerrainInfoMap infoMap, WorldMapConfig config, Vector2I gridSize, int x, int y)
    {
        var uv = new Vector2(
            gridSize.X == 1 ? 0.0f : x / (float)(gridSize.X - 1),
            gridSize.Y == 1 ? 0.0f : y / (float)(gridSize.Y - 1));

        var worldXz = WorldMapCoordinateUtility.UvToWorldXz(uv, config);
        var height = infoMap.SampleHeightUv(uv);
        var biome = infoMap.SampleBiomeUv(uv);

        surfaceTool.SetUV(uv);
        surfaceTool.SetColor(GetBiomeColor(biome, height, config));
        surfaceTool.AddVertex(WorldMapCoordinateUtility.WorldXzToWorldPosition(worldXz, height));
    }

    private static StandardMaterial3D BuildTerrainMaterial()
    {
        return new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 0.92f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled
        };
    }

    private static MeshInstance3D BuildWaterPlane(WorldMapConfig config)
    {
        var planeMesh = new PlaneMesh
        {
            Size = config.WorldSize,
            SubdivideWidth = 1,
            SubdivideDepth = 1
        };

        var waterMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.08f, 0.22f, 0.42f, 0.58f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.35f,
            Metallic = 0.0f
        };

        return new MeshInstance3D
        {
            Name = WaterPreviewNodeName,
            Mesh = planeMesh,
            Position = new Vector3(0.0f, config.SeaLevel + 0.35f, 0.0f),
            MaterialOverride = waterMaterial
        };
    }

    private static Color GetBiomeColor(BiomeKind biome, float height, WorldMapConfig config)
    {
        if (height <= config.SeaLevel)
        {
            var depth = Mathf.Clamp((config.SeaLevel - height) / 320.0f, 0.0f, 1.0f);
            return new Color(0.08f, 0.32f, 0.55f).Lerp(new Color(0.02f, 0.09f, 0.22f), depth);
        }

        return biome switch
        {
            BiomeKind.Coast => new Color(0.78f, 0.70f, 0.45f),
            BiomeKind.Grassland => new Color(0.30f, 0.55f, 0.22f),
            BiomeKind.Forest => new Color(0.10f, 0.31f, 0.15f),
            BiomeKind.Desert => new Color(0.74f, 0.58f, 0.28f),
            BiomeKind.Tundra => new Color(0.66f, 0.72f, 0.70f),
            BiomeKind.Hills => new Color(0.42f, 0.39f, 0.25f),
            BiomeKind.Mountain => new Color(0.50f, 0.49f, 0.48f),
            _ => new Color(0.35f, 0.48f, 0.28f)
        };
    }

    private static void SaveReport(WorldMapConfig config, bool pluginAvailable, string path)
    {
        var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);

        if (file == null)
        {
            WorldMapDebugLogger.Warn($"Failed to open visual terrain bake report '{path}' for writing.");
            return;
        }

        file.StoreLine("Visual Terrain Bake Report");
        file.StoreLine($"Seed: {config.Seed}");
        file.StoreLine($"WorldSize: {config.WorldSize}");
        file.StoreLine($"InfoMapSize: {config.InfoMapSize}");
        file.StoreLine($"VisualTerrainGridSize: {config.VisualTerrainGridSize}");
        file.StoreLine($"SeaLevel: {config.SeaLevel}");
        file.StoreLine($"Terrain3DPluginAvailable: {pluginAvailable}");
        file.StoreLine($"TerrainDirectory: {DefaultTerrainDirectory}");
        file.StoreLine($"GeneratedTerrainDirectory: {DefaultGeneratedTerrainDirectory}");
        file.StoreLine($"Terrain3DDataDirectory: {DefaultTerrain3DDataDirectory}");
        file.StoreLine($"Terrain3DReportsDirectory: {DefaultTerrain3DReportsDirectory}");
        file.StoreLine(pluginAvailable
            ? "Mode: ArrayMesh preview fallback. Terrain3D direct write path is pending."
            : "Mode: ArrayMesh preview fallback. Terrain3D plugin is not installed.");
        file.Close();
    }
}
