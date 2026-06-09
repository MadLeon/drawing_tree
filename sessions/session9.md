# Session 9: 应用数据库脚本

## 任务描述

本session主要任务: 应用数据库脚本

- 将 session8 的脚本, 在程序中进行应用
- 替换硬编码数据, 不再生成 {PO号}_tree.json 文件, 而是直接从数据库中读取数据进行展示
- 仅保留 {PO号}_import.json 文件的逻辑

执行修改计划：
1. 在 file path 文本框同一行右对齐添加一个 browse 按钮
2. 在图纸信息区的最下方右对齐添加一个 Save 按钮
3. 在读取数据库的过程中, 展示一个 loading 的状态

---

## 一句话总结

将 session8 的 SQL 脚本集成到程序中，建立数据访问层，替换 mock/JSON 数据流，并完成 Browse 按钮、Info Save 按钮、Loading 状态三项 UI 改进。

---

## 理解与推断

- **新增数据访问层**：新建 `src/DrawingTree/Data/` 目录，包含 `DatabaseConnectionFactory.cs`（dev/prod 路径切换）、`PoRepository.cs`（PO 级联 + 树形递归）、`DrawingRepository.cs`（图纸信息查/写 + 树形关系保存），csproj 添加 `Microsoft.Data.Sqlite`
- **`DrawingInfo` / `DrawingNode` 模型扩展**：各添加 `PartId`（int?）/ `PartTreeId`（int?）字段，用于关联数据库记录
- **替换 `SetupRootNodesFromPo()`**：由调用 `PoTreeService`（mock）改为调用 `PoRepository.GetGroupsForPo()`（DB），同时对每个根节点调用 `PoRepository.GetPartTree()` 递归预加载已保存的树形结构，并从左侧列表移除已在树中的图纸
- **替换 Toolbar Save 逻辑**：`SaveButton_Click()` 由写 `*_tree.json` 改为调用 `DrawingRepository.SaveTree()`（INSERT/UPDATE/WARNING `part_tree` 表），删除 `SerializeNode()`
- **Info panel Browse 按钮**：在 File Path 文本框同行右侧添加 Browse 按钮，点击弹出文件选择对话框；同时将 `InfoFilePath` 改为可编辑
- **Info panel Save 按钮**：在 Info panel 最下方添加 Save 按钮，点击将 revision/description/is_assembly/file_path 保存到 DB；成功无提示，失败显示错误
- **Loading 状态**：主区域叠加半透明遮罩 + ProgressBar，数据库操作期间显示，用 async/await + Task.Run 避免阻塞 UI
- **`DrawingViewerControl` 数据来源替换**：改为接受 PO 号并调用 `PoRepository` 从 DB 加载树；`MainWindow` View Drawings 入口改为扫描 `*_import.json`

---

## TODO 步骤

- [x] 1. csproj 添加 `Microsoft.Data.Sqlite` 包并验证编译
- [x] 2. 新建 `DatabaseConnectionFactory.cs`
- [x] 3. 新建 `PoRepository.cs`（PO 级联查询 + 树形递归查询）
- [x] 4. 新建 `DrawingRepository.cs`（图纸信息 UPSERT + 树形关系保存）
- [x] 5. 扩展 `DrawingInfo` / `DrawingNode` 模型，添加 `PartId` / `PartTreeId` 字段
- [x] 6. 替换 `TreeBuilderControl` 数据加载（mock → DB，预加载树形结构，Loading 状态）
- [x] 7. 替换 `TreeBuilderControl` Toolbar Save（JSON → DB part_tree）
- [x] 8. Info panel：Browse 按钮 + `InfoFilePath` 改为可编辑 + Info Save 按钮
- [x] 9. 替换 `DrawingViewerControl` 数据来源 + 更新 `MainWindow` View Drawings 入口

---

## Session 内容总结

- csproj 添加 `Microsoft.Data.Sqlite 9.0.5`，编译验证通过
- 新建 `Data/DatabaseConnectionFactory.cs`：dev/prod 路径切换，`OpenDevConnection()` / `OpenProdConnection()`
- 新建 `Data/PoRepository.cs`：`GetGroupsForPo()` 替代 mock，`GetPartTree()` 用 SQLite CTE 递归加载已保存树
- 新建 `Data/DrawingRepository.cs`：`GetDrawingInfo()` / `InsertPart()` / `UpdatePart()` / `UpsertDrawingFile()` / `SaveTree()`
- 模型扩展：`PoTreeGroup.PartId`、`DrawingInfo.PartId`（int?）、`DrawingNode.PartTreeId`（int?）
- `TreeBuilderControl`：移除 `PoTreeService` mock，改为 `LoadFromDatabaseAsync` 异步加载；添加 `SetupRootNodeFromGroup` / `AttachDbChildren`（含缺失图纸 Warning 逻辑）；XAML 添加 Loading 遮罩
- `TreeBuilderControl` Toolbar Save：由写 `*_tree.json` 改为调用 `DrawingRepository.SaveTree()`
- Info panel：`InfoFilePath` 改为可编辑，添加 Browse 按钮（OpenFileDialog）和 Info Save 按钮（UpdatePart + UpsertDrawingFile），失败显示错误文本，成功静默
- `DrawingViewerControl`：新增 `LoadFromDatabase(poName)` / `LoadFromDatabaseAsync`，从 DB 加载 PO 树；保留 `LoadFromJsonFile` 供兼容
- `MainWindow`：View Drawings 改扫 `*_import.json`，提取 PO 名后调用 `DrawingViewerControl.LoadFromDatabase()`

---

## 操作及决策细节

- **`System.IO.Path` 缺失**：`DatabaseConnectionFactory.cs` 初始未引入 `using System.IO`，编译报 CS0103；补充后通过
- **`System.Text.Json` 保留**：移除 JSON using 后 `LoadFromJsonFile` 仍用 `JsonDocument` 解析 import 文件，恢复引用
- **`PoRepository.GetPartTree` 返回结构**：CTE 查询只返回非根节点行（`WHERE parent_part_id IS NOT NULL`），在内存中按 `parent_part_id` 建树；`parent_part_id == rootPartId` 的节点作为 top-level children 返回
- **`AttachDbChildren` 一致性检查**：DB 树节点若不在 import JSON 左栏中，写 Warning 日志并跳过（包括其子树），防止构造区出现与导入数据不符的图纸
- **Toolbar Save 简化**：移除 `SerializeNode()` 和 JSON 写文件逻辑，`SaveTree` 递归处理每层父子关系；新边写入后将 `part_tree.id` 回写到 `DrawingNode.PartTreeId`，下次 Save 时走 UPDATE 分支
- **Info Save 设计**：Save 按钮同时写 `part`（UpdatePart）和 `drawing_file`（UpsertDrawingFile）；file_path 为空时跳过文件写入；`DrawingNode.PartTreeId` 不在此处更新（树结构由 Toolbar Save 管理）
- **View Drawings 入口**：改扫 `*_import.json` 而非 `*_tree.json`（后者不再生成），PO 名通过裁掉 `_import.json` 后缀得到，传给 `DrawingViewerControl.LoadFromDatabase()`
