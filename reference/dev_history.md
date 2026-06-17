# Development History

### 1. 解释构造页面 Save 按钮的完整流程
从 UI 点击事件到数据库事务提交，梳理了 `SaveButton_Click → SaveTree → SaveNodeChildren` 的调用链及其与 `part_tree`、`part` 两张表的互动。

### 2. 解释 `SaveNodeChildren()` 的逻辑
用普通语言描述了该方法"对照数据库逐层检查每对父子关系，按新增/已有/遗留三种情况处理"的行为。

### 3. 修复保存逻辑的数据一致性问题
- 识别出原有设计缺陷：被用户从树上移除的零件，数据库里的关系记录只写警告日志而不删除，导致下次加载时删除操作"失效"。
- 将 `CheckOrphanedEdges()` 改写为 `DeleteRemovedChildren()`，对界面上已不存在的父子关系执行 `DELETE FROM part_tree`。
- 同步处理 `part.has_parent`：若某零件在删除后不再挂在任何父节点下，将其 `has_parent` 重置为 `0`。

### 4. 新增保存前确认对话框
- 在 `DrawingRepository.cs` 新增 `ComputeTreeChanges()` 方法，对当前树做只读扫描，统计新增/删除/修改数量及被删除关系的明细。
- 新增 `ConfirmSaveDialog.xaml/.cs`，以三色数字摘要展示变更数量，并逐条列出被删除的父→子关系。
- 修改 `SaveButton_Click`：先计算变更，无变更时直接提示；有变更则弹出确认框，用户取消则不写入数据库。

### 5. 解释多 Job/Line 场景下的保存逻辑
确认 `_rootNodes` 中每个总装图节点代表一个 Job+Line 组合，`SaveTree` 在单一事务内依次处理所有根节点，各棵子树独立核对、互不干扰。

### 6. 图纸查看界面升级：按 part.id 加载树 + Info 面板编辑功能 + PDF 滚动条修复

- 在 `PoRepository` 新增 `GetRootPartId()`，通过 SQLite 递归 CTE 从任意 part.id 向上追溯至无父节点的根
- 在 `DrawingRepository` 新增 `GetDrawingInfo(int partId)` 重载，支持按 part.id 查询图纸信息
- 在 `DrawingViewerControl` 新增 `LoadFromPartId()`，自动找到根节点、加载完整子树、并初始高亮目标图纸
- 修改 `MainWindow.ViewDrawingsButton_Click` 为开发阶段硬编码入口（part.id=3490），跳过 PO 选择对话框
- 将 Drawing Info 面板的 Revision、Description、Quantity、Is Assembly、File Path 改为可编辑字段，并添加 Browse 和 Save 按钮
- Save 按钮调用 `UpdatePart`、`UpsertDrawingFile`、`UpdatePartTreeQuantity` 将修改持久化到数据库，成功时显示 Snackbar
- 修复 PDF 查看器 bug：在 `PdfScrollViewer_PreviewMouseLeftButtonDown` 中新增 `IsScrollBarInPath()` 检测，点击滚动条时不启动拖拽平移

### 7. 新增搜索功能：按图纸号查询并导航至图纸树查看界面

- 新建 `SearchControl.xaml/.cs`，包含 500ms 防抖的实时搜索输入框和结果 DataGrid
- 在 `DrawingRepository` 新增 `SearchParts()`，对 `part.drawing_number` 执行 LIKE 模糊查询（LIMIT 100）
- 修改 `MainWindow.xaml.cs` 实现 `SearchButton_Click`，点击后显示搜索界面
- 新建 `data/db_changes.sql` 用于追踪开发期间的数据库 DDL 变更，并在 `CLAUDE.md` 中记录其用途

### 8. 升级搜索查询逻辑，重构搜索界面布局，添加 Back 导航

- 在 `DrawingRepository` 新增 `SearchPartsWithJobContext()`，使用递归 CTE 从匹配图纸逐层向上遍历 `part_tree`，查找包含该图纸的所有 PO 和 Job（任意层级）
- 新增 `SearchResultRow` 记录类型，结果字段改为 PO / Job No / Dwg No / Rev. / Description
- 重构搜索工具栏：移除 Home 按钮，搜索框固定占 1/3 宽度，并添加 "Drawing Number / PO Number / Job Number" 占位文字
- 在 `DrawingViewerControl` 工具栏最左侧添加 Back 按钮（初始隐藏），点击后返回保留了原有搜索内容和结果的搜索界面
- 修改 `MainWindow.xaml.cs` 导航逻辑：从搜索跳转到查看界面时保留 `_searchControl` 引用，点 Home 时才完全清理

### 9. 修改搜索界面样式

调整搜索结果表格的列宽、对齐方式和选中效果，提升可读性。
- PO 列宽度从 120 逐步增加到 188（共两次加宽请求）
- 新增 `BodyCell` 样式统一单元格与表头的左侧 padding，解决文字错位问题
- 自定义 `DataGridCell` 模板去除单元格边框/焦点框，选中时整行通过 `IsSelected` 触发器变蓝，不再出现单独的黑框

### 10. 新增 All POs 列表页

新增一个展示数据库中全部采购订单明细的列表页面，作为浏览入口。
- 新建 `AllPosControl.xaml/.cs`，复用搜索页的表头/单元格样式
- 在 `PoRepository` 新增 `GetAllPoLines()`，级联查询 purchase_order→job→order_item→part/customer/customer_contact
- 在 `MainWindow` 工具栏新增 "All POs" 按钮及导航逻辑，View 按钮先做占位日志
- 调整 Contact/Drawing Number 列宽并在 Rev. 后新增 Description 列

### 11. 新增单个 PO 详情页

实现点击 All POs 列表的 View 按钮后跳转的 PO 详情页，按 Job 分组展示所有订单行及其 BOM 树。
- 在 `PoRepository` 新增 `GetPoHeader()`/`GetPoOrderItems()`，在 `DrawingNode` 模型新增 `ReleaseDate`/`DueDate` 属性
- 新建 `PoDetailControl.xaml/.cs`，复用 `DrawingViewerControl` 的树形连接线样式，根节点显示日期+查看树按钮，子节点留空
- 在 `MainWindow` 中接入 AllPos→PoDetail→DrawingViewer 的多级 Back 导航链路，Home 按钮清空整条导航栈
- 根据反馈调整布局：取消行内树形展开改为扁平零件列表，Release/Due Date 脱离表格独立显示在 Line 信息上方，Notes 区域移到 Job 区域上方

### 12. 新增 Part 详情页

实现从 PO 详情页 BOM 行的"打开零件"按钮进入的零件详情页，包含通用信息与按订单区分的工艺执行记录。
- 在 `PoOrderItemRow` 新增 `OrderItemId` 字段，BOM 子节点统一继承根节点（order_item）的订单上下文
- 新建 `PartRepository.cs`：`GetPartHeader`（is_assembly 保留三态 Yes/No/Unknown）、`GetDrawingFiles`（Active 置顶按时间降序）、`GetProcessSteps`（process_template 左连接 step_tracker 按 order_item 过滤）、`GetPartNotes`/`AddPartNote`（真实接入 part_note 表）
- 新建 `PartDetailControl.xaml/.cs`：Notes（真实增删改查）→ 基本信息+Tree View 按钮 → Drawing PDF 列表 → DIR 占位 → Process Template 表
- 在 `MainWindow` 接入 PartDetail↔PoDetail 及 PartDetail→DrawingViewer 的 Back 导航链路

### 13. 新增 Manufacturing Schedule 甘特图页面

实现生产进度甘特图，通过工具栏 "Mfg Schedule" 按钮进入，左侧为订单行列表，右侧为 Canvas 绘制的甘特条。
- 新建 `ScheduleRepository.cs`：`GetScheduleViewModels()`（三表 JOIN 批量加载活跃 PO 的 order_item，关联 step_tracker 和最新 part_note）、`GetStepTrackers()`、`GetProcessTemplate()`、`UpsertStepTracker()`（应用层 UPSERT）
- 数据模型：`ScheduleRow`、`ScheduleStepTracker`、`ProcessTemplateStep`、`ScheduleViewModel`（含 `IsOverdue`、`StatusText` 计算属性）
- 新建 `StepAssignmentDialog.xaml/.cs`：步骤下拉 + 开始/结束 DatePicker 的简单对话框，返回 `StepAssignmentResult`
- 新建 `ManufacturingScheduleControl.xaml/.cs`：
  - 左侧固定宽度（930px）ItemsControl，逾期行标浅红背景，Due Date 红色加粗，Status 颜色分级
  - 右侧横向 ScrollViewer 内 Canvas 绘制甘特条，按 `shop_code` 映射颜色
  - 固定顶部时间刻度条（TimeHeaderCanvas）随水平滚动偏移重绘
  - 今日红色竖线、逾期 Due Date 虚线标注
  - 点击甘特 Canvas：识别行+hit-test 已有条 → 弹 StepAssignmentDialog → `UpsertStepTracker` → 刷新该行
  - Day/Week/Month 三种视图缩放（RadioButton 切换），加载后自动居中于今天
  - Memo 列点击弹 Popup 显示最新笔记
- 在 `MainWindow` 新增 "Mfg Schedule" 按钮及对应 Back 导航

### 14. 优化 Manufacturing Schedule 页面交互体验

优化 Manufacturing Schedule 甘特图页面的多项交互与视觉问题，包括返回按钮风格、列宽、滚动行为、Memo 点击修复及图纸号导航。

- 返回按钮改为与 PoDetail 页一致的图标风格（ChevronLeft SVG + IconBtn 样式）
- PO 列宽从 100px 拓宽至 125px（+1/4），Drawing 列从 160px 拓宽至 200px（+1/4），左面板总宽从 930→995px
- 修复 Memo 格点击无反应：将 TextBlock 换为透明 Border 包裹，解决空文本时零尺寸无法命中的问题
- 右侧甘特区域鼠标滚轮改为横向滚动：在 GanttHScroll 添加 `PreviewMouseWheel` 拦截并标记 `Handled`，阻止外层纵向滚动
- 在右侧甘特区域下方（DockPanel.Dock="Bottom"）新增固定底部横向 `ScrollBar`，与 GanttHScroll 双向同步，原内置滚动条设为 Hidden
- 图纸号改为蓝色下划线可点击链接，点击触发 `OpenPartRequested` 事件，传递 `PartId` 和 `OrderItemId`
- 在 MainWindow 新增 `OnScheduleOpenPart` / `OnPartDetailBackToSchedule` 处理器，接入 Schedule→PartDetail→DrawingViewer 的完整导航链路
