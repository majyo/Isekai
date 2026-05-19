# 大战略 3D 地形与六边形大地图 MVP 完成说明与后续路线

## 当前状态

大地图 MVP 已完成从世界生成到交互检查的端到端闭环。

当前系统已经可以：

- 从统一配置生成地形信息图。
- 使用同一份地形信息图派生 3D 视觉地形预览。
- 使用同一份地形信息图烘焙六边形玩法 tile。
- 将河流转换为 hex edge 数据。
- 渲染贴合地形高度的六边形 overlay。
- 支持地形/政治地图模式。
- 支持 free look 第一人称相机预览。
- 支持鼠标 hover、点击选择和 tile 信息面板。
- 输出 MVP 验证报告，确认完整 pipeline 可重复执行。

MVP 验证报告位于：

```text
res://world/generated/world_map_mvp_validation_report.txt
```

该文件是可重建生成产物，按 `.gitignore` 不提交到仓库。

## 当前架构

当前大地图遵循一条核心原则：

```text
TerrainInfoMap 是世界事实层。
视觉地形、hex tile、overlay 和输入命中都从 TerrainInfoMap 派生。
```

数据流如下：

```text
WorldMapConfig
    ↓
WorldGenerator
    ↓
TerrainInfoMap
    ↓
    ├── Terrain3DBaker / ArrayMesh terrain preview
    ├── HexTileBaker
    ├── HexRiverEdgeBaker
    ├── HexOverlayRenderer
    └── WorldMapInputController
```

这样可以避免玩法数据依赖 Terrain3D 的当前可见 mesh 或 clipmap LOD。

## 已完成模块

### 项目结构与配置

核心配置资源：

```text
res://world/configs/world_map_config.tres
```

核心脚本：

```text
res://world/scripts/WorldMapConfig.cs
res://world/scripts/WorldMapPrototype.cs
res://world/scripts/WorldMapDebugLogger.cs
```

当前默认参数：

```text
world size: 4096 x 4096
info map: 1024 x 1024
hex grid: 128 x 128
hex radius: 16
seed: 1337
```

### 地形信息图

已实现：

- 高度图。
- 陆地/水域 mask。
- 湿度图。
- 温度图。
- biome 图。
- 河流流量图。
- debug PNG 导出。
- 可重复生成。

核心脚本：

```text
res://world/scripts/TerrainInfoMap.cs
res://world/scripts/WorldGenerator.cs
res://world/scripts/TerrainInfoMapDebugExporter.cs
```

### 坐标系统

已实现统一转换：

```text
info pixel <-> uv <-> world xz <-> axial hex
```

hex 使用 pointy-top axial 坐标。世界地图中心为 `world XZ = (0, 0)`。

核心脚本：

```text
res://world/scripts/WorldMapCoordinateUtility.cs
res://world/scripts/WorldMapCoordinateValidator.cs
```

### 视觉地形

当前项目已接入 Terrain3D 插件，但真实 Terrain3D 数据写入路径尚未实现。

MVP 当前使用 `ArrayMesh` 预览 fallback：

- 从 `TerrainInfoMap.HeightMap` 采样高度。
- 生成 3D 地形预览 mesh。
- 根据 biome/高度着色。
- 添加临时水面。

核心脚本：

```text
res://world/scripts/Terrain3DBaker.cs
```

### 卷轴俯视相机

已实现大战略式地形预览相机：

```text
res://world/scripts/FreeLookCameraController.cs
```

操作方式：

```text
WASD：上下左右平移视野
鼠标滚轮：拉近 / 拉远
```

### Hex Tile 烘焙

已实现 `128 x 128` hex tile 烘焙，共 `16,384` 个 tile。

每个 tile 包含：

- axial 坐标。
- world center XZ。
- 中心高度。
- 平均高度。
- 最低/最高高度。
- 坡度。
- 水域/海岸。
- terrain kind。
- biome kind。
- movement cost。
- province/region/owner/resource 占位数据。

核心脚本：

```text
res://world/scripts/HexTile.cs
res://world/scripts/HexTileMap.cs
res://world/scripts/HexTileBaker.cs
res://world/scripts/TerrainKind.cs
res://world/scripts/BiomeKind.cs
```

### 河流边数据

河流已从信息图转换为 hex edge 数据。

当前表现为：

- 河流属于两个相邻 tile 之间的边。
- 同一条边会写入双方 tile 的相反方向。
- 支持小河/大河分类。
- 支持跨河移动消耗修正。
- 烘焙报告会检查双向一致性。

核心脚本：

```text
res://world/scripts/HexRiverEdge.cs
res://world/scripts/RiverKind.cs
res://world/scripts/HexRiverEdgeBaker.cs
res://world/scripts/HexRiverEdgeDebugExporter.cs
```

### Hex Overlay

当前 overlay 不是从 Terrain3D mesh 投影得到，而是：

1. 在 `world XZ` 平面计算 hex 中心和角点。
2. 用 `TerrainInfoMap` 采样每个点的高度。
3. 生成贴合地形的边线和填色 mesh。

已支持：

- 六边形边框。
- 地形地图模式。
- 政治地图模式。
- hover 高亮 mesh。
- selection 高亮 mesh。

核心脚本：

```text
res://world/scripts/HexOverlayRenderer.cs
res://world/scripts/HexMapMode.cs
```

### 输入与信息面板

已实现：

- 鼠标 hover tile。
- 左键点击选择 tile。
- 右上角 tile 信息面板。
- 信息面板显示地形、biome、高度、坡度、水域、海岸、移动消耗、owner、region、河流数量等。

输入命中不依赖 Terrain3D 或预览 mesh 的碰撞体，而是：

1. 从相机发出屏幕射线。
2. 沿射线采样 `TerrainInfoMap` 高度。
3. 求出地形命中点。
4. 将 world XZ 转换成 axial hex。
5. 查询 `HexTileMap`。

核心脚本：

```text
res://world/scripts/WorldMapInputController.cs
```

### MVP 验证

已实现 MVP 验证器：

```text
res://world/scripts/WorldMapMvpValidator.cs
```

验证内容包括：

- 配置有效性。
- 坐标往返。
- 地形信息图尺寸和数组完整性。
- seed 生成确定性。
- hex tile 数量。
- axial 查询。
- 地形分类多样性。
- 海岸识别。
- 河流边双向一致性。
- 视觉地形预览存在。
- overlay 完整渲染。
- hover/selection mesh 存在。
- input controller 初始化。
- 政治 overlay owner id。
- 玩法数据不依赖 Terrain3D mesh。

## 如何运行

编辑器运行：

```text
godot --editor --path .
```

运行项目：

```text
godot --path .
```

headless 验证：

```text
godot --headless --path . --quit
```

如果 `godot` 不在 PATH，可使用本机 Godot 4.6.2 Mono 可执行文件。

当前根场景：

```text
res://scenes/main.tscn
```

大地图原型场景：

```text
res://world/scenes/world_map.tscn
```

## 生成产物

以下目录保存运行时生成/烘焙产物：

```text
res://world/generated/
```

典型产物：

```text
terrain_info_map.res
hex_tiles.res
height_map_debug.png
biome_map_debug.png
river_flow_map_debug.png
hex_tiles_terrain_debug.png
hex_river_edges_debug.png
coordinate_validation_report.txt
hex_tile_bake_report.txt
hex_river_edge_bake_report.txt
world_map_mvp_validation_report.txt
```

这些文件都是可重建产物，不提交到 Git。

## 当前已知限制

- Terrain3D 插件已在项目中启用，但还没有把 `TerrainInfoMap` 写入 Terrain3D 数据资源。
- 当前视觉地形仍是 `ArrayMesh` 预览 fallback。
- 河流已有 hex edge 玩法数据，但还没有独立河流视觉 mesh。
- tile inspector 是开发调试 UI，不是最终游戏 HUD。
- 政治 overlay 使用 owner id 占位数据，还没有真实国家/省份系统。
- province id 和 resource id 仍是占位。
- movement cost 已有基础值，但还没有寻路系统读取。
- generated 目录可重复生成，但目前还没有单独的编辑器按钮或菜单命令。

## 后续开发方向

### 1. 真实 Terrain3D 烘焙

目标：替换当前 `ArrayMesh` 预览 fallback，让 Terrain3D 成为正式视觉地形。

建议任务：

- 调研 Terrain3D C#/GDExtension API 的高度写入方式。
- 将 `TerrainInfoMap.HeightMap` 转成 Terrain3D region 数据。
- 建立 Terrain3D data 存储路径。
- 将 biome/slope/moisture 转成 Terrain3D 材质权重。
- 保留 `ArrayMesh` fallback 作为 debug 模式。
- 更新 MVP 验证器，让它区分真实 Terrain3D 和 fallback。

### 2. 河流视觉 mesh

目标：让已烘焙的 hex river edge 数据在地图上可见。

建议任务：

- 基于 `HexRiverEdge` 生成 river line/strip mesh。
- 小河和大河使用不同宽度与颜色。
- 采样信息图高度，让河流贴地显示。
- 避免与 hex overlay z-fighting。
- 后续可改为沿自然河谷而不是完全贴 hex edge。

### 3. 省份与区域系统

目标：从 tile 层聚合出大战略可管理的 province/region。

建议任务：

- 定义 `Province` 数据结构。
- 根据地形、水域、region seed 和连通性聚合 tile。
- 给 province 分配中心、边界、面积、地形摘要。
- 支持 province overlay。
- 支持点击 province 与点击 tile 两种检查模式。

### 4. 移动与寻路

目标：验证 hex tile 的 gameplay 数据是否能支持实际单位移动。

建议任务：

- 实现 hex 邻接查询。
- 实现基于 movement cost 的 A*。
- 读取 terrain movement cost。
- 读取 river crossing modifier。
- 区分陆地、海洋、海岸移动规则。
- 添加 movement range overlay。

### 5. 资源与城市/港口 marker

目标：让地图开始具备战略价值点。

建议任务：

- 基于 biome、地形、高度、海岸生成资源 hint。
- 定义 resource id 与资源类型。
- 生成城市、港口、矿点等 marker。
- marker 不应是一 tile 一节点，数量应可控。
- 支持资源地图模式。

### 6. 地图模式系统化

目标：从当前地形/政治 overlay 扩展成统一地图模式框架。

建议任务：

- 抽象 `MapModeRenderer`。
- 支持 terrain、political、province、region、supply、movement、resource。
- 支持快捷键切换。
- 支持 overlay alpha 和可见性控制。
- 将 tile inspector 与当前地图模式联动。

### 7. 编辑器工具与一键 Rebake

目标：把当前启动时自动 pipeline 变成可控的开发工具。

建议任务：

- 添加 editor tool 或菜单按钮。
- 支持单独执行 Generate Info Map、Bake Terrain、Bake Tiles、Bake Rivers、Render Overlay。
- 支持保存/加载 bake 配置。
- 支持手动清理 generated。
- 让验证报告在编辑器内可见。

### 8. 存档与数据格式

目标：为后续游戏运行时加载准备稳定格式。

建议任务：

- 评估 `.res`、`.tres`、自定义 binary、JSON 的边界。
- 将大数组数据从 Resource 导出字段迁移到更适合的二进制格式。
- 记录 bake version。
- 支持检测配置变化后提示重新 bake。

### 9. 性能与规模测试

目标：确认地图规模扩大后的性能边界。

建议任务：

- 测试 `256 x 256` hex tile。
- 测试更高分辨率信息图。
- 分离 debug overlay 和正式 overlay。
- 优化 overlay mesh 顶点重复。
- 评估 MultiMesh 或 shader overlay。
- 记录生成耗时、烘焙耗时、运行时 FPS。

## 推荐下一步

建议优先推进：

1. 真实 Terrain3D 烘焙。
2. 河流视觉 mesh。
3. 移动与寻路。

理由：

- Terrain3D 烘焙能替换当前最大技术债。
- 河流视觉能让已完成的 river edge 数据在地图上变得可读。
- 移动与寻路能立刻验证 terrain cost 和 river crossing cost 的玩法价值。
