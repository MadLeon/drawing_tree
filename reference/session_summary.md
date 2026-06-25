# Project Session Summary

## Last Updated
2026-06-25 | 优化 MP Schedule 工具栏样式并修复搜索框输入延迟

## Current Status
- MP Schedule 工具栏优化完成：返回按钮缩小（28×28/图标 14×14）、搜索框加宽（360px）并改为搜索 Job/Drawing/P.O.、Filter 按钮改用 filter_list 图标、单选框垂直居中
- 搜索框输入延迟问题通过添加 300ms DispatcherTimer debounce 解决，移除了 Description 搜索并改为 P.O. 搜索
- MP Schedule（甘特图）展开按钮已缩小（18×18 / 图标 12×12）；展开时左面板先响应，画布延后重绘
- 子节点甘特图条形图支持点击：打开 StepAssignmentDialog，保存后更新并重绘
- All POs 界面（现标题"Order Entry"）支持 OE 视图与简洁视图切换；简洁视图按 PO 分组，支持展开子图纸
- Import Drawing → Edit Parts → Build Tree 三步工作流通过 All POs PO 标题行"Input Data"入口串联

## Recent Sessions

2026-06-25 - 优化 MP Schedule 工具栏样式并修复搜索框输入延迟
- 添加 FilterListGeo 路径资源，Filter 按钮改用 filter_list 图标 + 文字，高度 24px
- 搜索框宽度从 240 扩至 360，高度增至 28，Placeholder 改为"Job / Drawing / P.O."
- 返回按钮缩小至 28×28，内部图标 14×14
- Day/Week/Month 单选框加 `VerticalContentAlignment="Center"` 确保垂直居中
- 搜索框 TextChanged 改用 300ms DispatcherTimer debounce，消除第一次输入的严重延迟
- 搜索字段移除 Description，改为匹配 `vm.Row.PoNumber`

2026-06-25 - 修复 MP Schedule 展开按钮尺寸、子节点最新数据查询及子节点甘特图点击响应
- XAML 中展开按钮从 20×20 缩小为 18×18，内部图标从 14×14 缩小为 12×12
- `ExpandToggle_Click` 将 `Render()` 推迟到 `DispatcherPriority.Background`，让左面板行更新先完成后再重绘甘特画布
- `GetAllScheduleChildItems` SQL 改为 JOIN 最新 revision（`UPPER(drawing_number) ORDER BY revision DESC LIMIT 1`），`ChildPartId` 指向最新 part 记录
- `GanttCanvas_MouseLeftButtonDown` 重构：新增 `IsChild` 分支，用 `ChildPartId`/`ParentOiId` 查模板、保存步骤、刷新 `_childStepMap`
- 分析 AllPo 展开无延迟（bool 翻转 + WPF binding hidden 元素）与 MP Schedule 不可避免延迟（平铺列表重建 + canvas 整体重绘）的架构差异

2026-06-25 - 实现 MP Schedule 展开功能并修复 WPF 资源前向引用崩溃
- `ScheduleRepository` 新增 `GetChildStepTrackers(orderItemIds, childPartIds)`：JOIN step_tracker 与 process_template，返回 `(OiId, ChildPartId) → List<StepTracker>` 字典
- `ScheduleDisplayRow` 新增 `Steps` 属性；`ManufacturingScheduleControl` 新增 `_childStepMap` 字段，`PrefetchChildrenAsync` 在单一 Task.Run 中批量加载 BOM 和子步骤数据
- `BuildDisplayRows` 子行改用真实步骤计算 StatusText（`{completed}/{total}`），`Steps` 属性传入子行
- `DrawBars` 重构为 `DrawStepBar` 辅助方法，父子行均可渲染 Gantt 条形图
- 修复崩溃（`XamlParseException: Cannot find resource 'LeftRowBorder'`）：将四个样式定义从 DataTemplate 之后移至之前

2026-06-24 - 重构 All POs 界面：简洁视图、CRUD 菜单、Import 工作流链
- 删除主工具栏三个按钮（Import Drawing / Edit Part / Build Drawing Tree），入口改由 All POs 的"Input Data"驱动
- `AllPosControl` 新增简洁视图：按 PO 分组，展开图标懒加载子图纸，PO 标题行含 Tree/Package Tracker 按钮及三点菜单
- `DrawingEditorControl` 新增 `ImportCompleted` 事件和 `PrefilledPoNumber`，`PartEditorControl` 新增 `SaveAllCompleted` 事件，`MainWindow` 串联三步自动导航
- 新建 `NewJobDialog`（含批量创建模式）和 `EditItemDialog`（级联更新 customer/part/order_item）
- `PoRepository` 新增 `MarkAsShipped`、`GetChildDrawings`（递归 CTE）、`CreateOrderItemCascade`、`UpdateOrderItemCascade` 等方法

2026-06-24 - 新增 purchase_order.is_active 全量同步脚本并修复 All POs 界面过滤
- 新建 `scripts/update_po_is_active.py`：`openpyxl` 只读读取 Excel AA 列，temp table JOIN 解析 PO，事务写入 `is_active`；默认 dry-run，`--apply` 才执行
- 18 个单元测试（`scripts/test_update_po_is_active.py`）全部通过，使用 in-memory SQLite + mock openpyxl
- 修复 `apply_changes` 中 "cannot start a transaction within a transaction" 错误：连接改用 `isolation_level=None`
- `PoRepository.GetAllPoLines()` 加 `WHERE po.is_active = 1`，All POs 界面只显示活跃 PO

## Key Decisions
- WPF `UserControl.Resources` 内 DataTemplate 引用的 `StaticResource` 样式必须定义在 DataTemplate **之前**；若样式在 DataTemplate 之后，WPF 在 Dispatcher layout pass 应用模板时找不到资源，抛出 `XamlParseException`（crash）
- Import Drawing / Edit Parts / Build Tree 的入口统一改到 All POs 界面的 PO 三点菜单"Input Data"，三个独立工具栏按钮已移除；工作流通过事件链自动跳转，无需手动切换
- ContextMenu 的 MenuItem 在 WPF 中不继承父 Button 的 DataContext；统一从 `ContextMenu.PlacementTarget`（即三点 Button）的 Tag 取数据
- drawing_number 大小写问题修复顺序：先清理数据再改 Schema，因为 NOCASE 约束会拒绝已有重复数据的导入
- revision 占位版本（rev="-"）与真实 revision 并存时，加载优先选最高 revision（ORDER BY revision DESC）；保存时重定向 order_item 而非批量删除占位版本
- MP Schedule 展开延迟是架构限制：AllPo 用嵌套 hidden 元素（bool 翻转即可），MP Schedule 用平铺列表 + canvas，子行插入必须重建列表并重绘整个画布，无法用 WPF binding 直接优化
