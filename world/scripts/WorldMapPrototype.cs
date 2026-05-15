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
	[Export] public string TerrainInfoMapPath { get; set; } = TerrainInfoMap.DefaultResourcePath;
	[Export] public string HexTileMapPath { get; set; } = HexTileMap.DefaultResourcePath;
	[Export] public string HexTileBakeReportPath { get; set; } = HexTileBaker.DefaultBakeReportPath;
	[Export] public string HexTileTerrainDebugPath { get; set; } = HexTileDebugExporter.DefaultTerrainPath;
	[Export] public string DebugOutputDirectory { get; set; } = TerrainInfoMapDebugExporter.DefaultOutputDirectory;
	[Export] public NodePath TerrainRootPath { get; set; } = "terrain_root";

	public WorldMapConfig Config { get; private set; }
	public TerrainInfoMap TerrainInfoMap { get; private set; }
	public HexTileMap HexTileMap { get; private set; }

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

		if (ValidateCoordinatesOnReady)
		{
			ValidateCoordinateSystem();
		}

		if (GenerateTerrainInfoOnReady)
		{
			GenerateTerrainInfoMap();
		}

		if (BakeVisualTerrainOnReady && TerrainInfoMap != null)
		{
			BakeVisualTerrain();
		}

		if (BakeHexTilesOnReady && TerrainInfoMap != null)
		{
			BakeHexTiles();
		}
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
}
