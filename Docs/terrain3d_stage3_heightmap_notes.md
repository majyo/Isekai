# Terrain3D 阶段 3 HeightMap 写入记录

日期：2026-05-20

## 目标

把正式 `TerrainInfoMap.HeightMap` 接入 `Terrain3DData.import_images(...)`，让主世界视觉地形切到 Terrain3D，同时保留 ArrayMesh debug preview 作为故障定位路径。

## 本次实现

- `WorldMapConfig.VisualTerrainMode` 新增两种模式：
  - `ArrayMeshPreview`
  - `Terrain3D`
- `world/configs/world_map_config.tres` 已切到 `Terrain3D`。
- `Terrain3DBaker` 在 Terrain3D 模式下会：
  - 通过 `ClassDB.Instantiate("Terrain3D")` 创建 `terrain3d_visual_terrain` 节点。
  - 从节点取得初始化后的 `Terrain3DData`。
  - 将 `TerrainInfoMap.HeightMap` 写入 `Image.Format.Rf` 高度图。
  - 用 `import_images(images, import_position, height_offset, height_scale)` 导入高度。
  - 调用 `update_maps(...)` 和 `calc_height_range(true)`。
  - 保存到 `res://world/terrain/generated/terrain3d_data/`。
  - 在 `visual_terrain_bake_report.txt` 中记录模式、数据目录、region 数量、保存文件数量和高度抽样误差。
- Terrain3D 写入失败时会清空半成品节点，并创建 `terrain_preview_mesh` 作为 ArrayMesh debug preview。
- `WorldMapMvpValidator` 现在会根据 `VisualTerrainMode` 检查 Terrain3D 节点或 ArrayMesh debug preview。

## 坐标和高度映射

- 高度图尺寸直接使用 `TerrainInfoMap.Size`。
- Terrain3D `vertex_spacing` 使用：

```text
min(WorldSize.X / InfoMapSize.X, WorldSize.Y / InfoMapSize.Y)
```

- 导入位置使用世界左上角：

```text
(-WorldSize.X / 2, 0, -WorldSize.Y / 2)
```

这个选择对应 Terrain3D 的规则：`vertex_spacing` 会横向缩放 region，所有 global position 都应使用缩放后的绝对世界坐标。

## 验证状态

已通过：

```powershell
dotnet build Isekai.csproj
```

已补跑：

```powershell
& 'D:\workspace\godot\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe' --headless --path . --quit
```

结果：

- `EffectiveVisualTerrainMode: Terrain3D`
- `RegionCount: 16`
- `SavedFileCount: 16`
- `MaxHeightSampleError: 0.579`
- `world_map_mvp_validation_report.txt` PASS

阶段 3 的 Terrain3D 高度写入、区域坐标映射和抽样误差报告已通过 Godot runtime 验证。
