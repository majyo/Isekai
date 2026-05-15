using System;
using Godot;

namespace Isekai.World;

public sealed class HexTileBaker
{
    public const string DefaultBakeReportPath = "res://world/generated/hex_tile_bake_report.txt";

    private const int SamplesPerTile = 16;
    private const float MountainSlopeThreshold = 2.20f;
    private const float HillSlopeThreshold = 0.85f;

    public HexTileMap Bake(TerrainInfoMap infoMap, WorldMapConfig config, string reportPath = DefaultBakeReportPath)
    {
        var tileCount = config.TargetHexGridSize.X * config.TargetHexGridSize.Y;
        var tileMap = new HexTileMap();
        tileMap.Initialize(config, tileCount);

        var writeIndex = 0;

        for (var gridY = 0; gridY < config.TargetHexGridSize.Y; gridY++)
        {
            var r = gridY - config.TargetHexGridSize.Y / 2;
            var qOffset = -Mathf.FloorToInt(r / 2.0f);

            for (var gridX = 0; gridX < config.TargetHexGridSize.X; gridX++)
            {
                var q = gridX - config.TargetHexGridSize.X / 2 + qOffset;
                BakeTileBase(tileMap, writeIndex, gridX, gridY, q, r, infoMap, config);
                writeIndex++;
            }
        }

        tileMap.RebuildLookup();
        ApplyCoastAdjacency(tileMap);
        SaveReport(tileMap, reportPath);

        return tileMap;
    }

    private static void BakeTileBase(
        HexTileMap tileMap,
        int index,
        int gridX,
        int gridY,
        int q,
        int r,
        TerrainInfoMap infoMap,
        WorldMapConfig config)
    {
        var centerWorldXz = WorldMapCoordinateUtility.AxialToWorldXz(new Vector2I(q, r), config);
        var samplePoints = BuildSamplePoints(centerWorldXz, config.HexRadius);

        var minHeight = float.PositiveInfinity;
        var maxHeight = float.NegativeInfinity;
        var heightSum = 0.0f;
        var landSamples = 0;
        var waterSamples = 0;
        var biomeCounts = new int[Enum.GetValues<BiomeKind>().Length];

        for (var i = 0; i < samplePoints.Length; i++)
        {
            var uv = WorldMapCoordinateUtility.WorldXzToUv(samplePoints[i], config);
            var sampleHeight = infoMap.SampleHeightUv(uv);
            var isLand = infoMap.SampleIsLandUv(uv);
            var biome = infoMap.SampleBiomeUv(uv);

            minHeight = MathF.Min(minHeight, sampleHeight);
            maxHeight = MathF.Max(maxHeight, sampleHeight);
            heightSum += sampleHeight;

            if (isLand)
            {
                landSamples++;
            }
            else
            {
                waterSamples++;
            }

            biomeCounts[(int)biome]++;
        }

        var centerUv = WorldMapCoordinateUtility.WorldXzToUv(centerWorldXz, config);
        var centerHeight = infoMap.SampleHeightUv(centerUv);
        var averageHeight = heightSum / samplePoints.Length;
        var slope = (maxHeight - minHeight) / Math.Max(1.0f, config.HexRadius * 2.0f);
        var isWater = waterSamples > landSamples;
        var dominantBiome = GetDominantBiome(biomeCounts);
        var terrain = ClassifyTerrain(config, isWater, false, averageHeight, slope, dominantBiome);
        var movementCost = GetMovementCost(terrain);
        var regionId = GetRegionId(gridX, gridY, config.TargetHexGridSize);
        var ownerId = isWater ? 0 : regionId % 6 + 1;

        tileMap.Q[index] = q;
        tileMap.R[index] = r;
        tileMap.WorldCenterX[index] = centerWorldXz.X;
        tileMap.WorldCenterZ[index] = centerWorldXz.Y;
        tileMap.CenterHeight[index] = centerHeight;
        tileMap.AverageHeight[index] = averageHeight;
        tileMap.MinHeight[index] = minHeight;
        tileMap.MaxHeight[index] = maxHeight;
        tileMap.Slope[index] = slope;
        tileMap.IsWater[index] = isWater ? (byte)1 : (byte)0;
        tileMap.IsCoastal[index] = 0;
        tileMap.Terrain[index] = (int)terrain;
        tileMap.Biome[index] = (int)dominantBiome;
        tileMap.MovementCost[index] = movementCost;
        tileMap.ProvinceId[index] = -1;
        tileMap.RegionId[index] = regionId;
        tileMap.OwnerId[index] = ownerId;
        tileMap.ResourceId[index] = -1;
    }

    private static Vector2[] BuildSamplePoints(Vector2 centerWorldXz, float hexRadius)
    {
        var points = new Vector2[SamplesPerTile];
        var index = 0;
        points[index++] = centerWorldXz;

        for (var corner = 0; corner < 6; corner++)
        {
            points[index++] = WorldMapCoordinateUtility.GetHexCornerWorldXz(centerWorldXz, hexRadius, corner);
        }

        var edgeMidpointDistance = hexRadius * 0.8660254f;

        for (var edge = 0; edge < 6; edge++)
        {
            var angle = Mathf.DegToRad(60.0f * edge);
            points[index++] = centerWorldXz + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * edgeMidpointDistance;
        }

        for (var inner = 0; inner < 3; inner++)
        {
            var angle = Mathf.DegToRad(120.0f * inner + 30.0f);
            points[index++] = centerWorldXz + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (hexRadius * 0.45f);
        }

        return points;
    }

    private static void ApplyCoastAdjacency(HexTileMap tileMap)
    {
        for (var index = 0; index < tileMap.TileCount; index++)
        {
            var axial = new Vector2I(tileMap.Q[index], tileMap.R[index]);
            var isWater = tileMap.IsWater[index] != 0;
            var hasOppositeNeighbor = false;

            for (var direction = 0; direction < WorldMapCoordinateUtility.AxialNeighborDirections.Length; direction++)
            {
                var neighborAxial = axial + WorldMapCoordinateUtility.AxialNeighborDirections[direction];

                if (!tileMap.TryGetTile(neighborAxial, out var neighbor))
                {
                    continue;
                }

                if (neighbor.IsWater != isWater)
                {
                    hasOppositeNeighbor = true;
                    break;
                }
            }

            if (!hasOppositeNeighbor)
            {
                continue;
            }

            tileMap.IsCoastal[index] = 1;

            if (!isWater)
            {
                tileMap.Terrain[index] = (int)TerrainKind.Coast;
                tileMap.MovementCost[index] = GetMovementCost(TerrainKind.Coast);
            }
        }
    }

    private static TerrainKind ClassifyTerrain(
        WorldMapConfig config,
        bool isWater,
        bool isCoastal,
        float averageHeight,
        float slope,
        BiomeKind biome)
    {
        if (isWater)
        {
            return TerrainKind.Ocean;
        }

        if (isCoastal)
        {
            return TerrainKind.Coast;
        }

        var normalizedHeight = Mathf.Clamp((averageHeight - config.SeaLevel) / Math.Max(1.0f, config.MaxHeight - config.SeaLevel), 0.0f, 1.0f);

        if (slope >= MountainSlopeThreshold || normalizedHeight >= 0.58f || biome == BiomeKind.Mountain)
        {
            return TerrainKind.Mountains;
        }

        if (slope >= HillSlopeThreshold || normalizedHeight >= 0.34f || biome == BiomeKind.Hills)
        {
            return TerrainKind.Hills;
        }

        return biome switch
        {
            BiomeKind.Forest => TerrainKind.Forest,
            BiomeKind.Desert => TerrainKind.Desert,
            BiomeKind.Tundra => TerrainKind.Tundra,
            _ => TerrainKind.Plains
        };
    }

    private static BiomeKind GetDominantBiome(int[] biomeCounts)
    {
        var bestIndex = 0;
        var bestCount = biomeCounts[0];

        for (var index = 1; index < biomeCounts.Length; index++)
        {
            if (biomeCounts[index] > bestCount)
            {
                bestCount = biomeCounts[index];
                bestIndex = index;
            }
        }

        return (BiomeKind)bestIndex;
    }

    private static int GetRegionId(int gridX, int gridY, Vector2I gridSize)
    {
        var regionWidth = Math.Max(1, Mathf.CeilToInt(gridSize.X / 8.0f));
        var regionHeight = Math.Max(1, Mathf.CeilToInt(gridSize.Y / 8.0f));
        return gridY / regionHeight * 8 + gridX / regionWidth;
    }

    private static float GetMovementCost(TerrainKind terrain)
    {
        return terrain switch
        {
            TerrainKind.Ocean => 4.0f,
            TerrainKind.Coast => 1.2f,
            TerrainKind.Plains => 1.0f,
            TerrainKind.Forest => 1.45f,
            TerrainKind.Desert => 1.60f,
            TerrainKind.Tundra => 1.70f,
            TerrainKind.Hills => 1.85f,
            TerrainKind.Mountains => 3.75f,
            _ => 1.0f
        };
    }

    private static void SaveReport(HexTileMap tileMap, string path)
    {
        var terrainCounts = new int[Enum.GetValues<TerrainKind>().Length];
        var waterCount = 0;
        var coastalCount = 0;
        var minX = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var minZ = float.PositiveInfinity;
        var maxZ = float.NegativeInfinity;

        for (var index = 0; index < tileMap.TileCount; index++)
        {
            terrainCounts[tileMap.Terrain[index]]++;
            waterCount += tileMap.IsWater[index] != 0 ? 1 : 0;
            coastalCount += tileMap.IsCoastal[index] != 0 ? 1 : 0;
            minX = MathF.Min(minX, tileMap.WorldCenterX[index]);
            maxX = MathF.Max(maxX, tileMap.WorldCenterX[index]);
            minZ = MathF.Min(minZ, tileMap.WorldCenterZ[index]);
            maxZ = MathF.Max(maxZ, tileMap.WorldCenterZ[index]);
        }

        var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);

        if (file == null)
        {
            WorldMapDebugLogger.Warn($"Failed to open hex tile bake report '{path}' for writing.");
            return;
        }

        file.StoreLine("Hex Tile Bake Report");
        file.StoreLine($"Seed: {tileMap.Seed}");
        file.StoreLine($"GridSize: {tileMap.GridSize}");
        file.StoreLine($"TileCount: {tileMap.TileCount}");
        file.StoreLine($"HexRadius: {tileMap.HexRadius}");
        file.StoreLine($"WorldCenterXRange: {minX:0.###} .. {maxX:0.###}");
        file.StoreLine($"WorldCenterZRange: {minZ:0.###} .. {maxZ:0.###}");
        file.StoreLine($"WaterTiles: {waterCount}");
        file.StoreLine($"CoastalTiles: {coastalCount}");
        file.StoreLine("TerrainCounts:");

        for (var index = 0; index < terrainCounts.Length; index++)
        {
            file.StoreLine($"- {(TerrainKind)index}: {terrainCounts[index]}");
        }

        file.Close();
    }
}
