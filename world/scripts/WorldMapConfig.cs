using Godot;

namespace Isekai.World;

public enum VisualTerrainMode
{
	ArrayMeshPreview = 0,
	Terrain3D = 1
}

[GlobalClass]
public sealed partial class WorldMapConfig : Resource
{
	public const string DefaultResourcePath = "res://world/configs/world_map_config.tres";

	[Export] public Vector2 WorldSize { get; set; } = new(4096.0f, 4096.0f);

	[Export] public Vector2I InfoMapSize { get; set; } = new(1024, 1024);

	[Export] public Vector2I TargetHexGridSize { get; set; } = new(128, 128);

	[Export] public VisualTerrainMode VisualTerrainMode { get; set; } = VisualTerrainMode.ArrayMeshPreview;

	[Export] public Vector2I VisualTerrainGridSize { get; set; } = new(193, 193);

	[Export(PropertyHint.Range, "-1000,1000,1")]
	public float SeaLevel { get; set; } = 0.0f;

	[Export(PropertyHint.Range, "1,5000,1")]
	public float MaxHeight { get; set; } = 800.0f;

	[Export(PropertyHint.Range, "1,512,0.5")]
	public float HexRadius { get; set; } = 16.0f;

	[Export] public int Seed { get; set; } = 1337;

	public bool IsValid(out string message)
	{
		if (WorldSize.X <= 0.0f || WorldSize.Y <= 0.0f)
		{
			message = "WorldSize must be positive on both axes.";
			return false;
		}

		if (InfoMapSize.X <= 0 || InfoMapSize.Y <= 0)
		{
			message = "InfoMapSize must be positive on both axes.";
			return false;
		}

		if (TargetHexGridSize.X <= 0 || TargetHexGridSize.Y <= 0)
		{
			message = "TargetHexGridSize must be positive on both axes.";
			return false;
		}

		if (VisualTerrainGridSize.X <= 1 || VisualTerrainGridSize.Y <= 1)
		{
			message = "VisualTerrainGridSize must be greater than one on both axes.";
			return false;
		}

		if (MaxHeight <= SeaLevel)
		{
			message = "MaxHeight must be greater than SeaLevel.";
			return false;
		}

		if (HexRadius <= 0.0f)
		{
			message = "HexRadius must be positive.";
			return false;
		}

		message = "World map config is valid.";
		return true;
	}
}
