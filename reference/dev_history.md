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
