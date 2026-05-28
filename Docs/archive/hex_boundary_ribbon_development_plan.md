# Hex 边界 Ribbon Mesh 开发文档

## 目标

本文档定义一种类似文明系列战略地图的 hex 边界渲染方案：边界线紧密贴合连续地形，有清晰的政治/选择高亮读图效果，但不改变玩法层 hex 数据。

推荐方案是：

```text
HexTileMap 边数据
    ↓
HexBoundaryRibbonRenderer / Baker
    ↓
贴合 TerrainInfoMap 高度的 ribbon mesh
    ↓
少量 MeshInstance3D + 发光边界材质
```

核心原则：

```text
玩法边界仍然以 HexTileMap 为准。
视觉边界是派生产物，可以更平滑、更漂亮，但不能反向修改玩法格子。
```

## 设计边界

### 需要实现

- 渲染紧贴地形的 hex 边界线。
- 支持政治边界、选中范围、hover 高亮和调试网格。
- 边线在山地、丘陵、海岸、水面上方都不明显嵌入地形。
- 将大量边界合并为少量 mesh，避免每条边一个节点。
- 边界材质支持主色、暗色外描边、亮色高光和透明度。
- 保持和当前 `TerrainInfoMap`、`HexTileMap`、`WorldMapConfig` 数据流一致。
- 输出简单报告，方便确认边数、顶点数、分段数和重建成本。

### 暂不实现

- 使用大量 `Decal` 节点投影整张 hex 网格。
- 在 Terrain3D shader 内直接绘制全部 hex 线。
- 根据视觉边界反推玩法领土或格子归属。
- 让边界自动避开树木、建筑、单位等局部模型。
- 对每个边界段做独立碰撞。

## 当前问题

当前 overlay/网格类方案容易出现两类问题：

- hex 面或边只在少量端点采样高度，中间区域可能被真实地形顶穿。
- 大量填充面在复杂地形上会像一张盖布，和文明类地图中“细边界贴地”的观感不同。

ribbon mesh 的目标不是继续铺满 hex，而是只画边线。边线本身按地形细分采样，视觉上更紧贴地表，也更容易控制性能。

## 推荐架构

### 第一阶段：接入现有渲染器

第一阶段可以先在现有 `HexOverlayRenderer` 内新增边界 ribbon 构建路径，降低场景和脚本改动量。

建议新增私有职责：

```text
CollectBoundaryEdges(...)
BuildBoundaryRibbonMesh(...)
AddRibbonSegment(...)
SampleOverlaySurface(...)
```

该阶段目标是快速验证：

- 边界线能贴地。
- 性能比填满所有 hex 面更稳定。
- hover/selection 可以复用同一套 ribbon 生成逻辑。

### 第二阶段：拆成独立渲染器

当边界类型和材质增多后，建议拆出专门脚本：

```text
res://world/scripts/HexBoundaryRibbonRenderer.cs
res://world/scripts/HexBoundaryRibbonStyle.cs
res://world/scripts/HexBoundaryRibbonReport.cs
```

推荐节点结构：

```text
world_map
├── hex_overlay
├── map_mode_overlay
└── hex_boundary_overlay
    ├── political_boundary_mesh
    ├── grid_boundary_mesh
    ├── hover_boundary_mesh
    └── selection_boundary_mesh
```

`hex_overlay` 可以继续负责旧的调试填色、地图模式覆盖或临时对照；`hex_boundary_overlay` 专注线条边界。

## 数据来源

### 地形高度

第一版优先使用：

```text
TerrainInfoMap.SampleHeightUv(WorldMapCoordinateUtility.WorldXzToUv(worldXz, config))
```

理由：

- 当前玩法、overlay 和输入命中已经以 `TerrainInfoMap` 作为世界事实层。
- 不依赖 Terrain3D clipmap 当前可见 mesh 或 LOD。
- ArrayMesh debug preview 与 Terrain3D 模式都可以共用同一套边界高度采样。

后续如发现 Terrain3D 实际显示高度与 `TerrainInfoMap` 有局部误差，可以加一个可选校准路径：

```text
Terrain3DData.get_height(worldPosition)
```

但默认不应让玩法或边界逻辑依赖 Terrain3D 节点存在。

### 边界来源

常见边界类型：

```text
政治边界：tile.OwnerId != neighbor.OwnerId
地形边界：tile.Terrain != neighbor.Terrain
水陆边界：tile.IsWater != neighbor.IsWater
选中范围：tileIndex 属于 selection set，neighbor 不属于
hover 边界：当前 hover tile 的 6 条边
调试网格：全部 hex edge，去重后显示
```

共享边只生成一次。建议规则：

```text
若 neighbor 存在，只在 tileIndex < neighborIndex 时生成。
若 neighbor 不存在，可生成地图外边界。
```

## 几何生成方案

### 边提取

每条边界记录至少包含：

```text
tileIndex
neighborIndex
direction
startWorldXz
endWorldXz
styleId
priority
```

`styleId` 用于决定颜色、宽度、层级和材质。`priority` 用于解决同一条边同时属于政治边界和选中边界时的显示顺序。

### 边线细分

每条 hex 边按世界长度切成多个 segment：

```text
segments = ceil(edgeLength / targetSegmentLength)
```

建议初始值：

```text
targetSegmentLength = 4.0 到 8.0
minSegmentsPerEdge = 3
maxSegmentsPerEdge = 12
```

地形越陡、hex 越大，越需要更多分段。第一版可以用固定分段，第二版再按坡度自适应。

### Ribbon 顶点

每个采样点生成左右两个顶点：

```text
center = lerp(start, end, t)
tangent = normalize(end - start)
side = perpendicular(tangent)
left = center + side * halfWidth
right = center - side * halfWidth
```

然后分别采样 `left` 和 `right` 的地形高度，而不是只采样 center。这样宽线在坡面上更稳，不会因为一侧地形更高而穿帮。

高度建议：

```text
height = SampleTerrainHeight(worldXz)
height = max(height, seaLevel + waterClearance)   // 水域线条浮在水面上
finalY = height + surfaceOffset
```

建议初始值：

```text
surfaceOffset = 0.35 到 1.0
waterClearance = 0.4 到 0.8
```

如果有 z-fighting，可以优先略微提高 `surfaceOffset`，其次增加分段，而不是直接开启全局 `NoDepthTest`。

### 转角处理

第一版可以每条边独立生成 ribbon，转角处允许轻微重叠。因为边界线较细，重叠通常不明显。

第二版可以把连续边整理成 polyline，并使用 miter 或 bevel join：

```text
提取边界边
    ↓
按端点连接为 boundary path
    ↓
沿 path 生成连续 ribbon
    ↓
在角点做 bevel 或 round join
```

连续 path 的好处是边界转角更像文明类地图的平滑折线，但实现成本更高。第一版不必强求。

## 材质方案

### 推荐三层线

为了接近文明类边界观感，建议使用三层 mesh 或三套 surface：

```text
shadow / underlay：宽、暗、半透明，用于把边界从地形中托出来
main line：中等宽度，政治或选择主色
highlight：窄、亮、略高，用于黄白色高光
```

示例宽度：

```text
shadowWidth = 3.2
mainWidth = 2.0
highlightWidth = 0.7
```

示例高度：

```text
shadowOffset = 0.35
mainOffset = 0.45
highlightOffset = 0.55
```

### Godot 材质设置

第一版可以使用 `StandardMaterial3D`：

```text
Transparency = Alpha
ShadingMode = Unshaded
CullMode = Disabled
VertexColorUseAsAlbedo = true
NoDepthTest = false
```

可选增强：

```text
EmissionEnabled = true
Emission = 主色
```

默认保持 depth test，让山体或建筑遮挡关系更自然。只有调试模式或 UI 高亮需要永远可见时，再单独启用 `NoDepthTest`。

## 性能策略

### 为什么优先 ribbon mesh

ribbon mesh 的性能核心是批量提交：

- 所有政治边界可以合成一个 `ArrayMesh`。
- hover/selection 使用小 mesh 单独更新。
- 地图模式切换时重建对应 mesh。
- 平时每帧不需要重新采样地形。

相比大量 `Decal` 节点，ribbon mesh 更容易控制 draw call、节点数量和内存。

### Chunk 化

如果完整地图边界很多，第二阶段可以按 chunk 拆分：

```text
boundary_chunk_0_0
boundary_chunk_0_1
...
```

chunk 尺寸建议先按 tile 网格：

```text
16x16 或 32x32 tiles
```

优势：

- 只重建归属变化的 chunk。
- 未来可以按相机距离做显示/LOD。
- 大地图编辑时不会一次重建所有边界。

### LOD

远距离可使用更少分段和更宽的简化线：

```text
近景：segmentsPerEdge = 6 到 12
中景：segmentsPerEdge = 3 到 6
远景：segmentsPerEdge = 1 到 3，甚至只显示政治主边界
```

第一版可以不做动态 LOD，只保留参数入口。

## 推荐开发顺序

状态标记：

```text
[ ] 未开始
[~] 进行中
[x] 完成
[!] 阻塞或需要决策
```

| ID | 状态 | 优先级 | 任务 | 文件/位置 | 验收标准 |
| --- | --- | --- | --- | --- | --- |
| HBR-0001 | [ ] | P0 | 定义边界样式参数 | `HexOverlayRenderer.cs` 或 `HexBoundaryRibbonStyle.cs` | 可配置宽度、偏移、颜色、分段长度 |
| HBR-0002 | [ ] | P0 | 实现共享边去重和边界提取 | `HexOverlayRenderer.cs` / 新 renderer | 政治边界不会重复生成双线 |
| HBR-0003 | [ ] | P0 | 实现贴地 ribbon mesh 构建 | `HexOverlayRenderer.cs` / 新 renderer | 山地、丘陵、海岸边界不明显嵌入地形 |
| HBR-0004 | [ ] | P0 | 将政治边界合并为少量 mesh | `hex_boundary_overlay` | draw call 和节点数可控 |
| HBR-0005 | [ ] | P1 | 为 hover/selection 接入同一套边界生成 | `WorldMapInputController.cs`, renderer | 选中 tile 显示边界轮廓而不是大片盖色 |
| HBR-0006 | [ ] | P1 | 增加三层线材质 | `world/materials/` 或代码材质 | 有暗色托底、主色、亮色高光 |
| HBR-0007 | [ ] | P1 | 输出边界渲染报告 | `world/generated/hex_boundary_ribbon_report.txt` | 报告包含边数、顶点数、分段配置 |
| HBR-0008 | [ ] | P2 | 按 chunk 拆分 mesh | 新 renderer | 大地图更新时可局部重建 |
| HBR-0009 | [ ] | P2 | 连续边界 path 和转角优化 | 新 builder | 政治边界转角更平滑 |
| HBR-0010 | [ ] | P2 | 增加远近 LOD 参数 | 新 renderer | 远景减少顶点或隐藏调试网格 |

## 验收标准

第一版完成时至少满足：

```text
[ ] 政治边界只沿 owner 不同的共享边显示。
[ ] hover/selection 边界不会铺满整块 hex。
[ ] 山地和丘陵上边界不被 Terrain3D 明显顶穿。
[ ] 水域边界显示在水面上方。
[ ] 单次重建后运行时不每帧重建全图边界。
[ ] Godot headless 可以跑完 MVP 流程。
[ ] 没有手动编辑 `.godot/` 缓存。
```

视觉检查建议：

```text
[ ] 平原区线条宽度稳定。
[ ] 山地斜坡上没有大段埋入。
[ ] 海岸线附近没有被水面盖住。
[ ] 远景不会因为过亮或过密影响读图。
[ ] 政治边界颜色比地形 grid 更醒目。
```

## 风险和应对

| 风险 | 影响 | 应对 |
| --- | --- | --- |
| 分段太少 | 边界在山地穿入地形 | 增加 `segmentsPerEdge` 或按坡度自适应 |
| 分段太多 | 顶点数上升、重建变慢 | 只渲染需要的边界，加入 chunk 和 LOD |
| 宽线横跨陡坡 | ribbon 一侧仍可能进地形 | 左右顶点分别采样高度，必要时沿多点最高值抬升 |
| 透明边界排序异常 | 高亮线局部闪烁或层级不稳定 | 三层线拆 surface/mesh，固定 offset，谨慎使用 alpha |
| 水面遮挡边界 | 海岸或海上 grid 被盖住 | 对水下采样使用 `max(height, seaLevel + waterClearance)` |
| 边界和旧 overlay 信息重复 | 画面过乱 | 将旧填色作为调试模式，常规地图模式优先显示 ribbon 边界 |

## 当前推荐下一步

```text
1. 先在 HexOverlayRenderer 内做一个最小 ribbon prototype，只画政治边界。
2. 验证 TerrainInfoMap 采样高度、分段和材质偏移能解决嵌地问题。
3. 通过后再把 hover/selection 和调试网格迁移到同一套 ribbon 生成器。
4. 若边界样式继续扩展，再拆出 HexBoundaryRibbonRenderer。
```
