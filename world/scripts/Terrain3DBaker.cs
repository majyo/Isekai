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
    private const string Terrain3DAssetsClassName = "Terrain3DAssets";
    private const string Terrain3DTextureAssetClassName = "Terrain3DTextureAsset";
    private const string WaterPreviewNodeName = "water_preview_plane";
    private const float HeightSampleTolerance = 8.0f;
    private const float RockSlopeStart = 0.64f;
    private const float RockSlopeFull = 0.94f;
    private const float RiverWetnessStart = 0.02f;
    private const int TerrainLayerTextureSize = 256;
    private const float Tau = 6.283185307179586f;

    public const string DefaultBakeReportPath = "res://world/generated/visual_terrain_bake_report.txt";
    public const string DefaultTerrainDirectory = "res://world/terrain";
    public const string DefaultGeneratedTerrainDirectory = "res://world/terrain/generated";
    public const string DefaultTerrain3DDataDirectory = "res://world/terrain/generated/terrain3d_data";
    public const string DefaultTerrain3DReportsDirectory = "res://world/terrain/generated/reports";
    public const string DefaultTerrain3DTextureDirectory = "res://world/terrain/generated/textures";

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
        var debugPreviewReason = string.Empty;
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
                    debugPreviewReason = terrain3DResult.FailureReason;
                    ClearTerrainRoot(terrainRoot);
                    WorldMapDebugLogger.Warn($"Terrain3D bake failed. Using ArrayMesh debug preview: {debugPreviewReason}");
                }
            }
            else
            {
                debugPreviewReason = "Terrain3D GDExtension classes are not available.";
                WorldMapDebugLogger.Warn("Terrain3D mode requested, but plugin classes are not available. Using ArrayMesh debug preview.");
            }
        }
        else if (pluginAvailable)
        {
            WorldMapDebugLogger.LogSystem("ArrayMesh debug preview mode selected. Terrain3D plugin is available for visual terrain bakes.");
        }

        if (effectiveMode == VisualTerrainMode.ArrayMeshPreview)
        {
            var previewMesh = BuildPreviewMesh(infoMap, config);
            terrainRoot.AddChild(previewMesh);
            previewMesh.Owner = terrainRoot.Owner;

            WorldMapDebugLogger.LogBakeStep($"Built ArrayMesh debug preview at grid {config.VisualTerrainGridSize}.", config);
        }

        var waterPlane = BuildWaterPlane(config);
        terrainRoot.AddChild(waterPlane);
        waterPlane.Owner = terrainRoot.Owner;

        SaveReport(config, pluginAvailable, requestedMode, effectiveMode, debugPreviewReason, terrain3DResult, reportPath);
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
            var typeControl = GetTerrainRegionConstant("TYPE_CONTROL", 1);
            var typeColor = GetTerrainRegionConstant("TYPE_COLOR", 2);
            var typeMax = GetTerrainRegionConstant("TYPE_MAX", 3);
            var heightRange = CalculateHeightRange(infoMap);
            var heightScale = MathF.Max(0.0001f, heightRange.Y - heightRange.X);
            var heightOffset = heightRange.X;
            var heightImage = BuildTerrain3DHeightImage(infoMap, heightOffset, heightScale);
            var colorBake = BuildTerrain3DColorImage(infoMap, config);
            var controlImage = BuildTerrain3DControlImage(colorBake.LayerIds, infoMap.Size);
            var textureBake = BuildTerrain3DTextureAssets();
            var importPosition = BuildTerrain3DImportPosition(config);
            var materialConfigured = ConfigureTerrain3DMaterial(terrain, textureBake.Configured);

            if (textureBake.Configured)
            {
                terrain.Call("set_assets", Variant.From(textureBake.Assets));
                textureBake.Assets.Call("update_texture_list");
            }

            var images = new GodotArray();
            images.Resize(typeMax);
            images[typeHeight] = Variant.From(heightImage);
            images[typeControl] = Variant.From(controlImage);
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
                new Vector2I(controlImage.GetWidth(), controlImage.GetHeight()),
                materialConfigured,
                textureBake.Configured,
                textureBake.TextureCount,
                textureBake.TextureDirectory,
                textureBake.Report,
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
        var layerIds = new byte[infoMap.CellCount];
        var counts = new int[TerrainColorLayerCount];

        for (var y = 0; y < infoMap.Size.Y; y++)
        {
            for (var x = 0; x < infoMap.Size.X; x++)
            {
                var layer = GetBaseColorLayer(infoMap.GetBiome(x, y));
                var color = GetBaseColor(layer, infoMap.GetHeight(x, y), config);

                var slope = CalculateSlope01(infoMap, x, y, config);
                var rockWeight = CalculateRockWeight(infoMap.GetHeight(x, y), config, slope, layer);

                if (rockWeight > 0.0f)
                {
                    color = color.Lerp(new Color(0.46f, 0.46f, 0.43f), rockWeight);

                    if (rockWeight >= 0.58f && (layer == TerrainColorLayer.Mountain || layer == TerrainColorLayer.Hills))
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

                layerIds[infoMap.GetIndex(x, y)] = (byte)layer;
                counts[(int)layer]++;
                image.SetPixel(x, y, color);
            }
        }

        return new TerrainColorBakeResult(image, layerIds, BuildColorCoverageReport(counts, infoMap.CellCount));
    }

    private static Image BuildTerrain3DControlImage(byte[] layerIds, Vector2I size)
    {
        var image = Image.CreateEmpty(size.X, size.Y, false, Image.Format.Rf);

        for (var y = 0; y < size.Y; y++)
        {
            for (var x = 0; x < size.X; x++)
            {
                var layerId = layerIds[y * size.X + x];
                image.SetPixel(x, y, new Color(EncodeTerrain3DControlFloat(layerId), 0.0f, 0.0f, 1.0f));
            }
        }

        return image;
    }

    private static float EncodeTerrain3DControlFloat(int baseTextureId)
    {
        var control = ((uint)baseTextureId & 0x1Fu) << 27;
        return BitConverter.UInt32BitsToSingle(control);
    }

    private static TerrainTextureBakeResult BuildTerrain3DTextureAssets()
    {
        if (!ClassDB.ClassExists(Terrain3DAssetsClassName) || !ClassDB.ClassExists(Terrain3DTextureAssetClassName))
        {
            return TerrainTextureBakeResult.Fail("Terrain3D asset classes are not available.");
        }

        var assets = ClassDB.Instantiate(Terrain3DAssetsClassName).AsGodotObject();

        if (assets == null)
        {
            return TerrainTextureBakeResult.Fail("ClassDB did not return a Terrain3DAssets resource.");
        }

        var textureDirectory = ProjectSettings.GlobalizePath(DefaultTerrain3DTextureDirectory);
        Directory.CreateDirectory(textureDirectory);
        ClearGeneratedTerrainTextures(textureDirectory);

        var report = new StringBuilder();
        var textureCount = 0;

        for (var id = 0; id < TerrainColorLayerCount; id++)
        {
            var layer = (TerrainColorLayer)id;
            var spec = GetLayerTextureSpec(layer);
            var image = BuildLayerAlbedoTexture(layer, spec, TerrainLayerTextureSize);
            var normalImage = BuildLayerNormalRoughnessTexture(image, spec);
            image.GenerateMipmaps();
            normalImage.GenerateMipmaps();

            var filePath = Path.Combine(textureDirectory, $"{layer.ToString().ToLowerInvariant()}_albedo.png");
            var saveResult = image.SavePng(filePath);

            if (saveResult != Error.Ok)
            {
                return TerrainTextureBakeResult.Fail($"Failed to save generated texture '{filePath}': {saveResult}.");
            }

            var texture = ImageTexture.CreateFromImage(image);
            var normalTexture = ImageTexture.CreateFromImage(normalImage);
            var textureAsset = ClassDB.Instantiate(Terrain3DTextureAssetClassName).AsGodotObject();

            if (textureAsset == null)
            {
                return TerrainTextureBakeResult.Fail($"ClassDB did not return a Terrain3DTextureAsset for layer {layer}.");
            }

            textureAsset.Set("id", Variant.From(id));
            TryCall(textureAsset, "set_name", Variant.From(layer.ToString()));
            TryCall(textureAsset, "set_albedo_texture", Variant.From(texture));
            TryCall(textureAsset, "set_albedo_color", Variant.From(Colors.White));
            TryCall(textureAsset, "set_normal_texture", Variant.From(normalTexture));
            TryCall(textureAsset, "set_normal_depth", Variant.From(spec.NormalDepth));
            TryCall(textureAsset, "set_uv_scale", Variant.From(spec.UvScale));
            TrySet(textureAsset, "uv_scale", Variant.From(spec.UvScale));
            assets.Call("set_texture", Variant.From(id), Variant.From(textureAsset));

            textureCount++;
            report.AppendLine($"- {layer}: slot={id}, uv_scale={spec.UvScale:0.##}, texture={DefaultTerrain3DTextureDirectory}/{Path.GetFileName(filePath)}");
        }

        assets.Call("update_texture_list");

        return new TerrainTextureBakeResult(
            true,
            string.Empty,
            assets,
            textureCount,
            DefaultTerrain3DTextureDirectory,
            report.ToString().TrimEnd());
    }

    private static void ClearGeneratedTerrainTextures(string textureDirectory)
    {
        foreach (var file in Directory.GetFiles(textureDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            var extension = Path.GetExtension(file);

            if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".import", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
            }
        }
    }

    private static Image BuildLayerAlbedoTexture(TerrainColorLayer layer, TerrainLayerTextureSpec spec, int size)
    {
        var image = Image.CreateEmpty(size, size, true, Image.Format.Rgba8);

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var u = x / (float)size;
                var v = y / (float)size;
                var broad = FractalTileNoise(u, v, spec.Seed, spec.BaseCells, 4);
                var fine = FractalTileNoise(u, v, spec.Seed + 37, spec.BaseCells * 4, 3);
                var grain = TileValueNoise(u, v, spec.Seed + 131, 96);
                var bands = 0.5f + MathF.Sin((u * spec.BandFrequency.X + v * spec.BandFrequency.Y + broad * spec.BandWarp) * Tau) * 0.5f;
                var mix = Mathf.Clamp(0.5f + (broad - 0.5f) * spec.Contrast + (fine - 0.5f) * 0.34f, 0.0f, 1.0f);
                var color = MixThree(spec.ShadowColor, spec.BaseColor, spec.HighlightColor, mix);
                var detail = Mathf.Clamp((grain - 0.5f) * spec.GrainStrength + (bands - 0.5f) * spec.BandStrength, -0.38f, 0.38f);

                color = ApplyLayerTextureDetail(layer, color, broad, fine, grain, bands, u, v, detail);
                color.A = Mathf.Clamp(0.55f + (broad - 0.5f) * spec.HeightStrength + (fine - 0.5f) * 0.24f + detail * 0.32f, 0.08f, 0.96f);

                image.SetPixel(x, y, color);
            }
        }

        return image;
    }

    private static Image BuildLayerNormalRoughnessTexture(Image albedoHeight, TerrainLayerTextureSpec spec)
    {
        var size = albedoHeight.GetWidth();
        var image = Image.CreateEmpty(size, size, true, Image.Format.Rgba8);
        var strength = spec.NormalStrength;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var left = SampleAlphaWrapped(albedoHeight, x - 1, y);
                var right = SampleAlphaWrapped(albedoHeight, x + 1, y);
                var down = SampleAlphaWrapped(albedoHeight, x, y - 1);
                var up = SampleAlphaWrapped(albedoHeight, x, y + 1);
                var normal = new Vector3((left - right) * strength, 1.0f, (down - up) * strength).Normalized();

                image.SetPixel(
                    x,
                    y,
                    new Color(
                        normal.X * 0.5f + 0.5f,
                        normal.Z * 0.5f + 0.5f,
                        normal.Y * 0.5f + 0.5f,
                        spec.Roughness));
            }
        }

        return image;
    }

    private static float SampleAlphaWrapped(Image image, int x, int y)
    {
        var width = image.GetWidth();
        var height = image.GetHeight();
        return image.GetPixel(PositiveMod(x, width), PositiveMod(y, height)).A;
    }

    private static Color ApplyLayerTextureDetail(
        TerrainColorLayer layer,
        Color color,
        float broad,
        float fine,
        float grain,
        float bands,
        float u,
        float v,
        float detail)
    {
        switch (layer)
        {
            case TerrainColorLayer.Ocean:
            {
                var ripple = 0.5f + MathF.Sin((u * 19.0f + v * 31.0f + fine * 0.18f) * Tau) * 0.5f;
                return Lighten(color.Lerp(new Color(0.03f, 0.12f, 0.28f), (1.0f - broad) * 0.18f), ripple * 0.08f);
            }
            case TerrainColorLayer.Coast:
                return Lighten(color, (grain - 0.5f) * 0.16f + bands * 0.06f);
            case TerrainColorLayer.Grassland:
            {
                var grassLines = 0.5f + MathF.Sin((u * 43.0f - v * 7.0f + fine * 0.2f) * Tau) * 0.5f;
                return color.Lerp(new Color(0.23f, 0.47f, 0.16f), grassLines * 0.16f + MathF.Max(0.0f, detail) * 0.12f);
            }
            case TerrainColorLayer.Forest:
            {
                var canopy = Mathf.SmoothStep(0.36f, 0.82f, broad);
                return color.Lerp(new Color(0.05f, 0.22f, 0.10f), canopy * 0.36f).Lerp(new Color(0.18f, 0.38f, 0.17f), fine * 0.11f);
            }
            case TerrainColorLayer.Desert:
                return Lighten(color.Lerp(new Color(0.82f, 0.66f, 0.32f), bands * 0.22f), (grain - 0.5f) * 0.10f);
            case TerrainColorLayer.Tundra:
                return color.Lerp(new Color(0.76f, 0.82f, 0.78f), MathF.Max(fine - 0.4f, 0.0f) * 0.35f);
            case TerrainColorLayer.Hills:
                return Lighten(color.Lerp(new Color(0.31f, 0.28f, 0.18f), bands * 0.24f), detail * 0.12f);
            case TerrainColorLayer.Mountain:
                return color.Lerp(new Color(0.72f, 0.71f, 0.68f), MathF.Max(bands - 0.42f, 0.0f) * 0.34f).Lerp(new Color(0.34f, 0.34f, 0.32f), (1.0f - fine) * 0.16f);
            case TerrainColorLayer.Rock:
                return color.Lerp(new Color(0.27f, 0.27f, 0.25f), bands * 0.34f).Lerp(new Color(0.58f, 0.57f, 0.53f), grain * 0.14f);
            case TerrainColorLayer.Riverbank:
                return color.Lerp(new Color(0.10f, 0.14f, 0.10f), broad * 0.24f).Lerp(new Color(0.27f, 0.25f, 0.16f), grain * 0.12f);
            default:
                return Lighten(color, detail * 0.1f);
        }
    }

    private static TerrainLayerTextureSpec GetLayerTextureSpec(TerrainColorLayer layer)
    {
        return layer switch
        {
            TerrainColorLayer.Ocean => new TerrainLayerTextureSpec(new Color(0.08f, 0.30f, 0.52f), new Color(0.03f, 0.11f, 0.26f), new Color(0.15f, 0.43f, 0.66f), 101, 5, 0.58f, 0.30f, 0.18f, 0.12f, new Vector2(4.0f, 9.0f), 0.22f, 0.22f, 0.18f, 2.0f, 0.58f),
            TerrainColorLayer.Coast => new TerrainLayerTextureSpec(new Color(0.74f, 0.66f, 0.41f), new Color(0.52f, 0.44f, 0.25f), new Color(0.91f, 0.82f, 0.55f), 211, 9, 0.48f, 0.26f, 0.42f, 0.08f, new Vector2(11.0f, 3.0f), 0.12f, 0.55f, 0.34f, 4.0f, 0.82f),
            TerrainColorLayer.Grassland => new TerrainLayerTextureSpec(new Color(0.31f, 0.54f, 0.21f), new Color(0.18f, 0.34f, 0.12f), new Color(0.47f, 0.66f, 0.30f), 307, 10, 0.62f, 0.34f, 0.28f, 0.08f, new Vector2(17.0f, 5.0f), 0.10f, 0.38f, 0.50f, 5.0f, 0.88f),
            TerrainColorLayer.Forest => new TerrainLayerTextureSpec(new Color(0.10f, 0.30f, 0.13f), new Color(0.04f, 0.14f, 0.06f), new Color(0.20f, 0.44f, 0.20f), 409, 8, 0.82f, 0.42f, 0.36f, 0.06f, new Vector2(8.0f, 12.0f), 0.14f, 0.32f, 0.46f, 4.5f, 0.94f),
            TerrainColorLayer.Desert => new TerrainLayerTextureSpec(new Color(0.72f, 0.56f, 0.28f), new Color(0.52f, 0.38f, 0.18f), new Color(0.91f, 0.73f, 0.39f), 503, 7, 0.45f, 0.22f, 0.40f, 0.24f, new Vector2(7.0f, 15.0f), 0.18f, 0.45f, 0.30f, 3.8f, 0.78f),
            TerrainColorLayer.Tundra => new TerrainLayerTextureSpec(new Color(0.61f, 0.69f, 0.67f), new Color(0.42f, 0.50f, 0.49f), new Color(0.80f, 0.84f, 0.78f), 607, 8, 0.52f, 0.24f, 0.24f, 0.06f, new Vector2(13.0f, 6.0f), 0.09f, 0.40f, 0.32f, 3.4f, 0.86f),
            TerrainColorLayer.Hills => new TerrainLayerTextureSpec(new Color(0.42f, 0.39f, 0.25f), new Color(0.25f, 0.23f, 0.15f), new Color(0.58f, 0.53f, 0.32f), 701, 7, 0.70f, 0.46f, 0.26f, 0.22f, new Vector2(10.0f, 18.0f), 0.24f, 0.34f, 0.72f, 6.0f, 0.9f),
            TerrainColorLayer.Mountain => new TerrainLayerTextureSpec(new Color(0.50f, 0.49f, 0.46f), new Color(0.30f, 0.30f, 0.29f), new Color(0.72f, 0.71f, 0.66f), 809, 6, 0.78f, 0.52f, 0.20f, 0.36f, new Vector2(16.0f, 22.0f), 0.32f, 0.28f, 0.92f, 7.0f, 0.95f),
            TerrainColorLayer.Rock => new TerrainLayerTextureSpec(new Color(0.45f, 0.45f, 0.42f), new Color(0.25f, 0.25f, 0.23f), new Color(0.62f, 0.61f, 0.56f), 907, 6, 0.88f, 0.62f, 0.22f, 0.46f, new Vector2(20.0f, 27.0f), 0.38f, 0.42f, 1.0f, 8.0f, 0.96f),
            TerrainColorLayer.Riverbank => new TerrainLayerTextureSpec(new Color(0.18f, 0.23f, 0.15f), new Color(0.08f, 0.11f, 0.08f), new Color(0.32f, 0.30f, 0.19f), 1009, 11, 0.56f, 0.36f, 0.36f, 0.08f, new Vector2(14.0f, 4.0f), 0.16f, 0.50f, 0.45f, 4.5f, 0.92f),
            _ => new TerrainLayerTextureSpec(new Color(0.35f, 0.48f, 0.28f), new Color(0.22f, 0.30f, 0.16f), new Color(0.52f, 0.62f, 0.34f), 1, 8, 0.5f, 0.3f, 0.25f, 0.1f, new Vector2(8.0f, 8.0f), 0.12f, 0.5f, 0.5f, 4.0f, 0.9f)
        };
    }

    private static float FractalTileNoise(float u, float v, int seed, int baseCells, int octaveCount)
    {
        var value = 0.0f;
        var amplitude = 1.0f;
        var amplitudeSum = 0.0f;
        var cells = Math.Max(1, baseCells);

        for (var octave = 0; octave < octaveCount; octave++)
        {
            value += TileValueNoise(u, v, seed + octave * 977, cells) * amplitude;
            amplitudeSum += amplitude;
            amplitude *= 0.52f;
            cells *= 2;
        }

        return value / MathF.Max(0.0001f, amplitudeSum);
    }

    private static float TileValueNoise(float u, float v, int seed, int cells)
    {
        u -= MathF.Floor(u);
        v -= MathF.Floor(v);

        var px = u * cells;
        var py = v * cells;
        var x0 = (int)MathF.Floor(px);
        var y0 = (int)MathF.Floor(py);
        var tx = Smooth(px - x0);
        var ty = Smooth(py - y0);
        var x1 = PositiveMod(x0 + 1, cells);
        var y1 = PositiveMod(y0 + 1, cells);

        x0 = PositiveMod(x0, cells);
        y0 = PositiveMod(y0, cells);

        var a = Hash01(x0, y0, seed);
        var b = Hash01(x1, y0, seed);
        var c = Hash01(x0, y1, seed);
        var d = Hash01(x1, y1, seed);

        return LerpFloat(LerpFloat(a, b, tx), LerpFloat(c, d, tx), ty);
    }

    private static float Hash01(int x, int y, int seed)
    {
        unchecked
        {
            var value = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            value = (value ^ (value >> 13)) * 1274126177u;
            value ^= value >> 16;
            return (value & 0x00FFFFFF) / 16777215.0f;
        }
    }

    private static int PositiveMod(int value, int modulus)
    {
        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static float Smooth(float value)
    {
        return value * value * (3.0f - 2.0f * value);
    }

    private static float LerpFloat(float a, float b, float weight)
    {
        return a + (b - a) * weight;
    }

    private static Color MixThree(Color shadow, Color mid, Color highlight, float weight)
    {
        return weight < 0.5f
            ? shadow.Lerp(mid, weight * 2.0f)
            : mid.Lerp(highlight, (weight - 0.5f) * 2.0f);
    }

    private static Color Lighten(Color color, float amount)
    {
        var factor = 1.0f + amount;
        return new Color(
            Mathf.Clamp(color.R * factor, 0.0f, 1.0f),
            Mathf.Clamp(color.G * factor, 0.0f, 1.0f),
            Mathf.Clamp(color.B * factor, 0.0f, 1.0f),
            color.A);
    }

    private static bool TryCall(GodotObject target, string method, params Variant[] arguments)
    {
        try
        {
            target.Call(method, arguments);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySet(GodotObject target, string property, Variant value)
    {
        try
        {
            target.Set(property, value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ConfigureTerrain3DMaterial(GodotObject terrain, bool enableTexturing)
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
        TrySet(terrain, "show_checkered", Variant.From(false));
        TrySet(terrain, "show_colormap", Variant.From(true));
        SetTerrain3DMaterialShaderParameter(material, "enable_texturing", Variant.From(enableTexturing));
        SetTerrain3DMaterialShaderParameter(material, "blend_sharpness", Variant.From(0.72f));
        SetTerrain3DMaterialShaderParameter(material, "height_blending", Variant.From(true));
        SetTerrain3DMaterialShaderParameter(material, "enable_macro_variation", Variant.From(true));
        TryCall(material, "update");
        return true;
    }

    private static void SetTerrain3DMaterialShaderParameter(GodotObject material, string parameter, Variant value)
    {
        TrySet(material, parameter, value);
        TryCall(material, "set_shader_param", Variant.From(parameter), value);

        try
        {
            var parameters = material.Get("_shader_parameters").AsGodotDictionary();
            parameters[parameter] = value;
            material.Set("_shader_parameters", Variant.From(parameters));
        }
        catch
        {
            // Terrain3DMaterial keeps shader uniforms in an internal dictionary; direct setters above cover older APIs.
        }
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

    private static float CalculateRockWeight(float height, WorldMapConfig config, float slope, TerrainColorLayer layer)
    {
        if (height <= config.SeaLevel)
        {
            return 0.0f;
        }

        var slopeWeight = Mathf.Clamp((slope - RockSlopeStart) / (RockSlopeFull - RockSlopeStart), 0.0f, 1.0f);
        var elevation01 = Mathf.Clamp((height - config.SeaLevel) / MathF.Max(1.0f, config.MaxHeight - config.SeaLevel), 0.0f, 1.0f);
        var elevationWeight = Mathf.Clamp((elevation01 - 0.72f) / 0.22f, 0.0f, 1.0f);

        return layer switch
        {
            TerrainColorLayer.Mountain => Mathf.Clamp(0.28f + MathF.Max(slopeWeight * 0.42f, elevationWeight * 0.54f), 0.28f, 0.82f),
            TerrainColorLayer.Hills => Mathf.Clamp(MathF.Max(slopeWeight * 0.28f, elevationWeight * 0.20f), 0.0f, 0.42f),
            TerrainColorLayer.Coast => slopeWeight * 0.06f,
            _ => slopeWeight * 0.12f
        };
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
        string debugPreviewReason,
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
        file.StoreLine($"Terrain3DTextureDirectory: {DefaultTerrain3DTextureDirectory}");

        if (!string.IsNullOrWhiteSpace(debugPreviewReason))
        {
            file.StoreLine($"ArrayMeshDebugPreviewReason: {debugPreviewReason}");
        }

        file.StoreLine($"Mode: {effectiveMode}");
        file.StoreLine($"ArrayMeshDebugPreviewActive: {effectiveMode == VisualTerrainMode.ArrayMeshPreview}");

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
            file.StoreLine($"- ControlImageSize: {terrain3DResult.ControlImageSize}");
            file.StoreLine($"- ColormapMaterialConfigured: {terrain3DResult.ColormapMaterialConfigured}");
            file.StoreLine($"- TextureAssetsConfigured: {terrain3DResult.TextureAssetsConfigured}");
            file.StoreLine($"- TextureAssetCount: {terrain3DResult.TextureAssetCount}");
            file.StoreLine($"- TextureDirectory: {terrain3DResult.TextureDirectory}");
            file.StoreLine($"- RegionCount: {terrain3DResult.RegionCount}");
            file.StoreLine($"- SavedFileCount: {terrain3DResult.SavedFileCount}");
            file.StoreLine($"- SaveResult: {terrain3DResult.SaveResult}");
            file.StoreLine($"- MaxHeightSampleError: {terrain3DResult.MaxSampleError:0.###}");
            file.StoreLine("TextureAssets:");
            file.StoreLine(terrain3DResult.TextureAssetReport);
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
        Vector2I ControlImageSize,
        bool ColormapMaterialConfigured,
        bool TextureAssetsConfigured,
        int TextureAssetCount,
        string TextureDirectory,
        string TextureAssetReport,
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
                Vector2I.Zero,
                false,
                false,
                0,
                string.Empty,
                string.Empty,
                string.Empty,
                0,
                0,
                float.PositiveInfinity,
                string.Empty,
                string.Empty);
        }
    }

    private sealed record TerrainColorBakeResult(Image Image, byte[] LayerIds, string CoverageReport);

    private sealed record TerrainTextureBakeResult(
        bool Configured,
        string FailureReason,
        GodotObject Assets,
        int TextureCount,
        string TextureDirectory,
        string Report)
    {
        public static TerrainTextureBakeResult Fail(string reason)
        {
            return new TerrainTextureBakeResult(false, reason, null, 0, DefaultTerrain3DTextureDirectory, $"- Failed: {reason}");
        }
    }

    private sealed record TerrainLayerTextureSpec(
        Color BaseColor,
        Color ShadowColor,
        Color HighlightColor,
        int Seed,
        int BaseCells,
        float Contrast,
        float HeightStrength,
        float GrainStrength,
        float BandStrength,
        Vector2 BandFrequency,
        float BandWarp,
        float UvScale,
        float NormalDepth,
        float NormalStrength,
        float Roughness);

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
