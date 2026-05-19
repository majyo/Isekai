# 地形生成算法改进开发计划

## 目标

将当前 MVP 级地形生成器升级为可调参、可验证、尺度稳定，并能支撑大战略玩法的世界生成管线。

核心目标：

- 保持 `TerrainInfoMap` 作为世界事实层。
- 保持同一 seed 和同一配置下可重复生成。
- 让信息图分辨率提升时只增加细节，不改变大陆主体形态。
- 让河流、海岸、山脉、气候、biome 和 hex tile 数据之间保持可解释关系。
- 为后续 Terrain3D 正式烘焙、寻路、资源、城市、港口和省份系统提供稳定数据。

## 非目标

本计划不包含以下内容：

- 完整板块构造模拟。
- 运行时动态地形修改。
- 最终美术材质和植被散布。
- 国家、省份、经济、城市和资源系统的完整玩法实现。
- Terrain3D 插件内部源码修改。

## 当前基线

当前生成链路：

```text
WorldMapConfig
    -> WorldGenerator
    -> TerrainInfoMap
    -> Terrain3DBaker / ArrayMesh preview
    -> HexTileBaker
    -> HexRiverEdgeBaker
    -> HexOverlayRenderer / WorldMapInputController
```

主要源文件：

```text
world/scripts/WorldMapConfig.cs
world/scripts/WorldGenerator.cs
world/scripts/TerrainInfoMap.cs
world/scripts/HexTileBaker.cs
world/scripts/HexRiverEdgeBaker.cs
world/scripts/Terrain3DBaker.cs
world/scripts/WorldMapMvpValidator.cs
```

当前主要问题：

- `WorldGenerator` 中大量参数硬编码。
- 噪声使用像素坐标和固定频率，信息图分辨率变化会改变世界形态。
- 河流只有简化流量，没有稳定流向、流域、河口和湖泊处理。
- `HexRiverEdgeBaker` 通过边附近最大流量推断河流边，缺少真实穿边逻辑。
- 气候模型缺少风向、雨影、洋流、河流和湖泊影响。
- Hex tile 覆盖范围小于 `WorldSize`，边缘存在未覆盖区域。
- Tile 分类采样和坡度统计较粗糙。
- 视觉地形仍是 `ArrayMesh` preview fallback。
- MVP 验证器主要验证管线存在，不验证地形质量。

## 状态约定

任务状态使用以下标记：

```text
[ ] 未开始
[~] 进行中
[x] 完成
[!] 阻塞或需要决策
```

优先级：

```text
P0 必须先做，影响后续所有阶段
P1 高价值核心改进
P2 重要但可以延后
P3 质量增强或长期优化
```

## 全局验收标准

每个阶段完成后都应满足：

- `godot --headless --path . --quit` 能完成自动生成和验证。
- 生成报告写入 `res://world/generated/`。
- 同一配置和 seed 下，关键统计值可重复。
- Debug 图能直观检查该阶段新增数据层。
- 新增数据层有对应尺寸、范围和一致性校验。
- 不编辑 `.godot/` 生成缓存。

## 阶段 0：基线、指标和报告

目标：先建立可观测基线，让后续改动可比较、可回滚、可验收。

产物：

```text
world/generated/terrain_generation_quality_report.txt
world/generated/height_histogram_debug.png
world/generated/terrain_generation_metrics.json
```

任务：

| ID | 状态 | 优先级 | 任务 | 文件 | 验收标准 |
| --- | --- | --- | --- | --- | --- |
| TG-0001 | [x] | P0 | 新增地形质量报告器 | `world/scripts/TerrainGenerationQualityReporter.cs` | 输出 seed、尺寸、耗时、陆地比例、高度范围、biome 分布、河流像素数 |
| TG-0002 | [x] | P0 | 记录高度分位数 | `TerrainGenerationQualityReporter.cs` | 报告 p01、p05、p25、p50、p75、p95、p99 |
| TG-0003 | [x] | P0 | 记录水陆和海岸指标 | `TerrainGenerationQualityReporter.cs` | 报告 land/water/coast 比例，异常时给出 warning |
| TG-0004 | [x] | P0 | 记录河流指标 | `TerrainGenerationQualityReporter.cs` | 报告 river pixel count、river edge count、max flow |
| TG-0005 | [x] | P0 | 记录 hex 覆盖范围 | `TerrainGenerationQualityReporter.cs` | 报告 hex center/corner 覆盖范围和世界范围差值 |
| TG-0006 | [x] | P1 | 在 MVP 验证器中接入质量报告路径 | `WorldMapMvpValidator.cs` | 验证报告列出质量报告位置 |
| TG-0007 | [x] | P1 | 增加生成耗时统计 | `WorldMapPrototype.cs` | 报告各阶段毫秒耗时 |

阶段完成标准：

- 当前算法不改行为，只新增报告。
- 能用报告作为后续所有阶段的对比基线。
- 至少保存 3 个不同 seed 的人工观察记录。

## 阶段 1：配置化和参数治理

目标：把算法关键参数从代码常量迁移到配置资源，让生成质量可以通过资源调参。

产物：

```text
world/configs/world_map_config.tres
world/scripts/WorldMapConfig.cs
world/generated/terrain_generation_config_report.txt
```

任务：

| ID | 状态 | 优先级 | 任务 | 文件 | 验收标准 |
| --- | --- | --- | --- | --- | --- |
| TG-0101 | [ ] | P0 | 配置化海洋深度 | `WorldMapConfig.cs`, `WorldGenerator.cs` | `OceanDepth` 不再是 `WorldGenerator` 私有常量 |
| TG-0102 | [ ] | P0 | 配置化大陆噪声参数 | `WorldMapConfig.cs`, `WorldGenerator.cs` | continent/broad/detail frequency 和 octave 可调 |
| TG-0103 | [ ] | P0 | 配置化边缘衰减 | `WorldMapConfig.cs`, `WorldGenerator.cs` | edge falloff 起止、强度和 bias 可调 |
| TG-0104 | [ ] | P0 | 配置化山地参数 | `WorldMapConfig.cs`, `WorldGenerator.cs` | ridge frequency、octave、power、height weight 可调 |
| TG-0105 | [ ] | P1 | 配置化 biome 阈值 | `WorldMapConfig.cs`, `WorldGenerator.cs` | 山地、丘陵、沙漠、森林、冻原阈值可调 |
| TG-0106 | [ ] | P1 | 配置化河流阈值 | `WorldMapConfig.cs`, `WorldGenerator.cs`, `HexRiverEdgeBaker.cs` | flow threshold、小河/大河阈值可调 |
| TG-0107 | [ ] | P1 | 新增配置合法性检查 | `WorldMapConfig.cs` | 非法频率、阈值、尺寸会返回明确错误 |
| TG-0108 | [ ] | P2 | 生成配置摘要报告 | `TerrainGenerationQualityReporter.cs` | 报告实际参与生成的所有关键参数 |

阶段完成标准：

- 修改生成参数不需要改 C# 代码。
- 旧默认配置生成结果尽量接近当前 MVP。
- `WorldMapConfig.IsValid` 能拦截明显非法参数。

## 阶段 2：尺度稳定化

目标：让生成结果绑定到世界坐标或归一化坐标，而不是信息图像素坐标。

产物：

```text
world/generated/scale_stability_report.txt
```

任务：

| ID | 状态 | 优先级 | 任务 | 文件 | 验收标准 |
| --- | --- | --- | --- | --- | --- |
| TG-0201 | [ ] | P0 | 引入统一采样坐标 | `WorldGenerator.cs` | 高度、气候、河流种子输入统一来自 uv 或 world xz |
| TG-0202 | [ ] | P0 | 噪声频率改为世界尺度 | `WorldGenerator.cs` | 频率含义从 pixel frequency 改为 world/normalized scale |
| TG-0203 | [ ] | P0 | 增加分辨率稳定性测试 | `WorldMapMvpValidator.cs` 或新验证器 | 1024 和 2048 信息图大陆轮廓相关性达标 |
| TG-0204 | [ ] | P1 | 修正距离到水的尺度含义 | `WorldGenerator.cs` | 湿度衰减使用世界距离而不是纯像素数 |
| TG-0205 | [ ] | P1 | 记录 scale stability 指标 | `TerrainGenerationQualityReporter.cs` | 报告不同分辨率下 land mask 差异比例 |

阶段完成标准：

- 同一 seed 下，`1024 x 1024` 与 `2048 x 2048` 的大陆轮廓基本一致。
- 提升 `InfoMapSize` 不会显著改变陆地比例、山脉位置和主要河流来源区。

## 阶段 3：高度场和大陆形态升级

目标：从纯 layered noise 过渡到更有结构的大陆、山脉、盆地、海岸和高原生成。

新增或扩展数据层：

```text
TerrainInfoMap.MountainMask
TerrainInfoMap.LowlandMask
TerrainInfoMap.ErosionHintMap
```

任务：

| ID | 状态 | 优先级 | 任务 | 文件 | 验收标准 |
| --- | --- | --- | --- | --- | --- |
| TG-0301 | [ ] | P1 | 拆分大陆 mask 和最终高度 | `WorldGenerator.cs` | 可单独导出大陆 mask debug 图 |
| TG-0302 | [ ] | P1 | 生成连续山脉 mask | `WorldGenerator.cs` | 山脉呈带状或链状，不只是散点 ridge |
| TG-0303 | [ ] | P1 | 生成低地和盆地 hint | `WorldGenerator.cs` | 河流更容易形成长路径，平原比例可控 |
| TG-0304 | [ ] | P2 | 增加海岸平滑 pass | `WorldGenerator.cs` | 减少单像素锯齿海岸和孤立水陆噪声 |
| TG-0305 | [ ] | P2 | 增加简单热侵蚀 pass | `WorldGenerator.cs` | 高坡度尖刺减少，坡度分布报告改善 |
| TG-0306 | [ ] | P2 | 导出新增 debug 图 | `TerrainInfoMapDebugExporter.cs` | mountain/lowland/erosion debug 图可检查 |

阶段完成标准：

- 高度图中能清楚读出大陆主体、山脉带、低地和平原。
- 高度直方图没有极端集中在海平面附近。
- 海岸线保留自然复杂度，但孤立噪点减少。

## 阶段 4：水系重做

目标：建立稳定水文系统，支持连续河流、河口、湖泊和后续河谷雕刻。

新增或扩展数据层：

```text
TerrainInfoMap.FlowDirectionMap
TerrainInfoMap.FlowAccumulationMap
TerrainInfoMap.RiverFlowMap
TerrainInfoMap.WaterBodyMap
TerrainInfoMap.BasinIdMap
```

任务：

| ID | 状态 | 优先级 | 任务 | 文件 | 验收标准 |
| --- | --- | --- | --- | --- | --- |
| TG-0401 | [ ] | P0 | 实现流向数据层 | `TerrainInfoMap.cs`, `WorldGenerator.cs` | 每个陆地像素有稳定下游方向或明确 sink 标记 |
| TG-0402 | [ ] | P0 | 实现洼地处理 | `WorldGenerator.cs` | 大部分内陆 sink 被填平或连到出流路径 |
| TG-0403 | [ ] | P0 | 实现流量累积 | `WorldGenerator.cs` | flow accumulation 可导出并稳定复现 |
| TG-0404 | [ ] | P0 | 从累积流量提取河道 | `WorldGenerator.cs` | 河流从高地连续流向海、湖或地图边界 |
| TG-0405 | [ ] | P1 | 增加湖泊和内陆水体初版 | `WorldGenerator.cs`, `TerrainInfoMap.cs` | sink 区可形成湖泊，而不是直接消失 |
| TG-0406 | [ ] | P1 | 记录河流连通性指标 | `TerrainGenerationQualityReporter.cs` | 报告 river segments、mouth count、dead-end count |
| TG-0407 | [ ] | P1 | 导出 flow direction debug 图 | `TerrainInfoMapDebugExporter.cs` | 能可视化主流向 |
| TG-0408 | [ ] | P2 | 初版河谷雕刻 | `WorldGenerator.cs` | 河道附近高度略降低，河流不悬在山坡顶 |

阶段完成标准：

- 主河流能连续追踪到海岸、湖泊或世界边界。
- 河流断头数量可被报告并低于阈值。
- 河流与高度场关系合理，不出现大量上坡流。

## 阶段 5：Hex 河流边重建

目标：让玩法层河流边来自真实河流路径，而不是边附近流量最大值。

任务：

| ID | 状态 | 优先级 | 任务 | 文件 | 验收标准 |
| --- | --- | --- | --- | --- | --- |
| TG-0501 | [ ] | P0 | 建立 info pixel 到 hex 的河流穿边映射 | `HexRiverEdgeBaker.cs` | 每段 river path 能映射到相邻 hex edge |
| TG-0502 | [ ] | P0 | 使用流向决定河流跨边 | `HexRiverEdgeBaker.cs` | 河流边方向与 flow direction 一致 |
| TG-0503 | [ ] | P1 | 合并连续小段河流 | `HexRiverEdgeBaker.cs` | 同一条河在 hex 边上连续可读 |
| TG-0504 | [ ] | P1 | 处理河口和湖边 | `HexRiverEdgeBaker.cs` | 陆地到水体边界可表示河口 |
| TG-0505 | [ ] | P1 | 增加 river edge 连通性验证 | `WorldMapMvpValidator.cs` | 报告断裂边、孤立边、异常环 |
| TG-0506 | [ ] | P2 | 输出河流路径调试报告 | `HexRiverEdgeDebugExporter.cs` | debug 图能区分小河、大河、河口 |

阶段完成标准：

- Hex river edge 与信息图河道位置高度一致。
- 河流边不再大面积随机跳边。
- crossing cost 能用于寻路原型。

## 阶段 6：气候和 biome 改进

目标：让 biome 分布受纬度、高度、海洋距离、风向、雨影和河流湖泊影响。

新增或扩展数据层：

```text
TerrainInfoMap.PrecipitationMap
TerrainInfoMap.WindExposureMap
```

任务：

| ID | 状态 | 优先级 | 任务 | 文件 | 验收标准 |
| --- | --- | --- | --- | --- | --- |
| TG-0601 | [ ] | P1 | 配置主导风向 | `WorldMapConfig.cs`, `WorldGenerator.cs` | 风向可调，并写入报告 |
| TG-0602 | [ ] | P1 | 实现海洋湿度传播 | `WorldGenerator.cs` | 沿风向内陆湿度递减 |
| TG-0603 | [ ] | P1 | 实现山脉雨影 | `WorldGenerator.cs` | 背风侧湿度降低，迎风侧更湿 |
| TG-0604 | [ ] | P2 | 河流湖泊局部湿润影响 | `WorldGenerator.cs` | 河谷附近湿度略提升 |
| TG-0605 | [ ] | P1 | Biome 分类改成规则表或曲线 | `WorldMapConfig.cs`, `WorldGenerator.cs` | 阈值集中配置，规则可解释 |
| TG-0606 | [ ] | P2 | 增加 biome 连续性 pass | `WorldGenerator.cs` | 减少孤立单像素 biome 噪点 |
| TG-0607 | [ ] | P1 | 导出气候 debug 图 | `TerrainInfoMapDebugExporter.cs` | precipitation/wind exposure debug 图可检查 |

阶段完成标准：

- 森林、草原、沙漠、冻原与纬度、山脉和海岸关系可解释。
- Biome 分布报告没有某一类异常占满或完全缺失。

## 阶段 7：Hex tile 烘焙质量升级

目标：提高 tile 分类稳定性，修正覆盖范围，并为寻路和省份系统提供更可信数据。

任务：

| ID | 状态 | 优先级 | 任务 | 文件 | 验收标准 |
| --- | --- | --- | --- | --- | --- |
| TG-0701 | [ ] | P0 | 明确 hex grid 覆盖策略 | `WorldMapConfig.cs`, `HexTileBaker.cs` | 决定覆盖完整世界或显式保留不可玩边缘 |
| TG-0702 | [ ] | P0 | 修正默认 hex 覆盖范围 | `world_map_config.tres`, `HexTileBaker.cs` | 覆盖范围与 `WorldSize` 一致或报告为设计选择 |
| TG-0703 | [ ] | P1 | 增加面积权重采样 | `HexTileBaker.cs` | 海岸、小岛、湖泊分类更稳定 |
| TG-0704 | [ ] | P1 | 改进坡度统计 | `HexTileBaker.cs` | 使用局部梯度或多方向坡度，而不是只用 max-min |
| TG-0705 | [ ] | P1 | 引入 water depth 或 shoreline 分类 | `TerrainInfoMap.cs`, `HexTileBaker.cs` | 能区分深海、浅海、湖泊、海岸 |
| TG-0706 | [ ] | P2 | 增加连通性分析 | 新验证器 | 报告陆块、水体、孤立 tile 和窄瓶颈 |
| TG-0707 | [ ] | P2 | 输出 tile classification confidence | `HexTileMap.cs`, `HexTileBaker.cs` | 边界 tile 可标记低置信度 |

阶段完成标准：

- Tile 分类和信息图 debug 图吻合。
- 海岸、山脊、河流附近的 tile 不再频繁误判。
- Hex 覆盖范围问题被修正或明确设计化。

## 阶段 8：视觉地形和 Terrain3D 接入

目标：让正式视觉地形使用 Terrain3D 数据，同时保留 ArrayMesh fallback 用于调试。

任务：

| ID | 状态 | 优先级 | 任务 | 文件 | 验收标准 |
| --- | --- | --- | --- | --- | --- |
| TG-0801 | [ ] | P1 | 调研 Terrain3D C#/GDExtension 高度写入 API | `Terrain3DBaker.cs` | 写入方式和限制记录到文档 |
| TG-0802 | [ ] | P1 | 建立 Terrain3D 数据输出路径 | `world/terrain/` | 生成数据可重建，不污染手工资源 |
| TG-0803 | [ ] | P1 | 将 HeightMap 写入 Terrain3D | `Terrain3DBaker.cs` | Terrain3D 显示高度与信息图一致 |
| TG-0804 | [ ] | P2 | 将 biome/slope/moisture 映射到材质 | `Terrain3DBaker.cs` | 至少支持水岸、草地、森林、沙地、岩石 |
| TG-0805 | [ ] | P2 | 保留 ArrayMesh debug 模式 | `WorldMapConfig.cs`, `Terrain3DBaker.cs` | 配置可切换 fallback |
| TG-0806 | [ ] | P2 | 更新验证器区分真实 Terrain3D 和 fallback | `WorldMapMvpValidator.cs` | 报告当前视觉地形模式 |

阶段完成标准：

- 正式路径不再只依赖 `ArrayMesh` 预览。
- Overlay、输入命中和 tile 数据仍然以 `TerrainInfoMap` 为准。

## 阶段 9：工具化和回归验证

目标：让生成和验证成为可重复工具，而不是只能靠启动场景自动执行。

任务：

| ID | 状态 | 优先级 | 任务 | 文件 | 验收标准 |
| --- | --- | --- | --- | --- | --- |
| TG-0901 | [ ] | P1 | 添加一键 rebake 入口 | 新 editor tool 或场景工具节点 | 可单独执行 generate/bake/validate |
| TG-0902 | [ ] | P1 | 支持多 seed 批量质量测试 | 新工具脚本 | 输出多个 seed 的统计汇总 |
| TG-0903 | [ ] | P1 | 建立质量阈值配置 | `WorldMapConfig.cs` 或新资源 | 异常 land ratio、river dead-end 等能 fail |
| TG-0904 | [ ] | P2 | 建立 generated 清理工具 | 新工具脚本 | 清理可重建产物，不触碰手写资源 |
| TG-0905 | [ ] | P2 | 添加文档化调参流程 | `Docs/` | 开发者知道如何检查 debug 图和报告 |

阶段完成标准：

- 可以用固定 seed 列表做回归。
- 质量退化能被报告捕捉，而不是只靠肉眼发现。

## 建议开发顺序

推荐按以下顺序推进：

```text
阶段 0 -> 阶段 1 -> 阶段 2 -> 阶段 4 -> 阶段 5 -> 阶段 7 -> 阶段 6 -> 阶段 3 -> 阶段 8 -> 阶段 9
```

理由：

- 阶段 0 到 2 先解决可观测、可调和尺度稳定问题。
- 阶段 4 和 5 优先解决当前最影响世界可信度的水系问题。
- 阶段 7 让玩法 tile 跟随新的水系和地形数据稳定化。
- 阶段 6 和 3 可以根据实际视觉质量穿插推进。
- 阶段 8 和 9 在数据质量稳定后推进更稳。

## 推荐里程碑

### 里程碑 A：可测可调的生成器

包含任务：

```text
TG-0001 到 TG-0007
TG-0101 到 TG-0108
TG-0201 到 TG-0205
```

验收：

- 输出完整质量报告。
- 关键参数全部可配置。
- 改变信息图分辨率不改变大陆主体。

### 里程碑 B：可信水系

包含任务：

```text
TG-0401 到 TG-0408
TG-0501 到 TG-0506
```

验收：

- 河流连续流向海、湖或边界。
- Hex river edge 与河道吻合。
- 验证器能报告断河、异常河口和孤立河边。

### 里程碑 C：可用大战略地貌

包含任务：

```text
TG-0301 到 TG-0306
TG-0601 到 TG-0607
TG-0701 到 TG-0707
```

验收：

- 大陆、山脉、低地、河流、气候和 biome 有可解释关系。
- Tile 分类稳定，并能支持初版寻路和地图模式。

### 里程碑 D：正式视觉和工具闭环

包含任务：

```text
TG-0801 到 TG-0806
TG-0901 到 TG-0905
```

验收：

- Terrain3D 正式视觉地形可从 `TerrainInfoMap` 重建。
- 多 seed 回归和一键 rebake 可用。

## 质量指标建议阈值

初始阈值可以宽松，后续根据美术和玩法目标收紧。

| 指标 | 初始建议 | 说明 |
| --- | --- | --- |
| 陆地比例 | 25% 到 65% | 避免纯海或纯陆 |
| 海岸 tile 比例 | 2% 到 20% | 过低说明海岸太少，过高说明碎岛过多 |
| 山地 tile 比例 | 3% 到 25% | 过高会破坏可通行性 |
| 森林 tile 比例 | 5% 到 40% | 需要按气候调优 |
| 河流 dead-end 比例 | 小于 5% | 阶段 4 后启用 |
| 河流上坡段比例 | 小于 1% | 阶段 4 后启用 |
| Hex 覆盖缺口 | 0 或明确配置 | 阶段 7 后启用 |
| 孤立陆地 tile 数 | 低于阈值 | 阶段 7 后启用 |
| 生成确定性 | 100% | 同配置同 seed 必须一致 |

## 风险和应对

| 风险 | 影响 | 应对 |
| --- | --- | --- |
| 水系重做范围过大 | 阶段 4 拖慢整体进度 | 先实现 D8 流向和洼地处理，湖泊和雕刻可后置 |
| 参数过多导致调参困难 | 配置资源难维护 | 用分组、默认 preset 和报告摘要管理参数 |
| Terrain3D API 接入成本高 | 视觉阶段卡住 | 保留 ArrayMesh fallback，先稳定事实层和玩法层 |
| Debug 图过多 | generated 目录混乱 | 统一命名和报告索引，必要时加入清理工具 |
| 质量阈值过严 | 开发中频繁误报 | 初期 warning，稳定后再升级为 fail |

## 每次提交前检查清单

```text
[ ] 没有编辑 .godot/ 生成缓存。
[ ] 新增或修改的生成参数有默认值和有效性检查。
[ ] 同 seed 重复运行结果一致。
[ ] Debug 图或报告能验证本次改动。
[ ] MVP 验证器没有退化。
[ ] 文档中的任务状态已更新。
```

## 下一步建议

先执行里程碑 A：

```text
1. 实现 TerrainGenerationQualityReporter。
2. 接入 WorldMapPrototype 的生成流程。
3. 把 WorldGenerator 的硬编码参数迁入 WorldMapConfig。
4. 将噪声采样坐标从像素坐标改为 world/uv 坐标。
5. 用 1024 和 2048 信息图做一次尺度稳定性验证。
```

完成里程碑 A 后，再开始水系重做。这样河流改动会有清晰的质量指标和回归基线。
