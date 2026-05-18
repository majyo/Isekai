# 大战略 3D 地形与六边形大地图 MVP 开发计划

## MVP 目标

构建一个可运行、可检查、可反复烘焙的大战略大地图垂直原型。

该 MVP 要验证一条完整的数据链路：

- 先生成一份 `1024 x 1024` 的地形信息图，作为世界事实层。
- 再由同一份信息图派生 Terrain3D 连续 3D 地形。
- 同时由同一份信息图派生约 `128 x 128` 的六边形玩法 tile。

MVP 不追求最终世界质量，而是验证架构是否成立：视觉地形、逻辑 tile、地图交互、overlay 显示和重新烘焙流程能否稳定协作。

## MVP 范围

必须包含：

- 程序化生成 `1024 x 1024` 地形信息图。
- 从高度数据生成一个 Terrain3D 地形。
- 生成约 `128 x 128` 的六边形 tile 数据。
- 绘制与 3D 地形对齐的六边形 overlay。
- 支持鼠标 hover 和点击选择 tile。
- 显示 tile 信息面板。
- 支持基础地形分类：海洋、海岸、平原、丘陵、山地、森林、沙漠。
- 生成基础河流边数据。
- 支持一个简单政治地图 overlay。

暂不包含：

- 完整省份系统。
- 国家 AI。
- 完整经济系统。
- 存档系统。
- 高级侵蚀模拟。
- 复杂手工地图编辑工具。
- 运行时地形修改。
- 多人同步。

## 进度标记

本文档使用以下标记跟踪任务状态：

```text
[ ] 未开始
[~] 进行中
[x] 已完成
[!] 阻塞
```

## 阶段 0：项目准备

目标：建立大地图原型需要的基础目录、配置资源和场景入口。

任务：

- [x] 创建 `res://world/` 目录结构。
- [x] 创建 `res://world/configs/world_map_config.tres`。
- [x] 创建或更新大地图原型场景。
- [x] 添加根节点 `world_map`，用于挂载大地图系统。
- [x] 确定第一版世界尺寸、hex 半径、海平面和随机种子。
- [x] 为生成和烘焙流程添加基础 debug 日志。

建议目录：

```text
res://world/
├── configs/
├── generated/
├── scripts/
├── terrain/
└── scenes/
```

交付物：

- `WorldMapConfig` C# 资源：`res://world/scripts/WorldMapConfig.cs`。
- 大地图原型场景：`res://world/scenes/world_map.tscn`。
- 保留的 generated 数据目录：`res://world/generated/`。
- 第一版配置：世界尺寸 `4096 x 4096`，信息图 `1024 x 1024`，hex 目标 `128 x 128`，hex 半径 `16`，海平面 `0`，seed `1337`。

验收标准：

- 原型场景可以无错误打开。
- C# 脚本可以读取大地图配置。
- 后续生成器、烘焙器、overlay 和输入系统都使用同一个配置对象。

依赖：

- 无。

## 阶段 1：地形信息图

目标：生成后续视觉地形和玩法 tile 共用的世界事实层。

任务：

- [x] 实现 `TerrainInfoMap` 数据容器。
- [x] 实现 `WorldGenerator`。
- [x] 生成基础高度数据。
- [x] 生成陆地/水域 mask。
- [x] 生成湿度图。
- [x] 生成温度图。
- [x] 生成 biome id 图。
- [x] 生成简化河流流量数据。
- [x] 导出 debug 图：高度、陆地 mask、湿度、温度、biome、河流。
- [x] 保存生成元数据：seed、地图尺寸、世界尺寸、海平面。

最低生成规则：

- 使用多层噪声生成大陆形状。
- 使用 ridge noise 生成山脉。
- 使用海平面阈值划分水域。
- 使用纬度和高度计算温度。
- 使用距海距离和高度计算湿度。
- 使用温度和湿度分类 biome。
- 使用简化下坡追踪或近似流量图生成河流。

交付物：

- `TerrainInfoMap` 运行时对象或资源：`res://world/scripts/TerrainInfoMap.cs`。
- `WorldGenerator`：`res://world/scripts/WorldGenerator.cs`。
- 生成资源：`res://world/generated/terrain_info_map.res`。
- `res://world/generated/` 下的 debug 图：高度、陆地 mask、湿度、温度、biome、河流。
- 基于 seed 的确定性生成结果。

验收标准：

- 相同 seed 会生成相同信息图。
- 高度数据精度高于 8-bit。
- 陆地、水域、山脉、biome 区域可以通过 debug 图检查。
- 河流 debug 图足以支持后续 tile 边数据烘焙。

依赖：

- 阶段 0。

## 阶段 2：坐标系统

目标：确保信息图、3D 世界坐标和六边形坐标完全对齐。

任务：

- [x] 实现 `WorldMapCoordinateUtility`。
- [x] 实现 info map pixel 到 UV 的转换。
- [x] 实现 UV 到 world XZ 的转换。
- [x] 实现 world XZ 到 info map 采样位置的转换。
- [x] 实现 axial hex 到 world XZ 的转换。
- [x] 实现 world XZ 到 axial hex 的转换。
- [x] 实现最近 hex 的坐标取整。
- [x] 添加调试验证脚本或小型测试场景。

必须统一的转换链：

```text
info pixel <-> uv <-> world xz <-> axial hex
```

交付物：

- 所有系统共用的坐标工具：`res://world/scripts/WorldMapCoordinateUtility.cs`。
- 坐标验证器：`res://world/scripts/WorldMapCoordinateValidator.cs`。
- 启动时生成的验证报告：`res://world/generated/coordinate_validation_report.txt`。

验收标准：

- hex 中心点转换到 world XZ 后再转回 hex，结果不变。
- info map 采样范围与世界尺寸匹配。
- 项目中没有重复实现的坐标转换逻辑。

依赖：

- 阶段 0。

## 阶段 3：Terrain3D 视觉地形

目标：从地形信息图生成 Terrain3D 连续视觉地形。

任务：

- [!] 在原型场景中接入 Terrain3D。当前项目尚未安装 Terrain3D 插件，暂由预览 mesh fallback 承担可视化验证。
- [x] 实现 `Terrain3DBaker`。
- [~] 将 `height_map` 转换为 Terrain3D 高度。当前已转换为 `ArrayMesh` 预览地形；真实 Terrain3D 数据写入待插件接入。
- [x] 根据 biome 和坡度应用粗略材质映射。
- [x] 添加水面或临时水体显示。
- [ ] 添加可控范围内的视觉细节噪声。
- [x] 添加重新烘焙命令或 editor 按钮。当前为原型场景启动时自动 rebake。
- [x] 添加 free look 第一人称预览相机。右键按住转向，WASD 移动，Q/E 降升，Shift 加速。

建议材质映射：

```text
海洋/海岸     -> 沙地或湿润岸线
平原          -> 草地
丘陵          -> 草地与岩石混合
山地          -> 岩石
森林 biome    -> 深色植被
沙漠 biome    -> 沙地或干土
```

交付物：

- 视觉地形烘焙入口：`res://world/scripts/Terrain3DBaker.cs`。
- 原型场景中可见的 3D 地形预览 fallback。
- 基础 biome 顶点色材质变化。
- 临时水面。
- 地形预览相机：`res://world/scripts/FreeLookCameraController.cs`。
- 烘焙报告：`res://world/generated/visual_terrain_bake_report.txt`。
- 可从 `TerrainInfoMap` 重建视觉地形的流程。

验收标准：

- 预览地形高度与高度 debug 图基本一致。
- 海洋、海岸、丘陵、山地在视觉上可辨识。
- 可以用 free look 第一人称相机检查地形起伏和水面关系。
- 视觉地形可以从源数据重新生成。
- 玩法逻辑不采样 Terrain3D 当前可见 mesh。

依赖：

- 阶段 1。
- 阶段 2。

## 阶段 4：六边形 Tile 烘焙

目标：从同一份地形信息图生成稳定的六边形玩法数据。

任务：

- [x] 定义 `HexTile` 数据模型。
- [x] 定义地形和 biome 枚举。
- [x] 实现 `HexTileBaker`。
- [x] 生成约 `128 x 128` 个 hex tile。
- [x] 对每个 tile 采样中心点、六个角、六条边中点和可选内部采样点。
- [x] 计算平均高度、最低高度、最高高度和坡度。
- [x] 分类水域、海岸、平原、丘陵、山地、森林、沙漠。
- [x] 计算移动消耗。
- [x] 生成初始 region 或 owner 占位数据。
- [x] 将 tile 数据保存为资源或二进制文件。

建议 tile 字段：

```text
q, r
world center xz
center height
average height
min height
max height
slope
is water
is coastal
terrain kind
biome kind
movement cost
province id
region id
owner id
resource id
```

交付物：

- `HexTile` 数据模型：`res://world/scripts/HexTile.cs`。
- `HexTileMap` 数据资源：`res://world/scripts/HexTileMap.cs`。
- `HexTileBaker`：`res://world/scripts/HexTileBaker.cs`。
- `TerrainKind` 枚举：`res://world/scripts/TerrainKind.cs`。
- 已烘焙的 hex tile 数据集：`res://world/generated/hex_tiles.res`。
- tile 分类 debug 图：`res://world/generated/hex_tiles_terrain_debug.png`。
- tile 烘焙报告：`res://world/generated/hex_tile_bake_report.txt`。
- 按 axial 坐标快速查询 tile 的能力。

验收标准：

- tile 数量接近配置目标。
- 地形分类与信息图吻合。
- 海岸 tile 可以通过相邻关系正确识别。
- 相同 seed 下重复烘焙结果稳定。

依赖：

- 阶段 1。
- 阶段 2。

## 阶段 5：河流边数据

目标：将信息图中的河流数据转换为适合大战略规则的 hex edge 数据。

任务：

- [x] 定义 `HexRiverEdge`。
- [x] 在 hex 边附近采样河流流量。
- [x] 将河流关联到相邻 tile 的边。
- [x] 分类无河流、小河、大河。
- [x] 添加跨河移动惩罚。
- [x] 添加河流 debug overlay 或控制台输出。

建议河流边字段：

```text
tile q/r
neighbor direction
flow strength
river kind
crossing cost modifier
```

交付物：

- `HexRiverEdge` 数据模型：`res://world/scripts/HexRiverEdge.cs`。
- `RiverKind` 枚举：`res://world/scripts/RiverKind.cs`。
- `HexRiverEdgeBaker`：`res://world/scripts/HexRiverEdgeBaker.cs`。
- `HexRiverEdgeDebugExporter`：`res://world/scripts/HexRiverEdgeDebugExporter.cs`。
- 挂载在相关 tile 上的河流边数据，保存在 `res://world/generated/hex_tiles.res` 的 edge 数组中。
- 可供移动系统读取的跨河消耗。
- 河流边 debug 图：`res://world/generated/hex_river_edges_debug.png`。
- 河流边烘焙报告：`res://world/generated/hex_river_edge_bake_report.txt`。

验收标准：

- 河流表示在 tile 边上，而不是只作为 tile 内部 flag。
- 相邻 tile 对同一条河流边的判断一致。
- 相同源数据下重复烘焙结果稳定。

依赖：

- 阶段 1。
- 阶段 4。

## 阶段 6：六边形 Overlay 渲染

目标：渲染与 Terrain3D 地形对齐的六边形网格。

任务：

- [x] 实现 `HexOverlayRenderer`。
- [x] 使用 line mesh 或 `MultiMeshInstance3D` 生成 tile 边框。
- [x] 为 hex 角点采样显示高度。
- [x] 添加少量垂直偏移，避免 z-fighting。
- [x] 添加 hover 高亮。当前已实现高亮 mesh 接口，实际鼠标输入在阶段 7 接入。
- [x] 添加选中高亮。当前已实现高亮 mesh 接口，实际鼠标输入在阶段 7 接入。
- [x] 添加按地形类型染色的 debug overlay。
- [x] 添加按 owner id 染色的政治 overlay。

MVP 推荐做法：

- 使用生成的 line mesh 或 MultiMesh。
- overlay 与 Terrain3D 分离。
- tile 使用纯数据对象，不为每个 tile 创建独立场景节点。

交付物：

- `HexOverlayRenderer`：`res://world/scripts/HexOverlayRenderer.cs`。
- `HexMapMode` 枚举：`res://world/scripts/HexMapMode.cs`。
- 可见六边形网格。
- hover 与选中视觉效果接口。
- 地形 overlay 和政治 overlay。

验收标准：

- overlay 在原型地图范围内与地形对齐。
- hover 和选中反馈 mesh 可由输入系统即时切换。
- 渲染层没有为每个 tile 创建一个 `Node3D`。
- 切换地形/政治 overlay 不需要重新烘焙数据。

依赖：

- 阶段 2。
- 阶段 3。
- 阶段 4。

## 阶段 7：输入与 Tile 信息面板

目标：让开发者或玩家可以在运行场景中检查 tile 数据。

任务：

- [x] 实现 `WorldMapInputController`。
- [x] 从相机向世界发射 raycast。当前使用信息图高度采样求射线与地形的命中点，不依赖 Terrain3D/预览 mesh 物理碰撞。
- [x] 将命中的 world XZ 转换为 axial hex。
- [x] 查询对应的 `HexTile`。
- [x] 更新 hover 状态。
- [x] 点击时选中 tile。
- [x] 创建紧凑的 tile 信息面板。
- [x] 显示地形、biome、高度、坡度、水域、海岸、移动消耗、owner 和河流数量。

交付物：

- `WorldMapInputController`：`res://world/scripts/WorldMapInputController.cs`。
- 运行时 hover。
- 运行时点击选择。
- tile 信息面板。

验收标准：

- 鼠标 hover 的 tile 与光标位置一致。
- 点击 tile 后可以稳定选中。
- 信息面板显示的数据来自烘焙后的 tile 数据。
- UI 在常见桌面分辨率下可读、无遮挡。

依赖：

- 阶段 4。
- 阶段 6。

## 阶段 8：MVP 集成与验证

目标：验证完整的“信息图 -> Terrain3D -> hex tile -> overlay -> 交互”流程。

任务：

- [x] 添加一键或单命令完整 rebake 流程。当前入口为 `WorldMapPrototype.RunFullRebakePipeline()`，运行场景默认也会执行完整 pipeline。
- [x] 重新生成地形信息图。
- [x] 重新烘焙 Terrain3D。当前仍使用 ArrayMesh 预览 fallback，真实 Terrain3D 写入后续接入。
- [x] 重新烘焙 hex tile。
- [x] 重新烘焙河流边。
- [x] 刷新 overlay。
- [x] 检查视觉地形和逻辑 tile 对齐。
- [x] 记录已知限制。
- [x] 更新本文档的进度状态。

验证清单：

- [x] 相同 seed 生成相同世界。
- [x] Terrain3D 高度与信息图一致。当前验证对象为信息图派生的 ArrayMesh 预览地形。
- [x] 六边形 overlay 与地形对齐。
- [x] 多个区域的 tile 分类合理。
- [x] 海岸识别可用。
- [x] 河流边数据出现在预期边界。
- [x] hover 和点击选择可用。
- [x] 政治 overlay 可以显示 owner id。
- [x] 没有玩法系统依赖 Terrain3D clipmap mesh 数据。

交付物：

- 端到端 MVP 场景：`res://world/scenes/world_map.tscn`。
- MVP 验证器：`res://world/scripts/WorldMapMvpValidator.cs`。
- 可复现的 generated 数据。
- MVP 验证报告：`res://world/generated/world_map_mvp_validation_report.txt`。
- 已更新的已知限制记录。

验收标准：

- 开发者可以打开原型场景、运行项目、检查 hex，并在生成的 3D 地形上看到一致的 tile 数据。
- 完整 pipeline 可以重复执行，不需要手工清理中间文件。
- 架构已准备好继续扩展省份、资源和移动系统。

依赖：

- 阶段 1 至阶段 7。

## 总进度表

| 阶段 | 状态 | 主要交付物 | 负责人 | 备注 |
| --- | --- | --- | --- | --- |
| 阶段 0：项目准备 | [x] | 配置与场景结构 | Codex | 已创建 world 目录、配置资源、原型场景和基础日志 |
| 阶段 1：地形信息图 | [x] | 生成的世界事实层 | Codex | 已实现信息图数据、生成器、资源保存和 debug 图导出 |
| 阶段 2：坐标系统 | [x] | 共用坐标转换工具 | Codex | 已实现 pointy-top axial hex、像素/UV/world 转换和启动验证报告 |
| 阶段 3：Terrain3D 视觉地形 | [~] | 连续 3D 地形 | Codex | ArrayMesh 预览 fallback 和 free look 预览相机已完成；真实 Terrain3D 接入等待插件安装 |
| 阶段 4：六边形 Tile 烘焙 | [x] | 烘焙后的 tile 数据 | Codex | 已生成 16,384 个 tile，包含高度统计、地形分类、海岸、移动消耗和占位 owner/region |
| 阶段 5：河流边数据 | [x] | hex edge 河流数据 | Codex | 已生成 2,310 条共享河流边，包含小河/大河分类、跨河消耗和双向一致性检查 |
| 阶段 6：六边形 Overlay 渲染 | [x] | 对齐地形的 hex overlay | Codex | 已渲染 16,384 个 tile 的边框、地形 overlay、政治 overlay，并预留 hover/selection 高亮接口 |
| 阶段 7：输入与 Tile 信息面板 | [x] | hover、选择、信息面板 | Codex | 已接入信息图高度 raycast、hover/selection 高亮和 tile inspector |
| 阶段 8：MVP 集成与验证 | [x] | 端到端 MVP | Codex | 完整 pipeline 和 MVP 验证报告已通过，当前报告结果为 PASS |

## 技术风险

| 风险 | 影响 | 缓解方式 |
| --- | --- | --- |
| Terrain3D API 集成成本高于预期 | 视觉地形阶段被卡住 | 保留临时 ArrayMesh 高度预览作为 debug fallback |
| 坐标转换出现偏移 | overlay、tile 和地形错位 | 早期集中实现转换工具，并做 round trip 验证 |
| hex overlay 性能不足 | 编辑器或运行时卡顿 | 使用批量 mesh 或 MultiMesh，不为每个 tile 创建节点 |
| 河流贴边后视觉不自然 | 河流玩法与视觉河道不完全一致 | 分离玩法河流边和视觉河流渲染，后续再细化匹配 |
| 生成地形缺乏战略可读性 | MVP 难以验证玩法地图价值 | 优先增加 debug 视图和生成参数调试 |
| 数据格式频繁变化 | 迭代中反复返工 | MVP 期间把 generated 数据视为可丢弃、可重烘焙产物 |

## MVP 已知限制

- 真实 Terrain3D 数据写入路径尚未完成；当前视觉地形仍使用 `ArrayMesh` 预览 fallback。
- `res://world/generated/` 下的资源和 debug 图是可重建产物，按 `.gitignore` 不提交到仓库。
- 河流边玩法数据已完成，但还没有沿 hex edge 绘制独立河流视觉 mesh。
- hover/selection 与 inspector 已完成，但 UI 仍是开发调试面板，不是最终游戏 HUD。
- 省份、资源、移动、存档和真实 Terrain3D 烘焙是 MVP 后续里程碑。

## MVP 完成定义

满足以下条件即可视为 MVP 完成：

- 项目可以生成地形信息图。
- Terrain3D 可以显示由信息图派生的地形。
- hex tile 数据可以从同一份信息图烘焙。
- 六边形 overlay 与 3D 地形对齐。
- 用户可以 hover 和点击 tile。
- tile 信息面板可以显示烘焙出的玩法数据。
- 地形、河流、海岸和政治 debug overlay 可用。
- pipeline 可以从 seed 和配置重新执行。

## MVP 后续推荐里程碑

MVP 完成后，建议按以下顺序继续扩展：

1. 省份生成与编辑。
2. 移动和寻路。
3. 资源分布与资源地图模式。
4. 城市、港口、道路和军队 marker。
5. 烘焙世界数据的存档和读取。
6. 手工修正 mask。
7. 战略 AI 区域分析。
