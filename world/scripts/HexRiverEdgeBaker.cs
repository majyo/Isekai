using System;
using Godot;

namespace Isekai.World;

public sealed class HexRiverEdgeBaker
{
    public const string DefaultBakeReportPath = "res://world/generated/hex_river_edge_bake_report.txt";

    private const float SmallRiverThreshold = 0.08f;
    private const float MajorRiverThreshold = 0.48f;
    private const float SmallRiverCrossingCost = 0.75f;
    private const float MajorRiverCrossingCost = 1.50f;

    public void Bake(TerrainInfoMap infoMap, HexTileMap tileMap, WorldMapConfig config, string reportPath = DefaultBakeReportPath)
    {
        Array.Clear(tileMap.RiverKindByEdge);
        Array.Clear(tileMap.RiverFlowByEdge);
        Array.Clear(tileMap.RiverCrossingCostByEdge);

        tileMap.RebuildLookup();

        for (var tileIndex = 0; tileIndex < tileMap.TileCount; tileIndex++)
        {
            var axial = new Vector2I(tileMap.Q[tileIndex], tileMap.R[tileIndex]);

            for (var direction = 0; direction < WorldMapCoordinateUtility.AxialNeighborDirections.Length; direction++)
            {
                var neighborAxial = axial + WorldMapCoordinateUtility.AxialNeighborDirections[direction];

                if (!tileMap.TryGetTileIndex(neighborAxial, out var neighborIndex) || neighborIndex < tileIndex)
                {
                    continue;
                }

                BakeSharedEdge(infoMap, tileMap, config, tileIndex, neighborIndex, direction);
            }
        }

        SaveReport(tileMap, reportPath);
    }

    private static void BakeSharedEdge(TerrainInfoMap infoMap, HexTileMap tileMap, WorldMapConfig config, int tileIndex, int neighborIndex, int direction)
    {
        if (tileMap.IsWater[tileIndex] != 0 || tileMap.IsWater[neighborIndex] != 0)
        {
            return;
        }

        var flow = SampleEdgeFlow(infoMap, config, tileMap, tileIndex, neighborIndex);
        var riverKind = ClassifyRiver(flow);

        if (riverKind == RiverKind.None)
        {
            return;
        }

        var crossingCost = GetCrossingCostModifier(riverKind);
        tileMap.SetRiverEdge(tileIndex, direction, riverKind, flow, crossingCost);
        tileMap.SetRiverEdge(neighborIndex, HexTileMap.GetOppositeDirection(direction), riverKind, flow, crossingCost);
    }

    private static float SampleEdgeFlow(TerrainInfoMap infoMap, WorldMapConfig config, HexTileMap tileMap, int tileIndex, int neighborIndex)
    {
        var center = new Vector2(tileMap.WorldCenterX[tileIndex], tileMap.WorldCenterZ[tileIndex]);
        var neighborCenter = new Vector2(tileMap.WorldCenterX[neighborIndex], tileMap.WorldCenterZ[neighborIndex]);
        var edgeCenter = (center + neighborCenter) * 0.5f;
        var centerToNeighbor = (neighborCenter - center).Normalized();
        var edgeTangent = new Vector2(-centerToNeighbor.Y, centerToNeighbor.X);
        var tangentOffset = edgeTangent * (config.HexRadius * 0.42f);
        var normalOffset = centerToNeighbor * (config.HexRadius * 0.22f);

        var samplePoints = new[]
        {
            edgeCenter,
            edgeCenter + tangentOffset,
            edgeCenter - tangentOffset,
            edgeCenter + tangentOffset * 0.5f,
            edgeCenter - tangentOffset * 0.5f,
            edgeCenter + normalOffset,
            edgeCenter - normalOffset
        };

        var maxFlow = 0.0f;

        foreach (var samplePoint in samplePoints)
        {
            var uv = WorldMapCoordinateUtility.WorldXzToUv(samplePoint, config);
            maxFlow = MathF.Max(maxFlow, infoMap.SampleRiverFlowUv(uv));
        }

        return maxFlow;
    }

    private static RiverKind ClassifyRiver(float flow)
    {
        if (flow >= MajorRiverThreshold)
        {
            return RiverKind.Major;
        }

        if (flow >= SmallRiverThreshold)
        {
            return RiverKind.Small;
        }

        return RiverKind.None;
    }

    private static float GetCrossingCostModifier(RiverKind riverKind)
    {
        return riverKind switch
        {
            RiverKind.Small => SmallRiverCrossingCost,
            RiverKind.Major => MajorRiverCrossingCost,
            _ => 0.0f
        };
    }

    private static void SaveReport(HexTileMap tileMap, string path)
    {
        var smallRiverEdges = 0;
        var majorRiverEdges = 0;
        var maxFlow = 0.0f;
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

                if (tileMap.TryGetTileIndex(neighborAxial, out var neighborIndex))
                {
                    var neighborEdge = tileMap.GetRiverEdge(neighborIndex, HexTileMap.GetOppositeDirection(direction));

                    if (neighborEdge.Kind != edge.Kind || !Mathf.IsEqualApprox(neighborEdge.Flow, edge.Flow))
                    {
                        inconsistentEdges++;
                    }

                    if (neighborIndex < tileIndex)
                    {
                        continue;
                    }
                }

                if (edge.Kind == RiverKind.Major)
                {
                    majorRiverEdges++;
                }
                else
                {
                    smallRiverEdges++;
                }

                maxFlow = MathF.Max(maxFlow, edge.Flow);
            }
        }

        var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);

        if (file == null)
        {
            WorldMapDebugLogger.Warn($"Failed to open hex river edge bake report '{path}' for writing.");
            return;
        }

        file.StoreLine("Hex River Edge Bake Report");
        file.StoreLine($"Seed: {tileMap.Seed}");
        file.StoreLine($"GridSize: {tileMap.GridSize}");
        file.StoreLine($"TileCount: {tileMap.TileCount}");
        file.StoreLine($"SharedRiverEdges: {smallRiverEdges + majorRiverEdges}");
        file.StoreLine($"SmallRiverEdges: {smallRiverEdges}");
        file.StoreLine($"MajorRiverEdges: {majorRiverEdges}");
        file.StoreLine($"DirectedRiverEdges: {tileMap.CountRiverEdges(false)}");
        file.StoreLine($"MaxFlow: {maxFlow:0.###}");
        file.StoreLine($"InconsistentDirectedEdges: {inconsistentEdges}");
        file.Close();
    }
}
