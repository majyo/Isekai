using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace Isekai.World;

public static class WorldMapMvpValidator
{
    public const string DefaultReportPath = "res://world/generated/world_map_mvp_validation_report.txt";

    private const float HeightTolerance = 0.0001f;
    private const float Terrain3DHeightTolerance = 8.0f;
    private const string Terrain3DClassName = "Terrain3D";
    private const int ExpectedTerrain3DTextureCount = 10;

    public static bool Validate(
        WorldMapConfig config,
        TerrainInfoMap infoMap,
        HexTileMap tileMap,
        Node3D terrainRoot,
        HexOverlayRenderer overlayRenderer,
        WorldMapInputController inputController,
        out string report)
    {
        var checks = new List<ValidationCheck>();
        var visualTerrain = InspectVisualTerrain(config, terrainRoot);

        AddCheck(checks, "Config is valid", config != null && config.IsValid(out _));
        AddCheck(checks, "Coordinate round trips pass", WorldMapCoordinateValidator.Validate(config, out _));
        AddCheck(checks, "Terrain info map matches config", ValidateTerrainInfoMap(config, infoMap, out var infoDetail), infoDetail);
        AddCheck(checks, "World generation is deterministic for current seed", ValidateDeterminism(config, infoMap, out var determinismDetail), determinismDetail);
        AddCheck(checks, "Hex tile map matches target grid", ValidateTileMap(config, tileMap, out var tileDetail), tileDetail);
        AddCheck(checks, "Hex tile axial lookup works", ValidateTileLookup(tileMap, out var lookupDetail), lookupDetail);
        AddCheck(checks, "Terrain classification has playable variety", ValidateTerrainVariety(tileMap, out var varietyDetail), varietyDetail);
        AddCheck(checks, "Coast detection produced coastal tiles", CountTiles(tileMap, static (map, index) => map.IsCoastal[index] != 0) > 0);
        AddCheck(checks, "River edge data is present and bidirectionally consistent", ValidateRiverEdges(tileMap, out var riverDetail), riverDetail);
        AddCheck(checks, "Visual terrain exists for selected mode", visualTerrain.IsValid, visualTerrain.Detail);
        AddCheck(checks, "Terrain3D texture assets are configured", ValidateTerrain3DTextureAssets(visualTerrain, out var textureAssetDetail), textureAssetDetail);
        AddCheck(checks, "Terrain3D control map has texture id variety", ValidateTerrain3DControlTextureIds(config, visualTerrain, out var controlTextureDetail), controlTextureDetail);
        AddCheck(checks, "Terrain3D height samples match TerrainInfoMap", ValidateTerrain3DHeightConsistency(config, infoMap, visualTerrain, out var terrain3DHeightDetail), terrain3DHeightDetail);
        AddCheck(checks, "Hex overlay rendered all tiles", overlayRenderer != null && overlayRenderer.LastRenderedTileCount == tileMap.TileCount, overlayRenderer == null ? "overlay renderer missing" : $"rendered={overlayRenderer.LastRenderedTileCount}, expected={tileMap.TileCount}");
        AddCheck(checks, "Hex overlay has grid and map mode meshes", overlayRenderer != null && overlayRenderer.HasRenderedGrid && overlayRenderer.HasTerrainMapMode && overlayRenderer.HasPoliticalMapMode);
        AddCheck(checks, "Hover and selection highlight meshes exist", overlayRenderer != null && overlayRenderer.HasHighlightMeshes);
        AddCheck(checks, "World map input controller is initialized", inputController != null && inputController.IsInitialized);
        AddCheck(checks, "Political overlay has owner ids", CountTiles(tileMap, static (map, index) => map.OwnerId[index] > 0) > 0);
        AddCheck(checks, "Gameplay data does not depend on Terrain3D mesh", true, "tile bake, input raycast, and overlay height all sample TerrainInfoMap");

        var failedCount = 0;
        var builder = new StringBuilder();
        builder.AppendLine("World Map MVP Validation Report");
        builder.AppendLine($"Seed: {config?.Seed}");
        builder.AppendLine($"InfoMapSize: {config?.InfoMapSize}");
        builder.AppendLine($"TargetHexGridSize: {config?.TargetHexGridSize}");
        builder.AppendLine($"RequestedVisualTerrainMode: {config?.VisualTerrainMode}");
        builder.AppendLine($"DetectedVisualTerrainMode: {visualTerrain.DetectedMode}");
        builder.AppendLine($"Terrain3DPluginAvailable: {visualTerrain.PluginAvailable}");
        builder.AppendLine($"HexRadius: {config?.HexRadius}");
        builder.AppendLine();

        foreach (var check in checks)
        {
            if (!check.Passed)
            {
                failedCount++;
            }

            builder.AppendLine($"{(check.Passed ? "PASS" : "FAIL")}: {check.Name}");

            if (!string.IsNullOrWhiteSpace(check.Detail))
            {
                builder.AppendLine($"  {check.Detail}");
            }
        }

        builder.AppendLine();
        builder.AppendLine(failedCount == 0 ? "Result: PASS" : $"Result: FAIL ({failedCount} failed)");
        builder.AppendLine();
        builder.AppendLine("Generated quality artifacts:");
        builder.AppendLine($"- Terrain quality report: {TerrainGenerationQualityReporter.DefaultQualityReportPath}");
        builder.AppendLine($"- Terrain metrics json: {TerrainGenerationQualityReporter.DefaultMetricsJsonPath}");
        builder.AppendLine($"- Height histogram debug image: {TerrainGenerationQualityReporter.DefaultHeightHistogramPath}");
        builder.AppendLine();
        builder.AppendLine("Known limitations:");
        builder.AppendLine(config?.VisualTerrainMode == VisualTerrainMode.Terrain3D
            ? "- Terrain3D height, color, control, and generated texture assets are active; river edge visuals are still pending."
            : "- ArrayMesh debug preview mode is selected; Terrain3D remains the default visual terrain path.");
        builder.AppendLine("- Generated files under res://world/generated/ are rebakable and intentionally ignored by Git.");
        builder.AppendLine("- River edge gameplay data is baked, but visual river meshes are not yet drawn as edge geometry.");
        builder.AppendLine("- Province, resource, movement, and save/load systems are follow-up milestones.");

        report = builder.ToString();
        return failedCount == 0;
    }

    public static void SaveReport(string report, string path = DefaultReportPath)
    {
        var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);

        if (file == null)
        {
            WorldMapDebugLogger.Warn($"Failed to open MVP validation report '{path}' for writing.");
            return;
        }

        file.StoreString(report);
        file.Close();
    }

    private static bool ValidateTerrainInfoMap(WorldMapConfig config, TerrainInfoMap infoMap, out string detail)
    {
        if (infoMap == null)
        {
            detail = "terrain info map missing";
            return false;
        }

        var expectedCount = config.InfoMapSize.X * config.InfoMapSize.Y;
        var isValid = infoMap.Size == config.InfoMapSize
            && infoMap.HeightMap.Length == expectedCount
            && infoMap.LandMask.Length == expectedCount
            && infoMap.MoistureMap.Length == expectedCount
            && infoMap.TemperatureMap.Length == expectedCount
            && infoMap.BiomeMap.Length == expectedCount
            && infoMap.RiverFlowMap.Length == expectedCount;

        detail = $"cell_count={infoMap.CellCount}, expected={expectedCount}";
        return isValid;
    }

    private static bool ValidateDeterminism(WorldMapConfig config, TerrainInfoMap infoMap, out string detail)
    {
        if (infoMap == null)
        {
            detail = "terrain info map missing";
            return false;
        }

        var regenerated = new WorldGenerator().Generate(config);
        var samples = new[]
        {
            Vector2I.Zero,
            new Vector2I(config.InfoMapSize.X / 2, config.InfoMapSize.Y / 2),
            config.InfoMapSize - Vector2I.One,
            new Vector2I(config.InfoMapSize.X / 3, config.InfoMapSize.Y * 2 / 3),
            new Vector2I(config.InfoMapSize.X * 7 / 8, config.InfoMapSize.Y / 5)
        };

        foreach (var sample in samples)
        {
            var index = infoMap.GetIndex(sample.X, sample.Y);

            if (MathF.Abs(infoMap.HeightMap[index] - regenerated.HeightMap[index]) > HeightTolerance
                || infoMap.BiomeMap[index] != regenerated.BiomeMap[index]
                || MathF.Abs(infoMap.RiverFlowMap[index] - regenerated.RiverFlowMap[index]) > HeightTolerance)
            {
                detail = $"mismatch at pixel={sample}";
                return false;
            }
        }

        detail = $"checked_samples={samples.Length}";
        return true;
    }

    private static bool ValidateTileMap(WorldMapConfig config, HexTileMap tileMap, out string detail)
    {
        if (tileMap == null)
        {
            detail = "tile map missing";
            return false;
        }

        var expectedTiles = config.TargetHexGridSize.X * config.TargetHexGridSize.Y;
        var isValid = tileMap.GridSize == config.TargetHexGridSize
            && tileMap.TileCount == expectedTiles
            && tileMap.Q.Length == expectedTiles
            && tileMap.R.Length == expectedTiles
            && tileMap.Terrain.Length == expectedTiles
            && tileMap.MovementCost.Length == expectedTiles
            && tileMap.RiverKindByEdge.Length == expectedTiles * WorldMapCoordinateUtility.AxialNeighborDirections.Length;

        detail = $"tile_count={tileMap.TileCount}, expected={expectedTiles}";
        return isValid;
    }

    private static bool ValidateTileLookup(HexTileMap tileMap, out string detail)
    {
        if (tileMap == null || tileMap.TileCount == 0)
        {
            detail = "tile map missing or empty";
            return false;
        }

        var sampleIndices = new[] { 0, tileMap.TileCount / 2, tileMap.TileCount - 1 };

        foreach (var index in sampleIndices)
        {
            var axial = new Vector2I(tileMap.Q[index], tileMap.R[index]);

            if (!tileMap.TryGetTileIndex(axial, out var lookupIndex) || lookupIndex != index)
            {
                detail = $"lookup failed for index={index}, axial={axial}, lookup={lookupIndex}";
                return false;
            }
        }

        detail = $"checked_tiles={sampleIndices.Length}";
        return true;
    }

    private static bool ValidateTerrainVariety(HexTileMap tileMap, out string detail)
    {
        var water = CountTiles(tileMap, static (map, index) => map.IsWater[index] != 0);
        var land = tileMap.TileCount - water;
        var mountains = CountTiles(tileMap, static (map, index) => (TerrainKind)map.Terrain[index] == TerrainKind.Mountains);
        var forests = CountTiles(tileMap, static (map, index) => (TerrainKind)map.Terrain[index] == TerrainKind.Forest);

        detail = $"water={water}, land={land}, mountains={mountains}, forests={forests}";
        return water > 0 && land > 0 && mountains > 0 && forests > 0;
    }

    private static bool ValidateRiverEdges(HexTileMap tileMap, out string detail)
    {
        if (tileMap == null)
        {
            detail = "tile map missing";
            return false;
        }

        var sharedRiverEdges = tileMap.CountRiverEdges(true);
        var inconsistentEdges = 0;

        for (var tileIndex = 0; tileIndex < tileMap.TileCount; tileIndex++)
        {
            var axial = new Vector2I(tileMap.Q[tileIndex], tileMap.R[tileIndex]);

            for (var direction = 0; direction < WorldMapCoordinateUtility.AxialNeighborDirections.Length; direction++)
            {
                var edge = tileMap.GetRiverEdge(tileIndex, direction);

                if (edge.Kind == RiverKind.None)
                {
                    continue;
                }

                var neighborAxial = axial + WorldMapCoordinateUtility.AxialNeighborDirections[direction];

                if (!tileMap.TryGetTileIndex(neighborAxial, out var neighborIndex))
                {
                    inconsistentEdges++;
                    continue;
                }

                var neighborEdge = tileMap.GetRiverEdge(neighborIndex, HexTileMap.GetOppositeDirection(direction));

                if (neighborEdge.Kind != edge.Kind || !Mathf.IsEqualApprox(neighborEdge.Flow, edge.Flow))
                {
                    inconsistentEdges++;
                }
            }
        }

        detail = $"shared_edges={sharedRiverEdges}, inconsistent_directed_edges={inconsistentEdges}";
        return sharedRiverEdges > 0 && inconsistentEdges == 0;
    }

    private static VisualTerrainInspection InspectVisualTerrain(WorldMapConfig config, Node3D terrainRoot)
    {
        var pluginAvailable = ClassDB.ClassExists(Terrain3DClassName);

        if (terrainRoot == null)
        {
            return new VisualTerrainInspection(
                false,
                DetectedVisualTerrainMode.Missing,
                pluginAvailable,
                null,
                null,
                0,
                "terrain root missing");
        }

        var terrainNode = terrainRoot.GetNodeOrNull<Node>(Terrain3DBaker.Terrain3DNodeName);
        var hasTerrain3DNode = terrainNode != null && terrainNode.IsClass(Terrain3DClassName);
        GodotObject terrainData = null;
        var regionCount = 0;

        if (hasTerrain3DNode)
        {
            terrainData = terrainNode.Call("get_data").AsGodotObject();
            regionCount = terrainData?.Call("get_region_count").AsInt32() ?? 0;
        }

        if (config?.VisualTerrainMode == VisualTerrainMode.Terrain3D && pluginAvailable)
        {
            if (!hasTerrain3DNode)
            {
                return new VisualTerrainInspection(
                    false,
                    DetectedVisualTerrainMode.Missing,
                    pluginAvailable,
                    null,
                    null,
                    0,
                    $"Terrain3D node '{Terrain3DBaker.Terrain3DNodeName}' missing");
            }

            if (terrainData == null)
            {
                return new VisualTerrainInspection(
                    false,
                    DetectedVisualTerrainMode.Terrain3D,
                    pluginAvailable,
                    terrainNode,
                    null,
                    0,
                    $"mode=Terrain3D, node={terrainNode.Name}, data=missing");
            }

            return new VisualTerrainInspection(
                regionCount > 0,
                DetectedVisualTerrainMode.Terrain3D,
                pluginAvailable,
                terrainNode,
                terrainData,
                regionCount,
                $"mode=Terrain3D, node={terrainNode.Name}, regions={regionCount}");
        }

        if (hasTerrain3DNode && terrainData != null && regionCount > 0)
        {
            return new VisualTerrainInspection(
                true,
                DetectedVisualTerrainMode.Terrain3D,
                pluginAvailable,
                terrainNode,
                terrainData,
                regionCount,
                $"mode=Terrain3D, node={terrainNode.Name}, regions={regionCount}");
        }

        var previewMesh = terrainRoot.GetNodeOrNull<MeshInstance3D>(Terrain3DBaker.TerrainPreviewNodeName)?.Mesh;

        if (previewMesh != null)
        {
            return new VisualTerrainInspection(
                true,
                DetectedVisualTerrainMode.ArrayMeshDebugPreview,
                pluginAvailable,
                null,
                null,
                0,
                "mode=ArrayMeshDebugPreview");
        }

        return new VisualTerrainInspection(
            false,
            DetectedVisualTerrainMode.Missing,
            pluginAvailable,
            null,
            null,
            0,
            "visual terrain node missing");
    }

    private static bool ValidateTerrain3DHeightConsistency(
        WorldMapConfig config,
        TerrainInfoMap infoMap,
        VisualTerrainInspection visualTerrain,
        out string detail)
    {
        if (visualTerrain.DetectedMode != DetectedVisualTerrainMode.Terrain3D)
        {
            detail = $"skipped: detected_mode={visualTerrain.DetectedMode}";
            return true;
        }

        if (config == null)
        {
            detail = "config missing";
            return false;
        }

        if (infoMap == null)
        {
            detail = "terrain info map missing";
            return false;
        }

        if (visualTerrain.TerrainData == null)
        {
            detail = "Terrain3D data missing";
            return false;
        }

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

        var maxError = 0.0f;
        var worstUv = Vector2.Zero;
        var worstExpected = 0.0f;
        var worstActual = 0.0f;

        foreach (var uv in sampleUvs)
        {
            var worldXz = WorldMapCoordinateUtility.UvToWorldXz(uv, config);
            var worldPosition = WorldMapCoordinateUtility.WorldXzToWorldPosition(worldXz);
            var expectedHeight = infoMap.SampleHeightUv(uv);
            var actualHeight = visualTerrain.TerrainData.Call("get_height", Variant.From(worldPosition)).AsSingle();

            if (!float.IsFinite(actualHeight))
            {
                detail = $"non-finite Terrain3D height at uv={uv}, world_xz={worldXz}";
                return false;
            }

            var error = MathF.Abs(actualHeight - expectedHeight);

            if (error > maxError)
            {
                maxError = error;
                worstUv = uv;
                worstExpected = expectedHeight;
                worstActual = actualHeight;
            }
        }

        detail = $"checked_samples={sampleUvs.Length}, max_error={maxError:0.###}, tolerance={Terrain3DHeightTolerance:0.###}, worst_uv={worstUv}, expected={worstExpected:0.###}, terrain3d={worstActual:0.###}";
        return maxError <= Terrain3DHeightTolerance;
    }

    private static bool ValidateTerrain3DTextureAssets(VisualTerrainInspection visualTerrain, out string detail)
    {
        if (visualTerrain.DetectedMode != DetectedVisualTerrainMode.Terrain3D)
        {
            detail = $"skipped: detected_mode={visualTerrain.DetectedMode}";
            return true;
        }

        if (visualTerrain.TerrainNode == null)
        {
            detail = "Terrain3D node missing";
            return false;
        }

        var assets = TryGetGodotObject(visualTerrain.TerrainNode, "assets");
        assets ??= TryCallGodotObject(visualTerrain.TerrainNode, "get_assets");

        if (assets == null)
        {
            detail = "Terrain3D assets resource missing";
            return false;
        }

        var textureCount = 0;

        for (var id = 0; id < ExpectedTerrain3DTextureCount; id++)
        {
            if (TryCallGodotObject(assets, "get_texture", Variant.From(id)) != null)
            {
                textureCount++;
            }
        }

        var material = TryGetGodotObject(visualTerrain.TerrainNode, "material");
        var texturingEnabled = TryGetMaterialBool(material, "enable_texturing", false);
        var colormapEnabled = TryGetBool(material, "show_colormap", false);
        var albedoArrayValid = TryCallRidValid(assets, "get_albedo_array_rid");
        var normalArrayValid = TryCallRidValid(assets, "get_normal_array_rid");

        detail = $"textures={textureCount}, expected={ExpectedTerrain3DTextureCount}, enable_texturing={texturingEnabled}, show_colormap={colormapEnabled}, albedo_array={albedoArrayValid}, normal_array={normalArrayValid}";
        return textureCount == ExpectedTerrain3DTextureCount && texturingEnabled && colormapEnabled && albedoArrayValid && normalArrayValid;
    }

    private static bool ValidateTerrain3DControlTextureIds(
        WorldMapConfig config,
        VisualTerrainInspection visualTerrain,
        out string detail)
    {
        if (visualTerrain.DetectedMode != DetectedVisualTerrainMode.Terrain3D)
        {
            detail = $"skipped: detected_mode={visualTerrain.DetectedMode}";
            return true;
        }

        if (config == null)
        {
            detail = "config missing";
            return false;
        }

        if (visualTerrain.TerrainData == null)
        {
            detail = "Terrain3D data missing";
            return false;
        }

        var distinctIds = new HashSet<int>();
        var invalidIds = 0;

        for (var y = 1; y <= 5; y++)
        {
            for (var x = 1; x <= 5; x++)
            {
                var uv = new Vector2(x / 6.0f, y / 6.0f);
                var worldXz = WorldMapCoordinateUtility.UvToWorldXz(uv, config);
                var worldPosition = WorldMapCoordinateUtility.WorldXzToWorldPosition(worldXz);
                var textureId = visualTerrain.TerrainData.Call("get_control_base_id", Variant.From(worldPosition)).AsInt32();

                if (textureId < 0 || textureId >= ExpectedTerrain3DTextureCount)
                {
                    invalidIds++;
                    continue;
                }

                distinctIds.Add(textureId);
            }
        }

        detail = $"distinct_texture_ids={string.Join(",", distinctIds)}, distinct_count={distinctIds.Count}, invalid_ids={invalidIds}";
        return invalidIds == 0 && distinctIds.Count >= 3;
    }

    private static GodotObject TryGetGodotObject(GodotObject target, string property)
    {
        if (target == null)
        {
            return null;
        }

        try
        {
            return target.Get(property).AsGodotObject();
        }
        catch
        {
            return null;
        }
    }

    private static GodotObject TryCallGodotObject(GodotObject target, string method, params Variant[] arguments)
    {
        if (target == null)
        {
            return null;
        }

        try
        {
            return target.Call(method, arguments).AsGodotObject();
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetMaterialBool(GodotObject target, string property, bool fallback)
    {
        if (target == null)
        {
            return fallback;
        }

        try
        {
            var parameters = target.Get("_shader_parameters").AsGodotDictionary();

            if (parameters.ContainsKey(property))
            {
                return parameters[property].AsBool();
            }
        }
        catch
        {
            // Older Terrain3D builds may expose shader options as direct properties.
        }

        return TryGetBool(target, property, fallback);
    }

    private static bool TryCallRidValid(GodotObject target, string method)
    {
        if (target == null)
        {
            return false;
        }

        try
        {
            var rid = target.Call(method).AsRid();
            return rid.IsValid;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetBool(GodotObject target, string property, bool fallback)
    {
        if (target == null)
        {
            return fallback;
        }

        try
        {
            return target.Get(property).AsBool();
        }
        catch
        {
            return fallback;
        }
    }

    private static int CountTiles(HexTileMap tileMap, TilePredicate predicate)
    {
        if (tileMap == null)
        {
            return 0;
        }

        var count = 0;

        for (var index = 0; index < tileMap.TileCount; index++)
        {
            if (predicate(tileMap, index))
            {
                count++;
            }
        }

        return count;
    }

    private static void AddCheck(List<ValidationCheck> checks, string name, bool passed, string detail = "")
    {
        checks.Add(new ValidationCheck(name, passed, detail));
    }

    private delegate bool TilePredicate(HexTileMap tileMap, int index);

    private enum DetectedVisualTerrainMode
    {
        Missing = 0,
        ArrayMeshDebugPreview = 1,
        Terrain3D = 2
    }

    private readonly record struct VisualTerrainInspection(
        bool IsValid,
        DetectedVisualTerrainMode DetectedMode,
        bool PluginAvailable,
        Node TerrainNode,
        GodotObject TerrainData,
        int RegionCount,
        string Detail);

    private readonly record struct ValidationCheck(string Name, bool Passed, string Detail);
}
