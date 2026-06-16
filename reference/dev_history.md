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
