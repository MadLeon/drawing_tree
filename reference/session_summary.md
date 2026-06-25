# Project Session Summary

## Last Updated
2026-06-25 | 实现 MP Schedule 展开功能并修复 WPF 资源前向引用崩溃

## Current Status
- MP Schedule（甘特图）支持展开子节点：add_box/indeterminate_check_box 图标切换，子节点显示 Drawing、Description、Qty（累计）、Memo（part_note）、Status（分数）、Gantt 条形图
- 子节点 step tracker 数据通过 `GetChildStepTrackers` 批量预加载，与父节点数据合并在单一 Task.Run 中完成
- WPF XAML 资源顺序已修正：`IconBtn`、`LeftRowBorder`、`CellText`、`HeaderText` 样式移至 DataTemplate 之前，消除 `XamlParseException`
- All POs 界面（现标题"Order Entry"）支持 OE 视图与简洁视图切换；简洁视图按 PO 分组，支持展开子图纸
- Import Drawing → Edit Parts → Build Tree 三步工作流通过 All POs PO 标题行"Input Data"入口串联

## Recent Sessions

2026-06-25 - 实现 MP Schedule 展开功能并修复 WPF 资源前向引用崩溃
- `ScheduleRepository` 新增 `GetChildStepTrackers(orderItemIds, childPartIds)`：JOIN step_tracker 与 process_template，返回 `(OiId, ChildPartId) → List<StepTracker>` 字典
- `ScheduleDisplayRow` 新增 `Steps` 属性；`ManufacturingScheduleControl` 新增 `_childStepMap` 字段，`PrefetchChildrenAsync` 在单一 Task.Run 中批量加载 BOM 和子步骤数据
- `BuildDisplayRows` 子行改用真实步骤计算 StatusText（`{completed}/{total}`），`Steps` 属性传入子行
- `DrawBars` 重构为 `DrawStepBar` 辅助方法，父子行均可渲染 Gantt 条形图
- `MemoCell_Click` 新增子节点 memo 更新路径，写回 `_childDataMap` 对应条目
- 修复崩溃（`XamlParseException: Cannot find resource 'LeftRowBorder'`）：将四个样式定义从 DataTemplate 之后移至之前，消除 WPF 解析期前向引用问题

2026-06-24 - 重构 All POs 界面：简洁视图、CRUD 菜单、Import 工作流链
- 删除主工具栏三个按钮（Import Drawing / Edit Part / Build Drawing Tree），入口改由 All POs 的"Input Data"驱动
- `AllPosControl` 新增简洁视图：按 PO 分组，展开图标懒加载子图纸，PO 标题行含 Tree/Package Tracker 按钮及三点菜单
- `DrawingEditorControl` 新增 `ImportCompleted` 事件和 `PrefilledPoNumber`，`PartEditorControl` 新增 `SaveAllCompleted` 事件，`MainWindow` 串联三步自动导航
- `PartEditorRow.DrawingNumber` 从 `init` 改为可设置，LostFocus 时比对值并回写 JSON、刷新该行 DB 信息
- 新建 `NewJobDialog`（含批量创建模式）和 `EditItemDialog`（级联更新 customer/part/order_item）
- `PoRepository` 新增 `MarkAsShipped`、`GetChildDrawings`（递归 CTE）、`CreateOrderItemCascade`、`UpdateOrderItemCascade` 等方法；`PoListRow` 增加 `PartId` 和 `OrderItemId` 字段

2026-06-24 - 新增 purchase_order.is_active 全量同步脚本并修复 All POs 界面过滤
- 新建 `scripts/update_po_is_active.py`：`openpyxl` 只读读取 Excel AA 列，temp table JOIN 解析 PO，事务写入 `is_active`；默认 dry-run，`--apply` 才执行
- 使用 `os.environ["USERPROFILE"]` 构建路径，不硬编码用户名
- 18 个单元测试（`scripts/test_update_po_is_active.py`）全部通过，使用 in-memory SQLite + mock openpyxl
- 修复 `apply_changes` 中 "cannot start a transaction within a transaction" 错误：连接改用 `isolation_level=None`
- `PoRepository.GetAllPoLines()` 加 `WHERE po.is_active = 1`，All POs 界面只显示活跃 PO

2026-06-24 - 修复 PartEditor revision 选取逻辑并实现 PO order_item 自动重定向
- 发现 `GetDrawingInfo(string)` 无 ORDER BY，LIMIT 1 随机返回任一 revision，可能选中占位版本 rev="-"
- 在查询中加 `ORDER BY p.revision DESC`，使真实 revision（ASCII 值高于 "-"）优先
- 新增 `DrawingRepository.RedirectPoOrderItems(poNumber, drawingNumber, targetPartId)`，将 PO 下引用旧 part 的 order_item 批量更新至新 part
- 在 `PartEditorControl.SaveRow()` 的 insert/update 两条成功路径中调用上述方法

2026-06-20 - 修复 PartEditor 保存逻辑并分析 drawing_number 大小写重复问题
- `PartEditorRow.PartId` 从 `init` 改为 `set`（INotifyPropertyChanged），支持创建后赋值
- `PartEditorControl.SaveRow()` 新增自动创建逻辑：若 PartId 为 null，调用 `InsertPart()` + `UpdatePart()` + `UpsertDrawingFile()` 新建记录
- 排查 RT-87000-71200-1004-1-DD-B 数据不更新问题，发现根本原因：SQLite TEXT UNIQUE 约束默认 BINARY 排序，大小写不同视为不同记录，导致 170+ 组重复
- 制定三步修复方案：① SQL 数据合并（保留大写，迁移 FK）→ ② Schema 迁移（加 COLLATE NOCASE）→ ③ InsertPart 代码加 ToUpperInvariant

## Key Decisions
- WPF `UserControl.Resources` 内 DataTemplate 引用的 `StaticResource` 样式必须定义在 DataTemplate **之前**；若样式在 DataTemplate 之后，WPF 在 Dispatcher layout pass 应用模板时找不到资源，抛出 `XamlParseException`（crash）
- Import Drawing / Edit Parts / Build Tree 的入口统一改到 All POs 界面的 PO 三点菜单"Input Data"，三个独立工具栏按钮已移除；工作流通过事件链自动跳转，无需手动切换
- ContextMenu 的 MenuItem 在 WPF 中不继承父 Button 的 DataContext；统一从 `ContextMenu.PlacementTarget`（即三点 Button）的 Tag 取数据
- drawing_number 大小写问题修复顺序：先清理数据再改 Schema，因为 NOCASE 约束会拒绝已有重复数据的导入
- PartEditor 创建新 part 时不弹 ConfirmOverwriteDialog，新建流程直接保存后返回
- InsertPart() 已存在于 DrawingRepository.cs 但原先从未被调用，本次直接接入而非重写
- revision 占位版本（rev="-"）与真实 revision 并存时，加载优先选最高 revision（ORDER BY revision DESC）；保存时重定向 order_item 而非批量删除占位版本
