using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;

namespace Isekai.World;

public sealed partial class WorldMapPrototype : Node3D
{
	[Export] public string ConfigPath { get; set; } = WorldMapConfig.DefaultResourcePath;
	[Export] public bool ValidateCoordinatesOnReady { get; set; } = true;
	[Export] public string CoordinateValidationReportPath { get; set; } = WorldMapCoordinateValidator.DefaultReportPath;
	[Export] public bool GenerateTerrainInfoOnReady { get; set; } = true;
	[Export] public bool BakeVisualTerrainOnReady { get; set; } = true;
	[Export] public bool BakeHexTilesOnReady { get; set; } = true;
	[Export] public bool BakeRiverEdgesOnReady { get; set; } = true;
	[Export] public bool RenderHexOverlayOnReady { get; set; } = true;
	[Export] public bool ValidateMvpPipelineOnReady { get; set; } = true;
	[Export] public bool ExportTerrainQualityOnReady { get; set; } = true;
	[Export] public string TerrainInfoMapPath { get; set; } = TerrainInfoMap.DefaultResourcePath;
	[Export] public string HexTileMapPath { get; set; } = HexTileMap.DefaultResourcePath;
	[Export] public string HexTileBakeReportPath { get; set; } = HexTileBaker.DefaultBakeReportPath;
	[Export] public string HexTileTerrainDebugPath { get; set; } = HexTileDebugExporter.DefaultTerrainPath;
	[Export] public string HexRiverEdgeBakeReportPath { get; set; } = HexRiverEdgeBaker.DefaultBakeReportPath;
	[Export] public string HexRiverEdgeDebugPath { get; set; } = HexRiverEdgeDebugExporter.DefaultDebugPath;
	[Export] public string MvpValidationReportPath { get; set; } = WorldMapMvpValidator.DefaultReportPath;
	[Export] public string TerrainQualityReportPath { get; set; } = TerrainGenerationQualityReporter.DefaultQualityReportPath;
	[Export] public string TerrainMetricsJsonPath { get; set; } = TerrainGenerationQualityReporter.DefaultMetricsJsonPath;
	[Export] public string HeightHistogramDebugPath { get; set; } = TerrainGenerationQualityReporter.DefaultHeightHistogramPath;
	[Export] public string DebugOutputDirectory { get; set; } = TerrainInfoMapDebugExporter.DefaultOutputDirectory;
	[Export] public NodePath TerrainRootPath { get; set; } = "terrain_root";
	[Export] public NodePath HexOverlayRendererPath { get; set; } = "hex_overlay";
	[Export] public NodePath InputControllerPath { get; set; } = "world_map_input";

	public WorldMapConfig Config { get; private set; }
	public TerrainInfoMap TerrainInfoMap { get; private set; }
	public HexTileMap HexTileMap { get; private set; }

	private readonly Dictionary<string, double> _pipelineTimingsMs = new();

	public override void _Ready()
	{
		Config = ResourceLoader.Load<WorldMapConfig>(ConfigPath);

		if (Config == null)
		{
			WorldMapDebugLogger.Warn($"Could not load world map config at '{ConfigPath}'.");
			return;
		}

		if (!Config.IsValid(out var validationMessage))
		{
			WorldMapDebugLogger.Warn(validationMessage);
			return;
		}

		WorldMapDebugLogger.LogSystem($"Loaded config from '{ConfigPath}'.");
		WorldMapDebugLogger.LogGenerationStep("Phase 0 ready.", Config);
		_pipelineTimingsMs.Clear();

		if (ValidateCoordinatesOnReady)
		{
			RunTimedStage("coordinate_validation", ValidateCoordinateSystem);
		}

		if (GenerateTerrainInfoOnReady)
		{
			RunTimedStage("terrain_info_generation", GenerateTerrainInfoMap);
		}

		if (BakeVisualTerrainOnReady && TerrainInfoMap != null)
		{
			RunTimedStage("visual_terrain_bake", BakeVisualTerrain);
		}

		if (BakeHexTilesOnReady && TerrainInfoMap != null)
		{
			RunTimedStage("hex_tile_bake", BakeHexTiles);
		}

		if (BakeRiverEdgesOnReady && TerrainInfoMap != null && HexTileMap != null)
		{
			RunTimedStage("hex_river_edge_bake", BakeRiverEdges);
		}

		if (RenderHexOverlayOnReady && TerrainInfoMap != null && HexTileMap != null)
		{
			RunTimedStage("hex_overlay_render", RenderHexOverlay);
		}

		if (ExportTerrainQualityOnReady && TerrainInfoMap != null)
		{
			RunTimedStage("terrain_quality_export", ExportTerrainGenerationQualityReport);
		}

		if (ValidateMvpPipelineOnReady && TerrainInfoMap != null && HexTileMap != null)
		{
			RunTimedStage("mvp_validation", ValidateMvpPipeline);
		}
	}

	public void RunFullRebakePipeline()
	{
		if (Config == null)
		{
			WorldMapDebugLogger.Warn("Cannot run full rebake pipeline before config is loaded.");
			return;
		}

		_pipelineTimingsMs.Clear();
		RunTimedStage("coordinate_validation", ValidateCoordinateSystem);
		RunTimedStage("terrain_info_generation", GenerateTerrainInfoMap);
		RunTimedStage("visual_terrain_bake", BakeVisualTerrain);
		RunTimedStage("hex_tile_bake", BakeHexTiles);
		RunTimedStage("hex_river_edge_bake", BakeRiverEdges);
		RunTimedStage("hex_overlay_render", RenderHexOverlay);
		RunTimedStage("terrain_quality_export", ExportTerrainGenerationQualityReport);
		RunTimedStage("mvp_validation", ValidateMvpPipeline);
	}

	private void ValidateCoordinateSystem()
	{
		var isValid = WorldMapCoordinateValidator.Validate(Config, out var report);
		WorldMapCoordinateValidator.SaveReport(report, CoordinateValidationReportPath);

		if (isValid)
		{
			WorldMapDebugLogger.LogSystem($"Coordinate validation passed. Report saved to '{CoordinateValidationReportPath}'.");
		}
		else
		{
			WorldMapDebugLogger.Warn($"Coordinate validation failed. Report saved to '{CoordinateValidationReportPath}'.");
		}
	}

	private void GenerateTerrainInfoMap()
	{
		WorldMapDebugLogger.LogGenerationStep("Generating terrain information map.", Config);

		var generator = new WorldGenerator();
		TerrainInfoMap = generator.Generate(Config);

		var saveError = ResourceSaver.Save(TerrainInfoMap, TerrainInfoMapPath);

		if (saveError != Error.Ok)
		{
			WorldMapDebugLogger.Warn($"Failed to save terrain info map '{TerrainInfoMapPath}': {saveError}");
		}
		else
		{
			WorldMapDebugLogger.LogGenerationStep($"Saved terrain information map to '{TerrainInfoMapPath}'.", Config);
		}

		TerrainInfoMapDebugExporter.ExportAll(TerrainInfoMap, DebugOutputDirectory);
		WorldMapDebugLogger.LogGenerationStep($"Exported terrain information debug maps to '{DebugOutputDirectory}'.", Config);
	}

	private void BakeVisualTerrain()
	{
		var terrainRoot = GetNodeOrNull<Node3D>(TerrainRootPath);

		if (terrainRoot == null)
		{
			WorldMapDebugLogger.Warn($"Could not find terrain root at '{TerrainRootPath}'.");
			return;
		}

		WorldMapDebugLogger.LogBakeStep("Baking visual terrain.", Config);
		var baker = new Terrain3DBaker();
		baker.Bake(TerrainInfoMap, Config, terrainRoot);
	}

	private void BakeHexTiles()
	{
		WorldMapDebugLogger.LogBakeStep("Baking hex tile data.", Config);

		var baker = new HexTileBaker();
		HexTileMap = baker.Bake(TerrainInfoMap, Config, HexTileBakeReportPath);

		var saveError = ResourceSaver.Save(HexTileMap, HexTileMapPath);

		if (saveError != Error.Ok)
		{
			WorldMapDebugLogger.Warn($"Failed to save hex tile map '{HexTileMapPath}': {saveError}");
		}
		else
		{
			WorldMapDebugLogger.LogBakeStep($"Saved hex tile map to '{HexTileMapPath}'.", Config);
		}

		HexTileDebugExporter.ExportTerrainMap(HexTileMap, HexTileTerrainDebugPath);
		WorldMapDebugLogger.LogBakeStep($"Exported hex tile terrain debug map to '{HexTileTerrainDebugPath}'.", Config);
	}

	private void BakeRiverEdges()
	{
		WorldMapDebugLogger.LogBakeStep("Baking hex river edge data.", Config);

		var baker = new HexRiverEdgeBaker();
		baker.Bake(TerrainInfoMap, HexTileMap, Config, HexRiverEdgeBakeReportPath);

		var saveError = ResourceSaver.Save(HexTileMap, HexTileMapPath);

		if (saveError != Error.Ok)
		{
			WorldMapDebugLogger.Warn($"Failed to save hex tile map with river edges '{HexTileMapPath}': {saveError}");
		}
		else
		{
			WorldMapDebugLogger.LogBakeStep($"Saved hex tile map with river edges to '{HexTileMapPath}'.", Config);
		}

		HexRiverEdgeDebugExporter.ExportRiverEdgeMap(HexTileMap, HexRiverEdgeDebugPath);
		WorldMapDebugLogger.LogBakeStep($"Exported hex river edge debug map to '{HexRiverEdgeDebugPath}'.", Config);
	}

	private void RenderHexOverlay()
	{
		var renderer = GetNodeOrNull<HexOverlayRenderer>(HexOverlayRendererPath);

		if (renderer == null)
		{
			WorldMapDebugLogger.Warn($"Could not find hex overlay renderer at '{HexOverlayRendererPath}'.");
			return;
		}

		renderer.Render(TerrainInfoMap, HexTileMap, Config);
		InitializeInputController(renderer);
	}

	private void ExportTerrainGenerationQualityReport()
	{
		TerrainGenerationQualityReporter.Export(
			TerrainInfoMap,
			HexTileMap,
			Config,
			_pipelineTimingsMs,
			TerrainQualityReportPath,
			TerrainMetricsJsonPath,
			HeightHistogramDebugPath);

		WorldMapDebugLogger.LogSystem($"Exported terrain generation quality report to '{TerrainQualityReportPath}'.");
	}

	private void InitializeInputController(HexOverlayRenderer renderer)
	{
		var inputController = GetNodeOrNull<WorldMapInputController>(InputControllerPath);

		if (inputController == null)
		{
			WorldMapDebugLogger.Warn($"Could not find world map input controller at '{InputControllerPath}'.");
			return;
		}

		inputController.Initialize(Config, TerrainInfoMap, HexTileMap, renderer);
	}

	private void ValidateMvpPipeline()
	{
		var terrainRoot = GetNodeOrNull<Node3D>(TerrainRootPath);
		var renderer = GetNodeOrNull<HexOverlayRenderer>(HexOverlayRendererPath);
		var inputController = GetNodeOrNull<WorldMapInputController>(InputControllerPath);
		var isValid = WorldMapMvpValidator.Validate(Config, TerrainInfoMap, HexTileMap, terrainRoot, renderer, inputController, out var report);
		WorldMapMvpValidator.SaveReport(report, MvpValidationReportPath);

		if (isValid)
		{
			WorldMapDebugLogger.LogSystem($"MVP validation passed. Report saved to '{MvpValidationReportPath}'.");
		}
		else
		{
			WorldMapDebugLogger.Warn($"MVP validation failed. Report saved to '{MvpValidationReportPath}'.");
		}
	}

	private void RunTimedStage(string stageName, Action action)
	{
		var stopwatch = Stopwatch.StartNew();

		try
		{
			action();
		}
		finally
		{
			stopwatch.Stop();
			_pipelineTimingsMs[stageName] = stopwatch.Elapsed.TotalMilliseconds;
		}
	}
}
