# 2D TileMap 地形重构计划

## 目标

移除当前项目中的 `Terrain3D` 视觉地形系统，把世界地图的正式视觉表现切换为 2D hex `TileMapLayer`。重构后：

- `TerrainInfoMap` 继续作为世界生成、海拔、生物群系、湿度、温度、河流流量的权威数据源。
- `HexTileMap` 继续作为玩法 tile、移动成本、归属、区域、河流边的权威数据源。
- 视觉地形由 Godot 2D `TileMapLayer` 绘制，不再依赖 `Terrain3D`、`MeshInstance3D` 地形、3D 水面 plane 或 Terrain3D 生成数据。
- 输入命中、hover、selection、overlay 和地图模式全部转为 2D 坐标流程。

## 非目标

- 不重写世界生成算法。
- 不改变 `TerrainInfoMap` 和 `HexTileMap` 的资源格式，除非某一阶段明确需要兼容字段。
- 不在第一版实现复杂自动地形过渡、海岸 autotile、动画水面或 2D shader 高级效果。
- 不把 Terrain3D 的高度场语义强行迁移到 2D；高度只作为分类、信息面板和调试数据使用。

## 架构方向

当前系统可拆成三层：

```text
TerrainInfoMap
  -> HexTileMap
      -> 视觉地形 / overlay / 输入命中
```

重构后保持前两层稳定，只替换第三层：

```text
TerrainInfoMap
  -> HexTileMap
      -> HexTileTerrainRenderer   -> TileMapLayer
      -> HexOverlayRenderer2D     -> Node2D draw / overlay TileMapLayer
      -> WorldMapInputController2D -> Camera2D mouse world position
```

## 进度总览

| 阶段 | 状态 | 目标 |
| --- | --- | --- |
| TM-0 | [x] | 锁定范围和备份清单 |
| TM-1 | [x] | 引入 2D TileSet 和基础地形渲染器 |
| TM-2 | [x] | 调整世界地图 pipeline 顺序 |
| TM-3 | [x] | 场景从 3D 世界切换到 2D 世界 |
| TM-4 | [x] | overlay 和地图模式 2D 化 |
| TM-5 | [x] | 输入命中和 tile inspector 2D 化 |
| TM-6 | [x] | MVP 验证器和报告改为 TileMap 口径 |
| TM-7 | [x] | 清理 Terrain3D 代码、插件和生成产物 |
| TM-8 | [x] | 视觉 polish 和性能检查 |

## 完成记录

状态：2026-05-28 已完成。

验证证据：

- `dotnet build` 通过，0 error / 0 warning。
- Godot headless 回归命令通过，主场景完整执行 2D pipeline。
- `world/generated/world_map_mvp_validation_report.txt` 显示 `VisualTerrain: TileMap2D`，结果为 `PASS`。
- `world/generated/tilemap_terrain_render_report.txt` 显示 `TileCount: 16384`，8 类 `TerrainKind` 均有 TileMap 渲染覆盖。
- `world/tilesets/world_hex_tileset.tres` 和 `world/tilesets/world_hex_tiles.png` 已生成。
- `world/scripts/`、`world/scenes/`、`project.godot` 中不再包含 Terrain3D 主流程、3D 地形、3D 相机或 3D mesh overlay 引用。

## 阶段 TM-0：范围锁定

目标：确认哪些系统保留、替换、删除，降低大范围删除风险。

| ID | 状态 | 优先级 | 任务 | 文件 / 范围 | 验收 |
| --- | --- | --- | --- | --- | --- |
| TM-0001 | [x] | P0 | 列出 Terrain3D 直接引用 | `world/scripts/`, `world/scenes/`, `Docs/`, `project.godot` | 有一份待删除/待替换清单 |
| TM-0002 | [x] | P0 | 确认 `TerrainInfoMap` 保留为权威采样层 | `TerrainInfoMap.cs`, `WorldGenerator.cs` | 生成流程不因视觉降维而变化 |
| TM-0003 | [x] | P0 | 确认 `HexTileMap` 保留为玩法 tile 层 | `HexTileMap.cs`, `HexTileBaker.cs` | tile count、terrain 分类、河流边数据继续可生成 |
| TM-0004 | [x] | P1 | 记录删除前可回退点 | git branch / commit | 能从当前 Terrain3D 版本回退 |

验收标准：

- 新方案只替换视觉、overlay、输入和验证口径。
- 不删除 `.godot/` 下生成缓存。
- 不直接手改 `project.godot` 的插件状态，优先通过 Godot editor 禁用 Terrain3D。

## 阶段 TM-1：2D TileSet 和基础地形渲染器

目标：建立第一条可见的 2D 地形绘制路径。

建议新增：

```text
world/tilesets/world_hex_tileset.tres
world/scripts/HexTileTerrainRenderer.cs
```

| ID | 状态 | 优先级 | 任务 | 文件 / 范围 | 验收 |
| --- | --- | --- | --- | --- | --- |
| TM-0101 | [x] | P0 | 创建 hex TileSet 资源 | `world/tilesets/world_hex_tileset.tres` | 至少包含 8 个 terrain tile |
| TM-0102 | [x] | P0 | 新增 `HexTileTerrainRenderer` | `world/scripts/HexTileTerrainRenderer.cs` | 可根据 `HexTileMap.Terrain` 写入 TileMap cells |
| TM-0103 | [x] | P0 | 建立 terrain 到 atlas 坐标映射 | 新 renderer 或配置资源 | `Ocean` 到 `Mountains` 全部有合法 tile |
| TM-0104 | [x] | P1 | 输出基础渲染报告 | `world/generated/tilemap_terrain_render_report.txt` | 记录 tile count、terrain coverage、missing tile 数 |
| TM-0105 | [x] | P1 | 保留临时纯色占位 tile | `world/tilesets/` | 没有最终美术也能验证流程 |

实现约定：

- TileMap cell 坐标使用 `gridX/gridY`，不要直接使用可能为负的 axial `q/r`。
- `tileIndex = gridY * config.TargetHexGridSize.X + gridX`。
- 第一版只负责填底图；边界、政治色、hover、selection 放到后续 overlay。

## 阶段 TM-2：调整世界地图 pipeline

目标：让视觉地形依赖 `HexTileMap`，而不是在 hex bake 之前运行。

当前顺序：

```text
GenerateTerrainInfoMap
BakeVisualTerrain
BakeHexTiles
BakeRiverEdges
RenderHexOverlay
```

目标顺序：

```text
GenerateTerrainInfoMap
BakeHexTiles
BakeRiverEdges
RenderVisualTerrain
RenderHexOverlay
ExportTerrainQuality
ValidateMvpPipeline
```

| ID | 状态 | 优先级 | 任务 | 文件 / 范围 | 验收 |
| --- | --- | --- | --- | --- | --- |
| TM-0201 | [x] | P0 | 将 `BakeVisualTerrainOnReady` 改为 `RenderVisualTerrainOnReady` | `WorldMapPrototype.cs` | 命名表达 TileMap 渲染而非 3D bake |
| TM-0202 | [x] | P0 | 调整 `_Ready()` pipeline 顺序 | `WorldMapPrototype.cs` | visual render 在 hex bake 和 river bake 后执行 |
| TM-0203 | [x] | P0 | 调整 `RunFullRebakePipeline()` 顺序 | `WorldMapPrototype.cs` | 手动 rebake 顺序一致 |
| TM-0204 | [x] | P1 | 删除 `TerrainRootPath` 依赖或改名为 `TerrainTileMapPath` | `WorldMapPrototype.cs`, scene | 不再查找 `Node3D terrain_root` |
| TM-0205 | [x] | P1 | 将 visual report 路径改名 | `WorldMapPrototype.cs` | 报告名不再包含 Terrain3D 语义 |

验收标准：

- 没有 `Terrain3DBaker` 被主流程实例化。
- `HexTileTerrainRenderer.Render(TerrainInfoMap, HexTileMap, Config)` 能在 pipeline 内运行。

## 阶段 TM-3：场景从 3D 切换到 2D

目标：把主地图场景降维，删除 3D camera、light、terrain root。

建议目标结构：

```text
world_map: Node2D
  terrain_tilemap: TileMapLayer
  map_mode_overlay: Node2D
  hex_overlay: Node2D
  markers: Node2D
  world_map_input: Node
  camera_controller: Node2D
    camera: Camera2D
```

| ID | 状态 | 优先级 | 任务 | 文件 / 范围 | 验收 |
| --- | --- | --- | --- | --- | --- |
| TM-0301 | [x] | P0 | 将 `world_map.tscn` 根节点改为 `Node2D` | `world/scenes/world_map.tscn` | 场景可加载 |
| TM-0302 | [x] | P0 | 替换 `terrain_root` 为 `terrain_tilemap` | `world/scenes/world_map.tscn` | TileMapLayer 节点存在 |
| TM-0303 | [x] | P0 | 替换 `Camera3D` 为 `Camera2D` | `world/scenes/world_map.tscn`, camera controller | 可平移缩放查看地图 |
| TM-0304 | [x] | P1 | 删除 `DirectionalLight3D` | `world/scenes/world_map.tscn` | 2D 场景无 3D 光源 |
| TM-0305 | [x] | P1 | 将 `scenes/main.tscn` 根节点改为 `Node` 或 `Node2D` | `scenes/main.tscn` | 主场景可运行 |

注意：

- 如果一次性改 scene 风险过高，可以先新增 `world_map_2d.tscn` 验证，再切主场景引用。
- `FreeLookCameraController.cs` 应拆出或替换为 `MapCamera2DController.cs`，避免在 2D 场景里保留 3D 语义。

## 阶段 TM-4：overlay 和地图模式 2D 化

目标：把网格、terrain mode、political mode、hover、selection 从 mesh overlay 改成 2D 绘制。

| ID | 状态 | 优先级 | 任务 | 文件 / 范围 | 验收 |
| --- | --- | --- | --- | --- | --- |
| TM-0401 | [x] | P0 | 将 `HexOverlayRenderer` 改为 `Node2D` | `HexOverlayRenderer.cs` | 不再引用 `SurfaceTool` / `MeshInstance3D` |
| TM-0402 | [x] | P0 | 用 `_Draw()` 绘制 hex grid | `HexOverlayRenderer.cs` | grid 覆盖所有 tile |
| TM-0403 | [x] | P0 | 用 `_Draw()` 绘制 hover 和 selection polygon | `HexOverlayRenderer.cs` | 鼠标移动和点击反馈正常 |
| TM-0404 | [x] | P1 | terrain/political 地图模式改为 2D fill layer | `HexOverlayRenderer.cs` 或独立 renderer | 可切换地图模式 |
| TM-0405 | [x] | P1 | 河流边绘制 2D 化 | `HexOverlayRenderer.cs` 或 `HexRiverOverlayRenderer.cs` | `RiverKindByEdge` 可见 |

实现约定：

- 继续复用 `WorldMapCoordinateUtility.GetHexCornerWorldXz()` 来计算 hex 顶点。
- 2D 屏幕平面把当前 `WorldXz.X` 映射到 `Vector2.X`，`WorldXz.Y` 映射到 `Vector2.Y`。
- 若短期保留 `WorldXz` 命名，文档和注释要说明它现在代表地图平面坐标。

## 阶段 TM-5：输入命中 2D 化

目标：删除 3D raycast，把鼠标坐标直接转 tile。

| ID | 状态 | 优先级 | 任务 | 文件 / 范围 | 验收 |
| --- | --- | --- | --- | --- | --- |
| TM-0501 | [x] | P0 | 将 camera 类型从 `Camera3D` 改为 `Camera2D` | `WorldMapInputController.cs` | 可获取当前 2D camera |
| TM-0502 | [x] | P0 | 删除 raycast 采样流程 | `WorldMapInputController.cs` | 不再使用 `ProjectRayOrigin` / `ProjectRayNormal` |
| TM-0503 | [x] | P0 | 鼠标世界坐标转 axial | `WorldMapInputController.cs` | hover tile 正确 |
| TM-0504 | [x] | P0 | 点击 selection 继续更新 inspector | `WorldMapInputController.cs` | 信息面板仍显示 tile 数据 |
| TM-0505 | [x] | P1 | 输入命中边界检查改为 2D world bounds | `WorldMapInputController.cs` | 地图外不会误选 |

目标命中流程：

```text
mouseScreen
  -> Camera2D / viewport global mouse position
  -> Vector2 mapWorldPosition
  -> WorldMapCoordinateUtility.WorldXzToAxial(mapWorldPosition, config)
  -> HexTileMap.TryGetTileIndex()
```

## 阶段 TM-6：验证器和报告改为 TileMap 口径

目标：自动报告能证明当前跑的是 2D TileMap 视觉地形，而不是 Terrain3D。

| ID | 状态 | 优先级 | 任务 | 文件 / 范围 | 验收 |
| --- | --- | --- | --- | --- | --- |
| TM-0601 | [x] | P0 | 删除 Terrain3D 节点存在性检查 | `WorldMapMvpValidator.cs` | 不再引用 Terrain3D class name |
| TM-0602 | [x] | P0 | 删除 Terrain3D texture/control/height consistency 检查 | `WorldMapMvpValidator.cs` | 验证器无 Terrain3D API 调用 |
| TM-0603 | [x] | P0 | 新增 TileMapLayer cell count 检查 | `WorldMapMvpValidator.cs` | cell 数等于 `HexTileMap.TileCount` |
| TM-0604 | [x] | P1 | 新增 terrain atlas 映射完整性检查 | `WorldMapMvpValidator.cs` / renderer | 8 类 terrain 无 missing tile |
| TM-0605 | [x] | P1 | 更新 MVP 报告文案 | `world/generated/world_map_mvp_validation_report.txt` | 报告明确 `VisualTerrain: TileMap2D` |

验收标准：

- `rg -n "Terrain3D|Terrain3DBaker" world/scripts` 只剩归档或待删除文件。
- MVP 报告不再出现 `Terrain3DPluginAvailable`。

## 阶段 TM-7：Terrain3D 清理

目标：删除旧视觉系统和生成产物，避免未来维护双系统。

| ID | 状态 | 优先级 | 任务 | 文件 / 范围 | 验收 |
| --- | --- | --- | --- | --- | --- |
| TM-0701 | [x] | P0 | 删除 `Terrain3DBaker.cs` | `world/scripts/Terrain3DBaker.cs` | 主项目可编译 |
| TM-0702 | [x] | P0 | 删除 Terrain3D spike | `Terrain3DSpikeRunner.cs`, `terrain3d_spike.tscn` | 无主流程引用 |
| TM-0703 | [x] | P0 | 禁用 Terrain3D 插件 | Godot editor / `project.godot` | `[editor_plugins]` 不再启用 Terrain3D |
| TM-0704 | [x] | P1 | 删除 `addons/terrain_3d/` | `addons/terrain_3d/` | Godot 加载无 GDExtension 错误 |
| TM-0705 | [x] | P1 | 清理 Terrain3D 生成目录 | `world/terrain/generated/terrain3d_data`, `world/terrain/generated/textures` | 不再有 Terrain3D 派生产物 |
| TM-0706 | [x] | P2 | 将旧 Terrain3D 文档归档 | `Docs/archive/` | 当前 Docs 根目录只保留新方案和活跃文档 |

注意：

- 如果 `project.godot` 必须手改，先确认 Godot editor 无法完成或变更足够小。
- 删除插件前先跑一次 headless，确认 2D 分支完全不依赖 Terrain3D class。

## 阶段 TM-8：视觉 polish 和性能检查

目标：在基础迁移完成后提升读图质量。

| ID | 状态 | 优先级 | 任务 | 文件 / 范围 | 验收 |
| --- | --- | --- | --- | --- | --- |
| TM-0801 | [x] | P1 | 海岸 tile 过渡规则 | TileSet / renderer | 海岸边界不再只有单色块 |
| TM-0802 | [x] | P1 | 水面轻量动画或 shader | TileSet / material | 海洋和河流更易读 |
| TM-0803 | [x] | P1 | 山地/丘陵视觉区分 | TileSet art | 地形分类可一眼识别 |
| TM-0804 | [x] | P2 | overlay 分层性能检查 | overlay renderers | 128x128 tile 下交互流畅 |
| TM-0805 | [x] | P2 | 地图缩放下线宽和字体适配 | camera / overlay | 缩放时 grid、highlight 不糊不遮挡 |

## 回归命令

优先使用：

```powershell
& 'D:\workspace\godot\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe' --headless --path . --quit
```

若 Godot 在 `PATH`：

```powershell
godot --headless --path . --quit
```

建议每完成一个阶段至少跑一次 headless；TM-3、TM-5、TM-8 还需要打开 editor/runtime 做人工视觉验证。

## 风险与应对

| 风险 | 影响 | 应对 |
| --- | --- | --- |
| 2D TileMap 与现有 axial 坐标错位 | hover/selection 不准 | cell 坐标使用 gridX/gridY，axial 只用于玩法查找和几何计算 |
| 一次性改 scene 导致主场景打不开 | 阻塞验证 | 先新增 `world_map_2d.tscn` 验证，再切主场景 |
| TileSet 资源手写复杂 | 容易出现格式错误 | 优先用 Godot editor 创建 TileSet，代码只引用资源 |
| 删除 Terrain3D 太早 | 失去回退路径 | TM-7 必须在 TileMap 视觉、输入、验证都通过后执行 |
| 2D 降维后高度信息丢失 | 视觉层不表达山势 | 第一版用 terrain 分类表达，后续用 tile variant、阴影、等高线或 shader 增强 |

## 完成定义

- `world_map.tscn` 是 2D 地图场景。
- 正式视觉地形由 `TileMapLayer` 绘制。
- 主流程不实例化 `Terrain3DBaker`。
- 输入命中不使用 3D raycast。
- overlay、hover、selection、地图模式在 2D 下可用。
- MVP 验证报告以 TileMap2D 为视觉地形口径。
- Terrain3D 插件、spike、生成目录和主流程引用已清理或归档。
