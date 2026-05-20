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

这样即使还没有正式 Terrain3D texture/control 资产，也能用 color map 表达主要地貌。

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
```

仍待 Godot runtime 验证：

- Terrain3D 是否成功导入 `TYPE_COLOR`。
- `show_colormap` 是否在实际材质上生效。
- `visual_terrain_bake_report.txt` 是否出现颜色覆盖比例。
- `world_map_mvp_validation_report.txt` 是否仍 PASS。

当前环境仍未发现 AGENTS.md 中记录的 Godot 可执行文件路径，`godot` 也不在 `PATH`。
