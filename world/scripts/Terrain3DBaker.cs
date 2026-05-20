using System;
using System.IO;
using System.Text;
using Godot;
using GodotArray = Godot.Collections.Array;

namespace Isekai.World;

public sealed class Terrain3DBaker
{
    public const string TerrainPreviewNodeName = "terrain_preview_mesh";
    public const string Terrain3DNodeName = "terrain3d_visual_terrain";

    private const string Terrain3DClassName = "Terrain3D";
    private const string Terrain3DDataClassName = "Terrain3DData";
    private const string Terrain3DRegionClassName = "Terrain3DRegion";
    private const string Terrain3DMaterialClassName = "Terrain3DMaterial";
    private const string WaterPreviewNodeName = "water_preview_plane";
    private const float HeightSampleTolerance = 8.0f;
    private const float RockSlopeStart = 0.26f;
    private const float RockSlopeFull = 0.62f;
    private const float RiverWetnessStart = 0.02f;

    public const string DefaultBakeReportPath = "res://world/generated/visual_terrain_bake_report.txt";
    public const string DefaultTerrainDirectory = "res://world/terrain";
    public const string DefaultGeneratedTerrainDirectory = "res://world/terrain/generated";
    public const string DefaultTerrain3DDataDirectory = "res://world/terrain/generated/terrain3d_data";
    public const string DefaultTerrain3DReportsDirectory = "res://world/terrain/generated/reports";

    public bool IsTerrain3DPluginAvailable()
    {
        return ClassDB.ClassExists(Terrain3DClassName)
            && ClassDB.ClassExists(Terrain3DDataClassName)
            && ClassDB.ClassExists(Terrain3DRegionClassName);
    }

    public void Bake(TerrainInfoMap infoMap, WorldMapConfig config, Node3D terrainRoot, string reportPath = DefaultBakeReportPath)
    {
        ClearTerrainRoot(terrainRoot);

        var pluginAvailable = IsTerrain3DPluginAvailable();
        var requestedMode = config.VisualTerrainMode;
        var effectiveMode = VisualTerrainMode.ArrayMeshPreview;
        var fallbackReason = string.Empty;
        Terrain3DBakeResult terrain3DResult = null;

        if (requestedMode == VisualTerrainMode.Terrain3D)
        {
            if (pluginAvailable)
            {
                terrain3DResult = TryBuildTerrain3D(infoMap, config, terrainRoot);

                if (terrain3DResult.Success)
                {
                    effectiveMode = VisualTerrainMode.Terrain3D;
                    WorldMapDebugLogger.LogBakeStep(
                        $"Built Terrain3D visual terrain with {terrain3DResult.RegionCount} regions at vertex spacing {terrain3DResult.VertexSpacing:0.###}.",
                        config);
                }
                else
                {
                    fallbackReason = terrain3DResult.FailureReason;
                    ClearTerrainRoot(terrainRoot);
                    WorldMapDebugLogger.Warn($"Terrain3D bake failed, falling back to ArrayMesh preview: {fallbackReason}");
                }
            }
            else
            {
                fallbackReason = "Terrain3D GDExtension classes are not available.";
                WorldMapDebugLogger.Warn("Terrain3D mode requested, but plugin classes are not available. Using ArrayMesh terrain preview fallback.");
            }
        }
        else if (pluginAvailable)
        {
            WorldMapDebugLogger.LogSystem("ArrayMesh terrain preview mode selected. Terrain3D plugin is available for opt-in bakes.");
        }

        if (effectiveMode == VisualTerrainMode.ArrayMeshPreview)
        {
            var previewMesh = BuildPreviewMesh(infoMap, config);
            terrainRoot.AddChild(previewMesh);
            previewMesh.Owner = terrainRoot.Owner;

            WorldMapDebugLogger.LogBakeStep($"Built visual terrain preview mesh at grid {config.VisualTerrainGridSize}.", config);
        }

        var waterPlane = BuildWaterPlane(config);
        terrainRoot.AddChild(waterPlane);
        waterPlane.Owner = terrainRoot.Owner;

        SaveReport(config, pluginAvailable, requestedMode, effectiveMode, fallbackReason, terrain3DResult, reportPath);
    }

    private static void ClearTerrainRoot(Node terrainRoot)
    {
        foreach (var child in terrainRoot.GetChildren())
        {
            terrainRoot.RemoveChild(child);
            child.Free();
        }
    }

    private static Terrain3DBakeResult TryBuildTerrain3D(TerrainInfoMap infoMap, WorldMapConfig config, Node3D terrainRoot)
    {
        Node terrainNode = null;

        try
        {
            var terrain = ClassDB.Instantiate(Terrain3DClassName).AsGodotObject();

            if (terrain is not Node node)
            {
                return Terrain3DBakeResult.Fail("ClassDB did not return a Terrain3D Node instance.");
            }

            terrainNode = node;
            terrainNode.Name = Terrain3DNodeName;
            terrainRoot.AddChild(terrainNode);
            terrainNode.Owner = terrainRoot.Owner;

            var vertexSpacing = CalculateVertexSpacing(infoMap, config, out var spacingDetail);
            terrain.Set("vertex_spacing", Variant.From(vertexSpacing));
            terrain.Set("data_directory", Variant.From(DefaultTerrain3DDataDirectory));

            var data = GetOrCreateTerrainData(terrain);

            if (data == null)
            {
                return Terrain3DBakeResult.Fail("Terrain3D node did not provide Terrain3DData.");
            }

            var typeHeight = GetTerrainRegionConstant("TYPE_HEIGHT", 0);
            var typeColor = GetTerrainRegionConstant("TYPE_COLOR", 2);
            var typeMax = GetTerrainRegionConstant("TYPE_MAX", 3);
            var heightRange = CalculateHeightRange(infoMap);
            var heightScale = MathF.Max(0.0001f, heightRange.Y - heightRange.X);
            var heightOffset = heightRange.X;
            var heightImage = BuildTerrain3DHeightImage(infoMap, heightOffset, heightScale);
            var colorBake = BuildTerrain3DColorImage(infoMap, config);
            var importPosition = BuildTerrain3DImportPosition(config);
            var colormapMaterialConfigured = ConfigureTerrain3DColormapMaterial(terrain);

            var images = new GodotArray();
            images.Resize(typeMax);
            images[typeHeight] = Variant.From(heightImage);
            images[typeColor] = Variant.From(colorBake.Image);

            data.Call(
                "import_images",
                Variant.From(images),
                Variant.From(importPosition),
                Variant.From(heightOffset),
                Variant.From(heightScale));

            data.Call(
                "update_maps",
                Variant.From(typeMax),
                Variant.From(true),
                Variant.From(false));

            data.Call("calc_height_range", Variant.From(true));

            PrepareTerrain3DDataDirectory(DefaultTerrain3DDataDirectory);
            var saveResult = data.Call("save_directory", Variant.From(DefaultTerrain3DDataDirectory));
            var savedFileCount = CountSavedTerrain3DFiles(DefaultTerrain3DDataDirectory);
            var regionCount = CallInt(data, "get_region_count");
            var sampleReport = BuildHeightSampleReport(infoMap, config, data, out var maxSampleError);

            if (regionCount <= 0)
            {
                return Terrain3DBakeResult.Fail("Terrain3D import produced no active regions.");
            }

            if (maxSampleError > HeightSampleTolerance)
            {
                return Terrain3DBakeResult.Fail($"Terrain3D height samples exceeded tolerance. max_error={maxSampleError:0.###}, tolerance={HeightSampleTolerance:0.###}");
            }

            return new Terrain3DBakeResult(
                true,
                string.Empty,
                new Vector2I(heightImage.GetWidth(), heightImage.GetHeight()),
                importPosition,
                heightOffset,
                heightScale,
                vertexSpacing,
                spacingDetail,
                new Vector2I(colorBake.Image.GetWidth(), colorBake.Image.GetHeight()),
                colormapMaterialConfigured,
                colorBake.CoverageReport,
                regionCount,
                savedFileCount,
                maxSampleError,
                FormatVariant(saveResult),
                sampleReport);
        }
        catch (Exception exception)
        {
            if (terrainNode != null)
            {
                terrainNode.GetParent()?.RemoveChild(terrainNode);
                terrainNode.Free();
            }

            return Terrain3DBakeResult.Fail(exception.Message);
        }
    }

    private static GodotObject GetOrCreateTerrainData(GodotObject terrain)
    {
        var data = terrain.Call("get_data").AsGodotObject();

        if (data != null)
        {
            return data;
        }

        data = ClassDB.Instantiate(Terrain3DDataClassName).AsGodotObject();
        terrain.Set("data", Variant.From(data));
        return data;
    }

    private static Image BuildTerrain3DHeightImage(TerrainInfoMap infoMap, float heightOffset, float heightScale)
    {
        var image = Image.CreateEmpty(infoMap.Size.X, infoMap.Size.Y, false, Image.Format.Rf);

        for (var y = 0; y < infoMap.Size.Y; y++)
        {
            for (var x = 0; x < infoMap.Size.X; x++)
            {
                var normalizedHeight = (infoMap.GetHeight(x, y) - heightOffset) / heightScale;
                image.SetPixel(x, y, new Color(normalizedHeight, 0.0f, 0.0f, 1.0f));
            }
        }

        return image;
    }

    private static TerrainColorBakeResult BuildTerrain3DColorImage(TerrainInfoMap infoMap, WorldMapConfig config)
    {
        var image = Image.CreateEmpty(infoMap.Size.X, infoMap.Size.Y, false, Image.Format.Rgba8);
        var counts = new int[TerrainColorLayerCount];

        for (var y = 0; y < infoMap.Size.Y; y++)
        {
            for (var x = 0; x < infoMap.Size.X; x++)
            {
                var layer = GetBaseColorLayer(infoMap.GetBiome(x, y));
                var color = GetBaseColor(layer, infoMap.GetHeight(x, y), config);

                var slope = CalculateSlope01(infoMap, x, y, config);
                var rockWeight = CalculateRockWeight(infoMap.GetHeight(x, y), config, slope);

                if (rockWeight > 0.0f)
                {
                    color = color.Lerp(new Color(0.46f, 0.46f, 0.43f), rockWeight);

                    if (rockWeight >= 0.5f)
                    {
                        layer = TerrainColorLayer.Rock;
                    }
                }

                var riverFlow = infoMap.RiverFlowMap[infoMap.GetIndex(x, y)];

                if (riverFlow > RiverWetnessStart && infoMap.IsLand(x, y))
                {
                    var wetness = Mathf.Clamp(0.22f + riverFlow * 0.5f, 0.0f, 0.72f);
                    color = color.Lerp(new Color(0.19f, 0.24f, 0.17f), wetness);
                    layer = TerrainColorLayer.Riverbank;
                }

                counts[(int)layer]++;
                image.SetPixel(x, y, color);
            }
        }

        return new TerrainColorBakeResult(image, BuildColorCoverageReport(counts, infoMap.CellCount));
    }

    private static bool ConfigureTerrain3DColormapMaterial(GodotObject terrain)
    {
        var material = terrain.Get("material").AsGodotObject();

        if (material == null && ClassDB.ClassExists(Terrain3DMaterialClassName))
        {
            material = ClassDB.Instantiate(Terrain3DMaterialClassName).AsGodotObject();
            terrain.Set("material", Variant.From(material));
        }

        if (material == null)
        {
            return false;
        }

        material.Set("show_checkered", Variant.From(false));
        material.Set("show_colormap", Variant.From(true));
        return true;
    }

    private static TerrainColorLayer GetBaseColorLayer(BiomeKind biome)
    {
        return biome switch
        {
            BiomeKind.Ocean => TerrainColorLayer.Ocean,
            BiomeKind.Coast => TerrainColorLayer.Coast,
            BiomeKind.Grassland => TerrainColorLayer.Grassland,
            BiomeKind.Forest => TerrainColorLayer.Forest,
            BiomeKind.Desert => TerrainColorLayer.Desert,
            BiomeKind.Tundra => TerrainColorLayer.Tundra,
            BiomeKind.Hills => TerrainColorLayer.Hills,
            BiomeKind.Mountain => TerrainColorLayer.Mountain,
            _ => TerrainColorLayer.Grassland
        };
    }

    private static Color GetBaseColor(TerrainColorLayer layer, float height, WorldMapConfig config)
    {
        if (height <= config.SeaLevel)
        {
            var depth = Mathf.Clamp((config.SeaLevel - height) / 320.0f, 0.0f, 1.0f);
            return new Color(0.08f, 0.32f, 0.55f).Lerp(new Color(0.02f, 0.09f, 0.22f), depth);
        }

        return layer switch
        {
            TerrainColorLayer.Coast => new Color(0.78f, 0.70f, 0.45f),
            TerrainColorLayer.Grassland => new Color(0.30f, 0.55f, 0.22f),
            TerrainColorLayer.Forest => new Color(0.10f, 0.31f, 0.15f),
            TerrainColorLayer.Desert => new Color(0.74f, 0.58f, 0.28f),
            TerrainColorLayer.Tundra => new Color(0.66f, 0.72f, 0.70f),
            TerrainColorLayer.Hills => new Color(0.42f, 0.39f, 0.25f),
            TerrainColorLayer.Mountain => new Color(0.50f, 0.49f, 0.48f),
            TerrainColorLayer.Rock => new Color(0.46f, 0.46f, 0.43f),
            TerrainColorLayer.Riverbank => new Color(0.19f, 0.24f, 0.17f),
            _ => new Color(0.35f, 0.48f, 0.28f)
        };
    }

    private static float CalculateSlope01(TerrainInfoMap infoMap, int x, int y, WorldMapConfig config)
    {
        var left = infoMap.GetHeight(Mathf.Max(0, x - 1), y);
        var right = infoMap.GetHeight(Mathf.Min(infoMap.Size.X - 1, x + 1), y);
        var down = infoMap.GetHeight(x, Mathf.Max(0, y - 1));
        var up = infoMap.GetHeight(x, Mathf.Min(infoMap.Size.Y - 1, y + 1));
        var xWorldStep = config.WorldSize.X / MathF.Max(1.0f, infoMap.Size.X - 1);
        var zWorldStep = config.WorldSize.Y / MathF.Max(1.0f, infoMap.Size.Y - 1);
        var dx = MathF.Abs(right - left) / MathF.Max(0.001f, xWorldStep * 2.0f);
        var dz = MathF.Abs(up - down) / MathF.Max(0.001f, zWorldStep * 2.0f);
        var slope = MathF.Sqrt(dx * dx + dz * dz);

        return Mathf.Clamp(slope / 0.35f, 0.0f, 1.0f);
    }

    private static float CalculateRockWeight(float height, WorldMapConfig config, float slope)
    {
        if (height <= config.SeaLevel)
        {
            return 0.0f;
        }

        var slopeWeight = Mathf.Clamp((slope - RockSlopeStart) / (RockSlopeFull - RockSlopeStart), 0.0f, 1.0f);
        var elevation01 = Mathf.Clamp((height - config.SeaLevel) / MathF.Max(1.0f, config.MaxHeight - config.SeaLevel), 0.0f, 1.0f);
        var elevationWeight = Mathf.Clamp((elevation01 - 0.62f) / 0.28f, 0.0f, 1.0f) * 0.5f;

        return Mathf.Clamp(MathF.Max(slopeWeight, elevationWeight), 0.0f, 1.0f);
    }

    private static string BuildColorCoverageReport(int[] counts, int totalCount)
    {
        var builder = new StringBuilder();
        var denominator = Math.Max(1, totalCount);

        for (var index = 0; index < counts.Length; index++)
        {
            var layer = (TerrainColorLayer)index;
            var percent = counts[index] * 100.0f / denominator;
            builder.AppendLine($"- {layer}: {counts[index]} ({percent:0.##}%)");
        }

        return builder.ToString().TrimEnd();
    }

    private static Vector2 CalculateHeightRange(TerrainInfoMap infoMap)
    {
        if (infoMap.HeightMap.Length == 0)
        {
            return Vector2.Zero;
        }

        var min = infoMap.HeightMap[0];
        var max = infoMap.HeightMap[0];

        foreach (var height in infoMap.HeightMap)
        {
            min = MathF.Min(min, height);
            max = MathF.Max(max, height);
        }

        return new Vector2(min, max);
    }

    private static float CalculateVertexSpacing(TerrainInfoMap infoMap, WorldMapConfig config, out string detail)
    {
        var xSpacing = config.WorldSize.X / Math.Max(1.0f, infoMap.Size.X);
        var zSpacing = config.WorldSize.Y / Math.Max(1.0f, infoMap.Size.Y);
        var spacing = MathF.Min(xSpacing, zSpacing);
        var delta = MathF.Abs(xSpacing - zSpacing);

        detail = delta <= 0.001f
            ? $"uniform={spacing:0.###}"
            : $"x={xSpacing:0.###}, z={zSpacing:0.###}, using_min={spacing:0.###}";

        return MathF.Max(0.0001f, spacing);
    }

    private static Vector3 BuildTerrain3DImportPosition(WorldMapConfig config)
    {
        return new Vector3(
            -config.WorldSize.X * 0.5f,
            0.0f,
            -config.WorldSize.Y * 0.5f);
    }

    private static string BuildHeightSampleReport(TerrainInfoMap infoMap, WorldMapConfig config, GodotObject data, out float maxError)
    {
        var builder = new StringBuilder();
        var sampleUvs = new[]
        {
            new Vector2(0.001f, 0.001f),
            new Vector2(0.999f, 0.001f),
            new Vector2(0.001f, 0.999f),
            new Vector2(0.999f, 0.999f),
            new Vector2(0.5f, 0.5f),
            new Vector2(1.0f / 3.0f, 2.0f / 3.0f),
            new Vector2(0.875f, 0.2f)
        };

        maxError = 0.0f;

        foreach (var uv in sampleUvs)
        {
            var worldXz = WorldMapCoordinateUtility.UvToWorldXz(uv, config);
            var worldPosition = WorldMapCoordinateUtility.WorldXzToWorldPosition(worldXz);
            var expectedHeight = infoMap.SampleHeightUv(uv);
            var actualHeight = CallFloat(data, "get_height", worldPosition);
            var error = MathF.Abs(actualHeight - expectedHeight);

            maxError = MathF.Max(maxError, error);
            builder.AppendLine($"- uv={uv}, world_xz={worldXz}, expected={expectedHeight:0.###}, terrain3d={actualHeight:0.###}, error={error:0.###}");
        }

        return builder.ToString().TrimEnd();
    }

    private static int GetTerrainRegionConstant(string constantName, int fallback)
    {
        return ClassDB.ClassHasIntegerConstant(Terrain3DRegionClassName, constantName)
            ? (int)ClassDB.ClassGetIntegerConstant(Terrain3DRegionClassName, constantName)
            : fallback;
    }

    private static int CallInt(GodotObject target, string method)
    {
        return target.Call(method).AsInt32();
    }

    private static float CallFloat(GodotObject target, string method, Vector3 position)
    {
        return target.Call(method, Variant.From(position)).AsSingle();
    }

    private static string FormatVariant(Variant value)
    {
        return $"{value.VariantType}:{value}";
    }

    private static void PrepareTerrain3DDataDirectory(string resPath)
    {
        var globalPath = ProjectSettings.GlobalizePath(resPath);
        Directory.CreateDirectory(globalPath);

        foreach (var file in Directory.GetFiles(globalPath, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file) == ".gitkeep")
            {
                continue;
            }

            File.Delete(file);
        }

        foreach (var directory in Directory.GetDirectories(globalPath))
        {
            Directory.Delete(directory, true);
        }
    }

    private static int CountSavedTerrain3DFiles(string resPath)
    {
        var globalPath = ProjectSettings.GlobalizePath(resPath);

        if (!Directory.Exists(globalPath))
        {
            return 0;
        }

        var count = 0;

        foreach (var file in Directory.GetFiles(globalPath, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file) != ".gitkeep")
            {
                count++;
            }
        }

        return count;
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

    private static void SaveReport(
        WorldMapConfig config,
        bool pluginAvailable,
        VisualTerrainMode requestedMode,
        VisualTerrainMode effectiveMode,
        string fallbackReason,
        Terrain3DBakeResult terrain3DResult,
        string path)
    {
        var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);

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
        file.StoreLine($"RequestedVisualTerrainMode: {requestedMode}");
        file.StoreLine($"EffectiveVisualTerrainMode: {effectiveMode}");
        file.StoreLine($"Terrain3DPluginAvailable: {pluginAvailable}");
        file.StoreLine($"TerrainDirectory: {DefaultTerrainDirectory}");
        file.StoreLine($"GeneratedTerrainDirectory: {DefaultGeneratedTerrainDirectory}");
        file.StoreLine($"Terrain3DDataDirectory: {DefaultTerrain3DDataDirectory}");
        file.StoreLine($"Terrain3DReportsDirectory: {DefaultTerrain3DReportsDirectory}");

        if (!string.IsNullOrWhiteSpace(fallbackReason))
        {
            file.StoreLine($"FallbackReason: {fallbackReason}");
        }

        file.StoreLine($"Mode: {effectiveMode}");

        if (terrain3DResult is { Success: true })
        {
            file.StoreLine("Terrain3D:");
            file.StoreLine($"- NodeName: {Terrain3DNodeName}");
            file.StoreLine($"- HeightImageSize: {terrain3DResult.HeightImageSize}");
            file.StoreLine($"- ImportPosition: {terrain3DResult.ImportPosition}");
            file.StoreLine($"- HeightOffset: {terrain3DResult.HeightOffset:0.###}");
            file.StoreLine($"- HeightScale: {terrain3DResult.HeightScale:0.###}");
            file.StoreLine($"- VertexSpacing: {terrain3DResult.VertexSpacing:0.###}");
            file.StoreLine($"- VertexSpacingDetail: {terrain3DResult.VertexSpacingDetail}");
            file.StoreLine($"- ColorImageSize: {terrain3DResult.ColorImageSize}");
            file.StoreLine($"- ColormapMaterialConfigured: {terrain3DResult.ColormapMaterialConfigured}");
            file.StoreLine($"- RegionCount: {terrain3DResult.RegionCount}");
            file.StoreLine($"- SavedFileCount: {terrain3DResult.SavedFileCount}");
            file.StoreLine($"- SaveResult: {terrain3DResult.SaveResult}");
            file.StoreLine($"- MaxHeightSampleError: {terrain3DResult.MaxSampleError:0.###}");
            file.StoreLine("ColorLayerCoverage:");
            file.StoreLine(terrain3DResult.ColorLayerCoverageReport);
            file.StoreLine("HeightSamples:");
            file.StoreLine(terrain3DResult.HeightSampleReport);
        }

        file.Close();
    }

    private sealed record Terrain3DBakeResult(
        bool Success,
        string FailureReason,
        Vector2I HeightImageSize,
        Vector3 ImportPosition,
        float HeightOffset,
        float HeightScale,
        float VertexSpacing,
        string VertexSpacingDetail,
        Vector2I ColorImageSize,
        bool ColormapMaterialConfigured,
        string ColorLayerCoverageReport,
        int RegionCount,
        int SavedFileCount,
        float MaxSampleError,
        string SaveResult,
        string HeightSampleReport)
    {
        public static Terrain3DBakeResult Fail(string reason)
        {
            return new Terrain3DBakeResult(
                false,
                reason,
                Vector2I.Zero,
                Vector3.Zero,
                0.0f,
                0.0f,
                0.0f,
                string.Empty,
                Vector2I.Zero,
                false,
                string.Empty,
                0,
                0,
                float.PositiveInfinity,
                string.Empty,
                string.Empty);
        }
    }

    private sealed record TerrainColorBakeResult(Image Image, string CoverageReport);

    private const int TerrainColorLayerCount = 10;

    private enum TerrainColorLayer
    {
        Ocean = 0,
        Coast = 1,
        Grassland = 2,
        Forest = 3,
        Desert = 4,
        Tundra = 5,
        Hills = 6,
        Mountain = 7,
        Rock = 8,
        Riverbank = 9
    }
}
