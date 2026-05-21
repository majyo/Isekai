# Terrain3D 阶段 4 Color Map 记录

日期：2026-05-21

## 目标

在 Terrain3D 高度写入路径上继续加入可读地貌视觉，让 Terrain3D 不只是灰色高度场。

## 本次实现

`Terrain3DBaker` 现在会在 Terrain3D 模式下额外生成 `TYPE_COLOR` 图，并和高度图一起传给：

```text
Terrain3DData.import_images(images, import_position, height_offset, height_scale)
```

颜色图来源仍然是 `TerrainInfoMap`，不会反向影响玩法数据。

## 颜色规则

基础颜色来自 `BiomeMap`：

- `Ocean`
- `Coast`
- `Grassland`
- `Forest`
- `Desert`
- `Tundra`
- `Hills`
- `Mountain`

后处理规则：

- 高坡度或高海拔区域混入岩石色，并计入 `Rock` 覆盖。
- 有 `RiverFlowMap` 的陆地区域混入湿润河岸色，并计入 `Riverbank` 覆盖。

## Terrain3D 材质显示

当 `Terrain3DMaterial` 可用时，baker 会为 Terrain3D 节点配置：

```text
show_checkered = false
show_colormap = true
```

阶段 7 之后，color map 继续作为整体地貌 tint；正式 texture/control 资产负责增加材质细节。

## 报告输出

`visual_terrain_bake_report.txt` 会新增：

- `ColorImageSize`
- `ColormapMaterialConfigured`
- `ColorLayerCoverage`

`ColorLayerCoverage` 会记录每类颜色层的像素数量和比例。

## 验证状态

已通过：

```powershell
dotnet build Isekai.csproj
& 'D:\workspace\godot\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe' --headless --path . --quit
```

Godot runtime 验证结果：

- `EffectiveVisualTerrainMode: Terrain3D`
- `ColorImageSize: (1024, 1024)`
- `ColormapMaterialConfigured: True`
- `ColorLayerCoverage` 已输出。
- `world_map_mvp_validation_report.txt` PASS。

## 视觉修正

2026-05-21 追加修正：

- 原岩石混色规则过于激进，导致大部分陆地被 `Rock` 覆盖成灰色。
- 岩石权重现在主要作用于 `Mountain` 和 `Hills`，普通草地、森林、海岸和沙漠只在极陡坡上获得轻微岩石混色。
- `ColorLayerCoverage` 中 `Rock` 从约 `40.82%` 降到约 `1.74%`，正常 biome 颜色恢复可见。
