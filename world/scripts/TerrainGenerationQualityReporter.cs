using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Godot;

namespace Isekai.World;

public static class TerrainGenerationQualityReporter
{
    public const string DefaultQualityReportPath = "res://world/generated/terrain_generation_quality_report.txt";
    public const string DefaultMetricsJsonPath = "res://world/generated/terrain_generation_metrics.json";
    public const string DefaultHeightHistogramPath = "res://world/generated/height_histogram_debug.png";

    private const int HeightHistogramBins = 64;
    private const int HeightHistogramImageWidth = 512;
    private const int HeightHistogramImageHeight = 160;

    public static void Export(
        TerrainInfoMap infoMap,
        HexTileMap tileMap,
        WorldMapConfig config,
        IReadOnlyDictionary<string, double> timingsMs = null,
        string reportPath = DefaultQualityReportPath,
        string metricsJsonPath = DefaultMetricsJsonPath,
        string heightHistogramPath = DefaultHeightHistogramPath)
    {
        if (infoMap == null || config == null)
        {
            WorldMapDebugLogger.Warn("Cannot export terrain generation quality report without terrain info map and config.");
            return;
        }

        EnsureParentDirectory(reportPath);
        EnsureParentDirectory(metricsJsonPath);
        EnsureParentDirectory(heightHistogramPath);

        var metrics = BuildMetrics(infoMap, tileMap, config);
        SaveReport(metrics, timingsMs, reportPath);
        SaveMetricsJson(metrics, timingsMs, metricsJsonPath);
        SaveHeightHistogram(metrics.Terrain.HeightHistogram, heightHistogramPath);
    }

    private static TerrainMetrics BuildMetrics(TerrainInfoMap infoMap, HexTileMap tileMap, WorldMapConfig config)
    {
        var terrainMetrics = BuildTerrainInfoMetrics(infoMap, config);
        var tileMetrics = tileMap == null ? TileMetrics.Empty : BuildTileMetrics(tileMap, config);
        var warnings = BuildWarnings(terrainMetrics, tileMetrics);

        return new TerrainMetrics(infoMap, config, terrainMetrics, tileMetrics, warnings);
    }

    private static TerrainInfoMetrics BuildTerrainInfoMetrics(TerrainInfoMap infoMap, WorldMapConfig config)
    {
        var cellCount = infoMap.CellCount;
        var sortedHeights = new float[cellCount];
        var biomeCounts = new int[Enum.GetValues<BiomeKind>().Length];
        var heightHistogramCounts = new int[HeightHistogramBins];

        Array.Copy(infoMap.HeightMap, sortedHeights, cellCount);
        Array.Sort(sortedHeights);

        var minHeight = sortedHeights.Length == 0 ? 0.0f : sortedHeights[0];
        var maxHeight = sortedHeights.Length == 0 ? 0.0f : sortedHeights[^1];
        var heightRange = Math.Max(0.0001f, maxHeight - minHeight);
        var heightSum = 0.0;
        var landPixels = 0;
        var coastPixels = 0;
        var riverPixels = 0;
        var maxRiverFlow = 0.0f;
        var riverFlowSum = 0.0;

        for (var y = 0; y < infoMap.Size.Y; y++)
        {
            for (var x = 0; x < infoMap.Size.X; x++)
            {
                var index = infoMap.GetIndex(x, y);
                var height = infoMap.HeightMap[index];
                heightSum += height;

                var histogramIndex = Math.Clamp((int)((height - minHeight) / heightRange * (HeightHistogramBins - 1)), 0, HeightHistogramBins - 1);
                heightHistogramCounts[histogramIndex]++;

                if (infoMap.LandMask[index] != 0)
                {
                    landPixels++;

                    if (IsAdjacentToWater(infoMap, x, y))
                    {
                        coastPixels++;
                    }
                }

                var biome = infoMap.BiomeMap[index];
                if (biome >= 0 && biome < biomeCounts.Length)
                {
                    biomeCounts[biome]++;
                }

                var riverFlow = infoMap.RiverFlowMap[index];
                if (riverFlow > 0.0f)
                {
                    riverPixels++;
                    riverFlowSum += riverFlow;
                    maxRiverFlow = Math.Max(maxRiverFlow, riverFlow);
                }
            }
        }

        var waterPixels = cellCount - landPixels;
        var averageHeight = cellCount == 0 ? 0.0f : (float)(heightSum / cellCount);
        var averageRiverFlow = riverPixels == 0 ? 0.0f : (float)(riverFlowSum / riverPixels);

        return new TerrainInfoMetrics(
            cellCount,
            landPixels,
            waterPixels,
            coastPixels,
            riverPixels,
            averageHeight,
            minHeight,
            maxHeight,
            GetPercentile(sortedHeights, 0.01f),
            GetPercentile(sortedHeights, 0.05f),
            GetPercentile(sortedHeights, 0.25f),
            GetPercentile(sortedHeights, 0.50f),
            GetPercentile(sortedHeights, 0.75f),
            GetPercentile(sortedHeights, 0.95f),
            GetPercentile(sortedHeights, 0.99f),
            maxRiverFlow,
            averageRiverFlow,
            biomeCounts,
            new HeightHistogram(minHeight, maxHeight, heightHistogramCounts));
    }

    private static TileMetrics BuildTileMetrics(HexTileMap tileMap, WorldMapConfig config)
    {
        var terrainCounts = new int[Enum.GetValues<TerrainKind>().Length];
        var waterTiles = 0;
        var coastalTiles = 0;
        var minCenterX = float.PositiveInfinity;
        var maxCenterX = float.NegativeInfinity;
        var minCenterZ = float.PositiveInfinity;
        var maxCenterZ = float.NegativeInfinity;
        var minCornerX = float.PositiveInfinity;
        var maxCornerX = float.NegativeInfinity;
        var minCornerZ = float.PositiveInfinity;
        var maxCornerZ = float.NegativeInfinity;

        for (var index = 0; index < tileMap.TileCount; index++)
        {
            var terrain = tileMap.Terrain[index];
            if (terrain >= 0 && terrain < terrainCounts.Length)
            {
                terrainCounts[terrain]++;
            }

            if (tileMap.IsWater[index] != 0)
            {
                waterTiles++;
            }

            if (tileMap.IsCoastal[index] != 0)
            {
                coastalTiles++;
            }

            var center = new Vector2(tileMap.WorldCenterX[index], tileMap.WorldCenterZ[index]);
            minCenterX = Math.Min(minCenterX, center.X);
            maxCenterX = Math.Max(maxCenterX, center.X);
            minCenterZ = Math.Min(minCenterZ, center.Y);
            maxCenterZ = Math.Max(maxCenterZ, center.Y);

            for (var corner = 0; corner < 6; corner++)
            {
                var cornerWorldXz = WorldMapCoordinateUtility.GetHexCornerWorldXz(center, tileMap.HexRadius, corner);
                minCornerX = Math.Min(minCornerX, cornerWorldXz.X);
                maxCornerX = Math.Max(maxCornerX, cornerWorldXz.X);
                minCornerZ = Math.Min(minCornerZ, cornerWorldXz.Y);
                maxCornerZ = Math.Max(maxCornerZ, cornerWorldXz.Y);
            }
        }

        var tileCount = tileMap.TileCount;
        var landTiles = tileCount - waterTiles;
        var sharedRiverEdges = tileMap.CountRiverEdges(true);
        var directedRiverEdges = tileMap.CountRiverEdges(false);
        var worldMinX = -config.WorldSize.X * 0.5f;
        var worldMaxX = config.WorldSize.X * 0.5f;
        var worldMinZ = -config.WorldSize.Y * 0.5f;
        var worldMaxZ = config.WorldSize.Y * 0.5f;
        var missingMinX = Math.Max(0.0f, minCornerX - worldMinX);
        var missingMaxX = Math.Max(0.0f, worldMaxX - maxCornerX);
        var missingMinZ = Math.Max(0.0f, minCornerZ - worldMinZ);
        var missingMaxZ = Math.Max(0.0f, worldMaxZ - maxCornerZ);

        return new TileMetrics(
            tileCount,
            landTiles,
            waterTiles,
            coastalTiles,
            sharedRiverEdges,
            directedRiverEdges,
            minCenterX,
            maxCenterX,
            minCenterZ,
            maxCenterZ,
            minCornerX,
            maxCornerX,
            minCornerZ,
            maxCornerZ,
            missingMinX,
            missingMaxX,
            missingMinZ,
            missingMaxZ,
            terrainCounts);
    }

    private static List<string> BuildWarnings(TerrainInfoMetrics terrain, TileMetrics tile)
    {
        var warnings = new List<string>();
        var landRatio = Ratio(terrain.LandPixels, terrain.CellCount);
        var coastRatio = Ratio(terrain.CoastPixels, terrain.CellCount);
        var riverRatio = Ratio(terrain.RiverPixels, terrain.CellCount);

        if (landRatio < 0.25f || landRatio > 0.65f)
        {
            warnings.Add($"land_ratio={FormatPercent(landRatio)} is outside the initial target range 25%..65%");
        }

        if (coastRatio > 0.20f)
        {
            warnings.Add($"coast_pixel_ratio={FormatPercent(coastRatio)} is high; coastlines may be too fragmented");
        }

        if (riverRatio <= 0.0f)
        {
            warnings.Add("river_pixel_count is zero");
        }

        if (tile.TileCount > 0)
        {
            var tileCoastRatio = Ratio(tile.CoastalTiles, tile.TileCount);
            var mountainRatio = Ratio(tile.GetTerrainCount(TerrainKind.Mountains), tile.TileCount);

            if (tileCoastRatio > 0.20f)
            {
                warnings.Add($"coastal_tile_ratio={FormatPercent(tileCoastRatio)} is high; tile coast classification may be noisy");
            }

            if (mountainRatio > 0.25f)
            {
                warnings.Add($"mountain_tile_ratio={FormatPercent(mountainRatio)} is high; movement may be too constrained");
            }

            if (tile.MissingMinX > 0.0f || tile.MissingMaxX > 0.0f || tile.MissingMinZ > 0.0f || tile.MissingMaxZ > 0.0f)
            {
                warnings.Add("hex grid does not fully cover WorldSize");
            }
        }

        return warnings;
    }

    private static void SaveReport(TerrainMetrics metrics, IReadOnlyDictionary<string, double> timingsMs, string path)
    {
        var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);

        if (file == null)
        {
            WorldMapDebugLogger.Warn($"Failed to open terrain generation quality report '{path}' for writing.");
            return;
        }

        var config = metrics.Config;
        var terrain = metrics.Terrain;
        var tile = metrics.Tile;

        file.StoreLine("Terrain Generation Quality Report");
        file.StoreLine($"Seed: {config.Seed}");
        file.StoreLine($"WorldSize: {config.WorldSize}");
        file.StoreLine($"InfoMapSize: {config.InfoMapSize}");
        file.StoreLine($"TargetHexGridSize: {config.TargetHexGridSize}");
        file.StoreLine($"HexRadius: {config.HexRadius:0.###}");
        file.StoreLine($"SeaLevel: {config.SeaLevel:0.###}");
        file.StoreLine($"MaxHeight: {config.MaxHeight:0.###}");
        file.StoreLine(string.Empty);

        file.StoreLine("TimingsMs:");
        if (timingsMs == null || timingsMs.Count == 0)
        {
            file.StoreLine("- none recorded");
        }
        else
        {
            foreach (var timing in timingsMs)
            {
                file.StoreLine($"- {timing.Key}: {timing.Value:0.###}");
            }
        }

        file.StoreLine(string.Empty);
        file.StoreLine("TerrainInfoMap:");
        file.StoreLine($"- CellCount: {terrain.CellCount}");
        file.StoreLine($"- LandPixels: {terrain.LandPixels} ({FormatPercent(Ratio(terrain.LandPixels, terrain.CellCount))})");
        file.StoreLine($"- WaterPixels: {terrain.WaterPixels} ({FormatPercent(Ratio(terrain.WaterPixels, terrain.CellCount))})");
        file.StoreLine($"- CoastPixels: {terrain.CoastPixels} ({FormatPercent(Ratio(terrain.CoastPixels, terrain.CellCount))})");
        file.StoreLine($"- HeightMin: {terrain.MinHeight:0.###}");
        file.StoreLine($"- HeightMax: {terrain.MaxHeight:0.###}");
        file.StoreLine($"- HeightAverage: {terrain.AverageHeight:0.###}");
        file.StoreLine($"- HeightP01: {terrain.HeightP01:0.###}");
        file.StoreLine($"- HeightP05: {terrain.HeightP05:0.###}");
        file.StoreLine($"- HeightP25: {terrain.HeightP25:0.###}");
        file.StoreLine($"- HeightP50: {terrain.HeightP50:0.###}");
        file.StoreLine($"- HeightP75: {terrain.HeightP75:0.###}");
        file.StoreLine($"- HeightP95: {terrain.HeightP95:0.###}");
        file.StoreLine($"- HeightP99: {terrain.HeightP99:0.###}");
        file.StoreLine($"- RiverPixels: {terrain.RiverPixels} ({FormatPercent(Ratio(terrain.RiverPixels, terrain.CellCount))})");
        file.StoreLine($"- RiverFlowMax: {terrain.MaxRiverFlow:0.###}");
        file.StoreLine($"- RiverFlowAverageNonZero: {terrain.AverageRiverFlow:0.###}");
        file.StoreLine("BiomeCounts:");
        WriteEnumCounts(file, terrain.BiomeCounts, static index => ((BiomeKind)index).ToString(), terrain.CellCount);

        file.StoreLine(string.Empty);
        file.StoreLine("HexTileMap:");
        if (tile.TileCount == 0)
        {
            file.StoreLine("- missing");
        }
        else
        {
            file.StoreLine($"- TileCount: {tile.TileCount}");
            file.StoreLine($"- LandTiles: {tile.LandTiles} ({FormatPercent(Ratio(tile.LandTiles, tile.TileCount))})");
            file.StoreLine($"- WaterTiles: {tile.WaterTiles} ({FormatPercent(Ratio(tile.WaterTiles, tile.TileCount))})");
            file.StoreLine($"- CoastalTiles: {tile.CoastalTiles} ({FormatPercent(Ratio(tile.CoastalTiles, tile.TileCount))})");
            file.StoreLine($"- SharedRiverEdges: {tile.SharedRiverEdges}");
            file.StoreLine($"- DirectedRiverEdges: {tile.DirectedRiverEdges}");
            file.StoreLine($"- CenterXRange: {tile.MinCenterX:0.###} .. {tile.MaxCenterX:0.###}");
            file.StoreLine($"- CenterZRange: {tile.MinCenterZ:0.###} .. {tile.MaxCenterZ:0.###}");
            file.StoreLine($"- CornerXRange: {tile.MinCornerX:0.###} .. {tile.MaxCornerX:0.###}");
            file.StoreLine($"- CornerZRange: {tile.MinCornerZ:0.###} .. {tile.MaxCornerZ:0.###}");
            file.StoreLine($"- WorldCoverageGapX: left={tile.MissingMinX:0.###}, right={tile.MissingMaxX:0.###}");
            file.StoreLine($"- WorldCoverageGapZ: top={tile.MissingMinZ:0.###}, bottom={tile.MissingMaxZ:0.###}");
            file.StoreLine("TerrainCounts:");
            WriteEnumCounts(file, tile.TerrainCounts, static index => ((TerrainKind)index).ToString(), tile.TileCount);
        }

        file.StoreLine(string.Empty);
        file.StoreLine("Warnings:");
        if (metrics.Warnings.Count == 0)
        {
            file.StoreLine("- none");
        }
        else
        {
            foreach (var warning in metrics.Warnings)
            {
                file.StoreLine($"- {warning}");
            }
        }

        file.StoreLine(string.Empty);
        file.StoreLine("GeneratedArtifacts:");
        file.StoreLine($"- MetricsJson: {DefaultMetricsJsonPath}");
        file.StoreLine($"- HeightHistogram: {DefaultHeightHistogramPath}");
        file.Close();
    }

    private static void SaveMetricsJson(TerrainMetrics metrics, IReadOnlyDictionary<string, double> timingsMs, string path)
    {
        var builder = new StringBuilder();
        var terrain = metrics.Terrain;
        var tile = metrics.Tile;
        var config = metrics.Config;

        builder.AppendLine("{");
        AppendJsonNumber(builder, "seed", config.Seed, 1);
        AppendJsonString(builder, "world_size", $"{config.WorldSize}", 1);
        AppendJsonString(builder, "info_map_size", $"{config.InfoMapSize}", 1);
        AppendJsonString(builder, "target_hex_grid_size", $"{config.TargetHexGridSize}", 1);
        AppendJsonNumber(builder, "hex_radius", config.HexRadius, 1);
        AppendJsonNumber(builder, "sea_level", config.SeaLevel, 1);
        AppendJsonNumber(builder, "max_height", config.MaxHeight, 1);

        AppendJsonObjectStart(builder, "timings_ms", 1);
        if (timingsMs != null)
        {
            var timingIndex = 0;
            foreach (var timing in timingsMs)
            {
                AppendJsonNumber(builder, timing.Key, timing.Value, 2, timingIndex < timingsMs.Count - 1);
                timingIndex++;
            }
        }
        AppendJsonObjectEnd(builder, 1, true);

        AppendJsonObjectStart(builder, "terrain_info_map", 1);
        AppendJsonNumber(builder, "cell_count", terrain.CellCount, 2);
        AppendJsonNumber(builder, "land_pixels", terrain.LandPixels, 2);
        AppendJsonNumber(builder, "water_pixels", terrain.WaterPixels, 2);
        AppendJsonNumber(builder, "coast_pixels", terrain.CoastPixels, 2);
        AppendJsonNumber(builder, "river_pixels", terrain.RiverPixels, 2);
        AppendJsonNumber(builder, "height_min", terrain.MinHeight, 2);
        AppendJsonNumber(builder, "height_max", terrain.MaxHeight, 2);
        AppendJsonNumber(builder, "height_average", terrain.AverageHeight, 2);
        AppendJsonNumber(builder, "height_p01", terrain.HeightP01, 2);
        AppendJsonNumber(builder, "height_p05", terrain.HeightP05, 2);
        AppendJsonNumber(builder, "height_p25", terrain.HeightP25, 2);
        AppendJsonNumber(builder, "height_p50", terrain.HeightP50, 2);
        AppendJsonNumber(builder, "height_p75", terrain.HeightP75, 2);
        AppendJsonNumber(builder, "height_p95", terrain.HeightP95, 2);
        AppendJsonNumber(builder, "height_p99", terrain.HeightP99, 2);
        AppendJsonNumber(builder, "river_flow_max", terrain.MaxRiverFlow, 2);
        AppendJsonNumber(builder, "river_flow_average_non_zero", terrain.AverageRiverFlow, 2);
        AppendJsonEnumCounts(builder, "biome_counts", terrain.BiomeCounts, static index => ((BiomeKind)index).ToString(), 2, false);
        AppendJsonObjectEnd(builder, 1, true);

        AppendJsonObjectStart(builder, "hex_tile_map", 1);
        AppendJsonNumber(builder, "tile_count", tile.TileCount, 2);
        AppendJsonNumber(builder, "land_tiles", tile.LandTiles, 2);
        AppendJsonNumber(builder, "water_tiles", tile.WaterTiles, 2);
        AppendJsonNumber(builder, "coastal_tiles", tile.CoastalTiles, 2);
        AppendJsonNumber(builder, "shared_river_edges", tile.SharedRiverEdges, 2);
        AppendJsonNumber(builder, "directed_river_edges", tile.DirectedRiverEdges, 2);
        AppendJsonNumber(builder, "center_min_x", tile.MinCenterX, 2);
        AppendJsonNumber(builder, "center_max_x", tile.MaxCenterX, 2);
        AppendJsonNumber(builder, "center_min_z", tile.MinCenterZ, 2);
        AppendJsonNumber(builder, "center_max_z", tile.MaxCenterZ, 2);
        AppendJsonNumber(builder, "corner_min_x", tile.MinCornerX, 2);
        AppendJsonNumber(builder, "corner_max_x", tile.MaxCornerX, 2);
        AppendJsonNumber(builder, "corner_min_z", tile.MinCornerZ, 2);
        AppendJsonNumber(builder, "corner_max_z", tile.MaxCornerZ, 2);
        AppendJsonNumber(builder, "coverage_gap_min_x", tile.MissingMinX, 2);
        AppendJsonNumber(builder, "coverage_gap_max_x", tile.MissingMaxX, 2);
        AppendJsonNumber(builder, "coverage_gap_min_z", tile.MissingMinZ, 2);
        AppendJsonNumber(builder, "coverage_gap_max_z", tile.MissingMaxZ, 2);
        AppendJsonEnumCounts(builder, "terrain_counts", tile.TerrainCounts, static index => ((TerrainKind)index).ToString(), 2, false);
        AppendJsonObjectEnd(builder, 1, true);

        AppendJsonArrayStart(builder, "warnings", 1);
        for (var i = 0; i < metrics.Warnings.Count; i++)
        {
            builder.Append(Indent(2));
            builder.Append('"');
            builder.Append(EscapeJson(metrics.Warnings[i]));
            builder.Append('"');
            builder.AppendLine(i < metrics.Warnings.Count - 1 ? "," : string.Empty);
        }
        AppendJsonArrayEnd(builder, 1, false);
        builder.AppendLine("}");

        var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);

        if (file == null)
        {
            WorldMapDebugLogger.Warn($"Failed to open terrain generation metrics json '{path}' for writing.");
            return;
        }

        file.StoreString(builder.ToString());
        file.Close();
    }

    private static void SaveHeightHistogram(HeightHistogram histogram, string path)
    {
        var image = Image.CreateEmpty(HeightHistogramImageWidth, HeightHistogramImageHeight, false, Image.Format.Rgb8);
        image.Fill(new Color(0.06f, 0.07f, 0.08f));

        var maxCount = 0;
        foreach (var count in histogram.Counts)
        {
            maxCount = Math.Max(maxCount, count);
        }

        if (maxCount == 0)
        {
            SaveImage(image, path);
            return;
        }

        var plotBottom = HeightHistogramImageHeight - 16;
        var plotTop = 12;
        var plotHeight = plotBottom - plotTop;
        var barWidth = Math.Max(1, HeightHistogramImageWidth / histogram.Counts.Length);
        var seaLevelBin = histogram.GetBinForHeight(0.0f);

        for (var bin = 0; bin < histogram.Counts.Length; bin++)
        {
            var normalized = histogram.Counts[bin] / (float)maxCount;
            var barHeight = Math.Max(1, Mathf.RoundToInt(normalized * plotHeight));
            var color = bin < seaLevelBin
                ? new Color(0.12f, 0.32f, 0.56f)
                : new Color(0.58f, 0.62f, 0.50f);

            var xStart = bin * barWidth;
            var xEnd = Math.Min(HeightHistogramImageWidth, xStart + barWidth);

            for (var x = xStart; x < xEnd; x++)
            {
                for (var y = plotBottom - barHeight; y < plotBottom; y++)
                {
                    image.SetPixel(x, y, color);
                }
            }
        }

        var seaX = Math.Clamp(seaLevelBin * barWidth, 0, HeightHistogramImageWidth - 1);
        for (var y = plotTop; y < plotBottom; y++)
        {
            image.SetPixel(seaX, y, new Color(0.82f, 0.92f, 1.0f));
        }

        SaveImage(image, path);
    }

    private static bool IsAdjacentToWater(TerrainInfoMap infoMap, int x, int y)
    {
        for (var neighborY = y - 1; neighborY <= y + 1; neighborY++)
        {
            for (var neighborX = x - 1; neighborX <= x + 1; neighborX++)
            {
                if (neighborX == x && neighborY == y)
                {
                    continue;
                }

                if (!infoMap.IsInBounds(neighborX, neighborY))
                {
                    continue;
                }

                if (infoMap.LandMask[infoMap.GetIndex(neighborX, neighborY)] == 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static float GetPercentile(float[] sortedValues, float percentile)
    {
        if (sortedValues.Length == 0)
        {
            return 0.0f;
        }

        var position = Math.Clamp(percentile, 0.0f, 1.0f) * (sortedValues.Length - 1);
        var lowerIndex = Math.Clamp((int)MathF.Floor(position), 0, sortedValues.Length - 1);
        var upperIndex = Math.Clamp((int)MathF.Ceiling(position), 0, sortedValues.Length - 1);
        var weight = position - lowerIndex;

        return Mathf.Lerp(sortedValues[lowerIndex], sortedValues[upperIndex], weight);
    }

    private static float Ratio(int value, int total)
    {
        return total <= 0 ? 0.0f : value / (float)total;
    }

    private static string FormatPercent(float value)
    {
        return $"{value * 100.0f:0.##}%";
    }

    private static void WriteEnumCounts(FileAccess file, int[] counts, Func<int, string> getName, int total)
    {
        for (var index = 0; index < counts.Length; index++)
        {
            file.StoreLine($"- {getName(index)}: {counts[index]} ({FormatPercent(Ratio(counts[index], total))})");
        }
    }

    private static void SaveImage(Image image, string path)
    {
        var error = image.SavePng(path);

        if (error != Error.Ok)
        {
            WorldMapDebugLogger.Warn($"Failed to save terrain generation quality image '{path}': {error}");
        }
    }

    private static void EnsureParentDirectory(string path)
    {
        var normalizedPath = path.Replace('\\', '/');
        var slashIndex = normalizedPath.LastIndexOf('/');

        if (slashIndex < 0)
        {
            return;
        }

        var directory = normalizedPath[..slashIndex];

        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(directory));
    }

    private static void AppendJsonObjectStart(StringBuilder builder, string key, int indent)
    {
        builder.Append(Indent(indent));
        builder.Append('"');
        builder.Append(EscapeJson(key));
        builder.AppendLine("\": {");
    }

    private static void AppendJsonObjectEnd(StringBuilder builder, int indent, bool includeComma)
    {
        builder.Append(Indent(indent));
        builder.Append('}');
        builder.AppendLine(includeComma ? "," : string.Empty);
    }

    private static void AppendJsonArrayStart(StringBuilder builder, string key, int indent)
    {
        builder.Append(Indent(indent));
        builder.Append('"');
        builder.Append(EscapeJson(key));
        builder.AppendLine("\": [");
    }

    private static void AppendJsonArrayEnd(StringBuilder builder, int indent, bool includeComma)
    {
        builder.Append(Indent(indent));
        builder.Append(']');
        builder.AppendLine(includeComma ? "," : string.Empty);
    }

    private static void AppendJsonString(StringBuilder builder, string key, string value, int indent, bool includeComma = true)
    {
        builder.Append(Indent(indent));
        builder.Append('"');
        builder.Append(EscapeJson(key));
        builder.Append("\": \"");
        builder.Append(EscapeJson(value));
        builder.Append('"');
        builder.AppendLine(includeComma ? "," : string.Empty);
    }

    private static void AppendJsonNumber(StringBuilder builder, string key, double value, int indent, bool includeComma = true)
    {
        builder.Append(Indent(indent));
        builder.Append('"');
        builder.Append(EscapeJson(key));
        builder.Append("\": ");
        builder.Append(value.ToString("0.###", CultureInfo.InvariantCulture));
        builder.AppendLine(includeComma ? "," : string.Empty);
    }

    private static void AppendJsonEnumCounts(StringBuilder builder, string key, int[] counts, Func<int, string> getName, int indent, bool includeComma)
    {
        AppendJsonObjectStart(builder, key, indent);

        for (var index = 0; index < counts.Length; index++)
        {
            AppendJsonNumber(builder, getName(index), counts[index], indent + 1, index < counts.Length - 1);
        }

        AppendJsonObjectEnd(builder, indent, includeComma);
    }

    private static string EscapeJson(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string Indent(int depth)
    {
        return new string(' ', depth * 2);
    }

    private readonly record struct TerrainMetrics(
        TerrainInfoMap InfoMap,
        WorldMapConfig Config,
        TerrainInfoMetrics Terrain,
        TileMetrics Tile,
        List<string> Warnings);

    private readonly record struct TerrainInfoMetrics(
        int CellCount,
        int LandPixels,
        int WaterPixels,
        int CoastPixels,
        int RiverPixels,
        float AverageHeight,
        float MinHeight,
        float MaxHeight,
        float HeightP01,
        float HeightP05,
        float HeightP25,
        float HeightP50,
        float HeightP75,
        float HeightP95,
        float HeightP99,
        float MaxRiverFlow,
        float AverageRiverFlow,
        int[] BiomeCounts,
        HeightHistogram HeightHistogram);

    private readonly record struct TileMetrics(
        int TileCount,
        int LandTiles,
        int WaterTiles,
        int CoastalTiles,
        int SharedRiverEdges,
        int DirectedRiverEdges,
        float MinCenterX,
        float MaxCenterX,
        float MinCenterZ,
        float MaxCenterZ,
        float MinCornerX,
        float MaxCornerX,
        float MinCornerZ,
        float MaxCornerZ,
        float MissingMinX,
        float MissingMaxX,
        float MissingMinZ,
        float MissingMaxZ,
        int[] TerrainCounts)
    {
        public static TileMetrics Empty => new(
            0,
            0,
            0,
            0,
            0,
            0,
            0.0f,
            0.0f,
            0.0f,
            0.0f,
            0.0f,
            0.0f,
            0.0f,
            0.0f,
            0.0f,
            0.0f,
            0.0f,
            0.0f,
            Array.Empty<int>());

        public int GetTerrainCount(TerrainKind terrain)
        {
            var index = (int)terrain;
            return index >= 0 && index < TerrainCounts.Length ? TerrainCounts[index] : 0;
        }
    }

    private readonly record struct HeightHistogram(float MinHeight, float MaxHeight, int[] Counts)
    {
        public int GetBinForHeight(float height)
        {
            if (Counts.Length == 0)
            {
                return 0;
            }

            var range = Math.Max(0.0001f, MaxHeight - MinHeight);
            return Math.Clamp((int)((height - MinHeight) / range * (Counts.Length - 1)), 0, Counts.Length - 1);
        }
    }
}
