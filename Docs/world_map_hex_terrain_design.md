# 3D Terrain And Hex Tile World Map Design

## Overview

The grand strategy world map uses a shared source-of-truth workflow:

1. Procedurally generate a medium-resolution terrain information map.
2. Sample that information map to generate the high-detail 3D visual terrain.
3. Sample the same information map to generate hex tile data for gameplay logic.

The important rule is that the 3D terrain and the gameplay tiles are both derived products. Neither one should become the other's source of truth.

```text
procedural_world_generator
        |
        v
terrain_info_map
        |
        +--> Terrain3D visual terrain
        |
        +--> Hex tile logic data
```

This keeps the world stable, reproducible, and easy to rebake when generation rules or hand-authored masks change.

## Design Goals

- Use `Terrain3D` for the visual terrain layer and rely on its clipmap system.
- Use hex tiles for all strategy rules, movement, ownership, resources, combat, and map modes.
- Keep terrain visuals continuous while gameplay remains discrete and deterministic.
- Make world generation reproducible from a seed and a shared configuration resource.
- Allow future hand-authored correction masks without breaking the procedural pipeline.
- Avoid depending on Terrain3D's visible mesh or LOD state for gameplay data.

## Data Ownership

The terrain information map is the world fact layer.

Terrain3D is responsible for visual presentation:

- Height display
- Material blending
- Terrain detail
- Clipmap LOD
- Visual-only noise and surface variation

Hex tile data is responsible for gameplay:

- Movement and pathfinding
- Terrain type
- Biome
- Province and region membership
- Ownership
- Rivers and crossing costs
- Resources
- Supply, roads, ports, cities, and military state

Visual detail may make the terrain look richer, but it must not change gameplay classification. For example, adding small visual bumps to a plain is allowed. Turning a passable plain into a logical mountain only in the visual layer is not allowed.

## Terrain Information Map

The terrain information map should be treated as a bundle of aligned 2D data layers, not as a single image.

Recommended layers:

| Layer | Format | Purpose |
| --- | --- | --- |
| `height_map` | float or 16-bit | Base elevation used by Terrain3D and hex baking |
| `land_mask` | 8-bit or bool | Land and water classification |
| `water_depth` | float or 16-bit | Ocean, lake, and shallow water depth |
| `moisture_map` | float or 16-bit | Biome and vegetation generation |
| `temperature_map` | float or 16-bit | Biome and climate generation |
| `biome_map` | integer id | Semantic biome classification |
| `soil_map` | integer id | Ground type and agricultural hints |
| `river_flow_map` | float or vector | River flow strength and direction |
| `erosion_map` | float | Visual terrain and slope shaping |
| `region_seed_map` | integer id | Province and region generation hints |
| `resource_hint_map` | float or ids | Strategic resource placement hints |

Initial resolution recommendations:

- Prototype: `1024 x 1024`
- Medium world: `2048 x 2048`
- Large world: `4096 x 4096` or higher

Height, moisture, temperature, water depth, and river flow should not use 8-bit precision. Classification maps can use integer ids.

## World Map Configuration

All systems should use the same configuration resource for coordinate conversion and generation settings.

```csharp
public sealed partial class WorldMapConfig : Resource
{
    public Vector2 WorldSize { get; set; } = new(4096.0f, 4096.0f);
    public Vector2I InfoMapSize { get; set; } = new(2048, 2048);
    public float SeaLevel { get; set; } = 0.0f;
    public float MaxHeight { get; set; } = 800.0f;
    public float HexRadius { get; set; } = 16.0f;
    public int Seed { get; set; } = 1;
}
```

The project should define conversion helpers for:

```text
info map pixel
<-> normalized uv
<-> world xz
<-> hex axial q/r
```

No generator, baker, overlay renderer, or input system should define its own independent version of these transforms.

## Procedural Generation Pipeline

The generator produces the terrain information map in stages.

Recommended generation order:

1. Generate continental masks.
2. Generate base elevation.
3. Add mountain ranges, highlands, basins, and lowlands.
4. Apply sea level and water masks.
5. Run simplified erosion or erosion-like shaping.
6. Generate flow direction and flow accumulation.
7. Derive rivers, lakes, and wetlands.
8. Generate temperature from latitude, altitude, and climate rules.
9. Generate moisture from ocean distance, wind direction, altitude, and rain shadow.
10. Classify biomes.
11. Generate strategic region hints.
12. Generate resource placement hints.

The first implementation can use layered noise, ridge noise, distance-to-coast, and simple flow accumulation. Full plate tectonics can be added later if needed.

## Terrain3D Visual Terrain

Terrain3D consumes the terrain information map to build the visual world.

Suggested mapping:

```text
height_map      -> Terrain3D height
biome_map       -> material selection
moisture_map    -> grass, forest, wetland, and dry material weights
slope           -> rock, cliff, and bare ground weights
water_depth     -> ocean, lake, and shoreline rendering
river_flow_map  -> river mesh, decal, or shader mask
erosion_map     -> surface detail and exposed terrain accents
```

Visual terrain height can include detail noise:

```text
visual_height = base_height_from_info_map + biome_detail_noise + slope_detail_noise
```

The detail component should stay within a controlled range. It should improve visual fidelity without changing the strategic meaning of the cell.

Terrain3D should not be used as the authoritative source for gameplay sampling because clipmap LOD and visible mesh topology can change at runtime.

## Hex Tile Logic

Hex tiles are generated from the same terrain information map.

The project should use axial coordinates:

```text
q, r
```

Each tile samples the terrain information map at multiple points:

- Center point
- Six corners
- Six edge midpoints
- Optional internal sample pattern for more stable classification

The baker aggregates these samples into deterministic gameplay values.

```csharp
public sealed class HexTile
{
    public int Q { get; set; }
    public int R { get; set; }

    public Vector2 WorldCenterXZ { get; set; }

    public float CenterHeight { get; set; }
    public float AverageHeight { get; set; }
    public float MinHeight { get; set; }
    public float MaxHeight { get; set; }
    public float Slope { get; set; }

    public bool IsWater { get; set; }
    public bool IsCoastal { get; set; }

    public TerrainKind Terrain { get; set; }
    public BiomeKind Biome { get; set; }

    public float MovementCost { get; set; }
    public float SupplyCost { get; set; }

    public int ProvinceId { get; set; }
    public int RegionId { get; set; }
    public int OwnerId { get; set; }
    public int ResourceId { get; set; }

    public HexRiverEdge[] Rivers { get; set; } = [];
}
```

Terrain classification should be based on stable source data:

```text
height
water depth
slope
biome id
river flow
coast adjacency
```

Example terrain rules:

```text
water depth > threshold          -> ocean or lake
water tile adjacent to land      -> coastal water
land tile adjacent to water      -> coast
slope > mountain threshold       -> mountain
slope > hill threshold           -> hills
biome id == desert               -> desert
otherwise                        -> plains, grassland, forest, tundra, etc.
```

## Rivers

Rivers should be represented as edge data between neighboring hexes, not only as a flag on a tile.

```csharp
public sealed class HexRiverEdge
{
    public int NeighborDirection { get; set; }
    public float Flow { get; set; }
    public RiverKind Kind { get; set; }
}
```

This supports:

- Crossing penalties
- River defense bonuses
- River trade routes
- Supply movement along river systems
- Clear visual alignment with hex boundaries or nearby terrain channels

The river source comes from `river_flow_map`, but the final gameplay representation should be snapped or associated to hex edges.

## Provinces And Regions

Hex tiles are too granular for most grand strategy administration. The recommended hierarchy is:

```text
hex tile -> province -> region -> country
```

Responsibilities:

| Level | Responsibilities |
| --- | --- |
| Hex tile | Movement, combat terrain, local resources, rivers, roads |
| Province | Ownership, population, buildings, taxes, local economy |
| Region | Climate zone, culture area, AI planning area, strategic grouping |
| Country | Diplomacy, war, economy, laws, technology |

Province generation should use terrain and region hints from the information map, but it should be saved as gameplay data after baking.

## Map Modes

Map modes should be rendered from baked gameplay data, not inferred from visual terrain.

Recommended early map modes:

- Terrain
- Political
- Province
- Region
- Biome
- Supply
- Movement cost
- Resource

Rendering options:

1. Hex overlay mesh with per-cell colors.
2. Texture overlay generated from tile data.
3. Shader projection using world coordinates.

The prototype should start with a hex overlay mesh because it is easier to debug.

## Hex Overlay And Selection

The hex overlay should be separate from Terrain3D.

Recommended first implementation:

- Generate hex line meshes or use `MultiMeshInstance3D`.
- Sample display height from the same information map or from a stable Terrain3D height query if available.
- Add a small vertical offset to avoid z-fighting.
- Use separate meshes or material parameters for hover, selection, movement range, and ownership.

Input flow:

1. Cast a ray from the camera into the world.
2. Resolve the hit world `xz`.
3. Convert world `xz` to axial hex coordinates.
4. Look up the baked `HexTile`.
5. Display hover, selection, and tile information from tile data.

Gameplay selection should not depend on visible Terrain3D mesh topology.

## Godot Project Structure

Recommended project layout:

```text
res://world/
├── configs/
│   └── world_map_config.tres
├── generated/
│   ├── height_map.exr
│   ├── moisture_map.exr
│   ├── temperature_map.exr
│   ├── biome_map.png
│   ├── river_flow_map.exr
│   └── hex_tiles.tres
├── terrain/
│   └── terrain3d_data/
└── scenes/
    └── world_map.tscn
```

Recommended C# systems:

```text
WorldMapConfig
TerrainInfoMap
WorldGenerator
Terrain3DBaker
HexTileBaker
HexCoordinateUtility
HexOverlayRenderer
MapModeRenderer
WorldMapInputController
```

Generated and baked data should be clearly separated from hand-authored source scenes and scripts.

## Editor Workflow

Recommended workflow:

1. Edit `WorldMapConfig`.
2. Generate the terrain information map from seed and configuration.
3. Inspect debug views for height, land mask, rivers, moisture, temperature, and biome.
4. Apply optional hand-authored masks or corrections.
5. Bake Terrain3D data.
6. Bake hex tile data.
7. Open the world scene and verify overlay alignment.
8. Save generated resources.

When the world needs structural changes, update the terrain information map or generation masks and rebake. Avoid manually editing derived Terrain3D data or hex data unless a specific tool records that change back into the source layer.

## Prototype Scope

The first vertical slice should include:

- `1024 x 1024` terrain information map.
- One Terrain3D terrain generated from the height map.
- Around `128 x 128` hex tiles.
- Hex overlay aligned to the terrain.
- Mouse hover and click selection.
- Tile inspection panel showing height, slope, terrain, biome, water, coast, and movement cost.
- Terrain classification for ocean, coast, plains, hills, mountains, forest, and desert.
- Basic river edge data.
- One simple political overlay.

This validates the full pipeline without committing to final world scale or final generation quality.

## Implementation Notes

- Prefer C# for all generation, baking, and runtime map logic.
- Keep tile data in plain data structures or resources rather than one node per tile.
- Keep generated paths stable once referenced by scenes or resources.
- Use Terrain3D for visual terrain, not as the gameplay data authority.
- Use deterministic generation from `WorldMapConfig.Seed`.
- Store enough metadata with generated outputs to know which config and seed produced them.
- Keep rebaking idempotent where possible.

## Open Questions

- Final world scale in meters.
- Final hex radius and number of playable tiles.
- Whether the world wraps horizontally.
- Whether rivers should visually follow natural terrain channels or be snapped closer to hex edges.
- How much hand-authored map correction is needed.
- Whether generated information maps should be stored as Godot resources, image files, or a custom binary format.
