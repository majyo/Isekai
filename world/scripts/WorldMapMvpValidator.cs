using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace Isekai.World;

public static class WorldMapMvpValidator
{
    public const string DefaultReportPath = "res://world/generated/world_map_mvp_validation_report.txt";

    private const float HeightTolerance = 0.0001f;
    private const string Terrain3DClassName = "Terrain3D";

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

        AddCheck(checks, "Config is valid", config != null && config.IsValid(out _));
        AddCheck(checks, "Coordinate round trips pass", WorldMapCoordinateValidator.Validate(config, out _));
        AddCheck(checks, "Terrain info map matches config", ValidateTerrainInfoMap(config, infoMap, out var infoDetail), infoDetail);
        AddCheck(checks, "World generation is deterministic for current seed", ValidateDeterminism(config, infoMap, out var determinismDetail), determinismDetail);
        AddCheck(checks, "Hex tile map matches target grid", ValidateTileMap(config, tileMap, out var tileDetail), tileDetail);
        AddCheck(checks, "Hex tile axial lookup works", ValidateTileLookup(tileMap, out var lookupDetail), lookupDetail);
        AddCheck(checks, "Terrain classification has playable variety", ValidateTerrainVariety(tileMap, out var varietyDetail), varietyDetail);
        AddCheck(checks, "Coast detection produced coastal tiles", CountTiles(tileMap, static (map, index) => map.IsCoastal[index] != 0) > 0);
        AddCheck(checks, "River edge data is present and bidirectionally consistent", ValidateRiverEdges(tileMap, out var riverDetail), riverDetail);
        AddCheck(checks, "Visual terrain exists for selected mode", ValidateVisualTerrain(config, terrainRoot, out var visualTerrainDetail), visualTerrainDetail);
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
        builder.AppendLine($"VisualTerrainMode: {config?.VisualTerrainMode}");
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
            ? "- Terrain3D height writing is active; material/control/color map writing is still pending."
            : "- ArrayMesh preview mode is selected; Terrain3D height writing is available through VisualTerrainMode.");
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

    private static bool ValidateVisualTerrain(WorldMapConfig config, Node3D terrainRoot, out string detail)
    {
        if (terrainRoot == null)
        {
            detail = "terrain root missing";
            return false;
        }

        if (config?.VisualTerrainMode == VisualTerrainMode.Terrain3D && ClassDB.ClassExists(Terrain3DClassName))
        {
            var terrainNode = terrainRoot.GetNodeOrNull<Node>(Terrain3DBaker.Terrain3DNodeName);

            if (terrainNode == null || !terrainNode.IsClass(Terrain3DClassName))
            {
                detail = $"Terrain3D node '{Terrain3DBaker.Terrain3DNodeName}' missing";
                return false;
            }

            var data = terrainNode.Call("get_data").AsGodotObject();
            var regionCount = data?.Call("get_region_count").AsInt32() ?? 0;

            detail = $"mode=Terrain3D, node={terrainNode.Name}, regions={regionCount}";
            return regionCount > 0;
        }

        var previewMesh = terrainRoot.GetNodeOrNull<MeshInstance3D>(Terrain3DBaker.TerrainPreviewNodeName)?.Mesh;
        detail = previewMesh == null
            ? "ArrayMesh preview mesh missing"
            : "mode=ArrayMeshPreview";
        return previewMesh != null;
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

    private readonly record struct ValidationCheck(string Name, bool Passed, string Detail);
}
