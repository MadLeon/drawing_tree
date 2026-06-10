# Session 8: 编写测试数据库脚本

## 任务描述

本session主要任务: 编写测试数据库脚本

背景:
- 下一阶段的任务是使用真实数据替换所有 json 数据和硬编码数据, 而本阶段需要编写测试数据库脚本, 以便后续使用
- 参考你的修改计划 data/app-quirky-breeze.md
- 参考 /database 这个 skill 获取数据库相关知识

数据流分析:
**Import Drawing**: 
[in]: 用户输入 -> [out]: {PO号}_import.json (本地)

**Build Drawing Tree**: 
[in]: {PO号}_import.json
1. 根据 json 文件提供的订单号, 级联查询所有该订单下的 job 和 order_item (代替硬编码)
	- 用于在构造区列出所有关联的 job_number(job), line_number(order_item) 和 part(order_item, 最上层), 同时对构造区进行分区
2. 对于 json 文件中的每个图纸, 根据图纸号级联查询 part 表和 drawing_file 表
	- 用于在图纸信息区显示每张图纸的信息
	- 若图纸不存在, 则进行添加操作
3. 根据每个最上层图纸号, 递归查询 part 表和 part_tree 表, 如果存在父子关系, 则直接在构造区构建树形关系
	- 用于在构造区显示树形结构
	- 注意1: 如果存在父子关系, 构建完成后, 需要将所有涉及到的图纸号从左侧的图纸列表中移除, 以免重复显示; 
	- 注意2: 如果左侧图纸列表中不存在右边构建树结构的图纸, 则告知用户哪些图纸不存在, 造成了当前图纸列表与数据库中的数据不一致, 同时输出 warning 等级的日志, 并给出足够的信息
	- 注意3: 如存在图纸缺失的情况, 不要将缺失的图纸添加到构造区, 以免造成数据不一致;
4. 图纸信息区的内容应该可以进行修改
	- 为此功能改变 UI, 见修改计划1和2
5. 在左侧图纸列表区中点击添加按钮后弹出的 add drawing 对话框中, 用户输入图纸号和版本号后, 级联查询 part 表和 drawing_file 表
	- 如果结果存在, 则将 is_active=1 的图纸文件地址填入文本框, 用户可以修改后点击 Save 按钮进行保存;
	- 如果结果不存在, 则新建并告知用户, 同时输出 warning 等级的日志, 并给出足够的信息
6. 点击工具栏的 Save 按钮后, 保存树形关系
	- 对每个父子关系, 
		- 如果已存在关联, 则对比数据, 如果数据不同则进行更新; 
		- 如果不存在关联, 则进行添加; 
		- 如果数据库中存在但当前树形结构中不存在的关联, 则输出 warning 等级的日志, 并给出足够的信息

**修改计划**
1. 在 file path 文本框同一行右对齐添加一个 browse 按钮
	- 点击弹出文件选择对话框, 选择图纸 PDF 文件后, 将文件路径填入文本框
2. 在图纸信息区的最下方右对齐添加一个 Save 按钮
	- 修改不成功时显示错误信息, 修改成功时不显示任何信息, 直接更新界面上的图纸信息
3. 在读取数据库的过程中, 展示一个 loading 的状态, 以提升用户体验

---

## 一句话总结

为 Build Drawing Tree 功能编写完整的 SQL 测试脚本，覆盖 PO 级联查询、图纸信息查/写、树形递归查询、Add Drawing 查询和 Save 树形关系五个场景，作为后续 C# Repository 实现的规格参考。

---

## 理解与推断

- **本 session 范围**：只编写 SQL 脚本（`db/queries/*.sql`），不修改任何 C# 代码；修改计划（Browse 按钮、Save 按钮、Loading 状态）属于下一阶段
- **目标数据库**：`data/record.db`，使用 PO 号 `RT79-87630-PN-R005` 作为测试数据；当前 mock 返回 job `72395` / line `1` / drawing `RT-87630-71254-1000-1-GA-D`，SQL 需返回一致结果以便验证
- **脚本1 - PO 级联查询** (`po_tree.sql`)：`purchase_order → job → order_item → part`，替代 `PoTreeService.GetMockGroups()` 中的硬编码
- **脚本2 - 图纸信息查询** (`drawing_info.sql`)：根据图纸号查询 `part + drawing_file(is_active=1)`；附 INSERT 模板（part / drawing_file 不存在时新建）
- **脚本3 - 树形递归查询** (`part_tree.sql`)：以最上层图纸号为根，用 SQLite CTE 递归遍历 `part_tree`，返回完整父子关系列表
- **脚本4 - Add Drawing 查询** (`add_drawing.sql`)：根据 `drawing_number + revision` 查询 `part`，再查 `drawing_file(is_active=1)`；附 INSERT 模板（part 不存在时新建）
- **脚本5 - Save 树形关系** (`save_tree.sql`)：`part_tree` 表的 INSERT（新关系）/ UPDATE（数量变化）/ SELECT（检查数据库有但树中没有的孤立关系）

---

## TODO 步骤

- [x] 1. 创建 `db/queries/` 目录结构，备份数据库
- [x] 2. 编写 PO 级联查询脚本 (`po_tree.sql`)
- [x] 3. 编写图纸信息查询与新建脚本 (`drawing_info.sql`)
- [x] 4. 编写树形递归查询脚本 (`part_tree.sql`)
- [x] 5. 编写 Add Drawing 对话框脚本 (`add_drawing.sql`)
- [x] 6. 编写 Save 树形关系脚本 (`save_tree.sql`)

---

## Session 内容总结

- 创建 `db/queries/` 目录并备份 `data/record.db`
- 编写 `po_tree.sql`：PO 级联查询（purchase_order → job → order_item → part），验证 PO `RT79-87630-PN-R005` 返回 job `72395` / line `1` / drawing `RT-87630-71254-1000-1-GA-D`
- 编写 `drawing_info.sql`：图纸信息查询与 UPSERT，覆盖 part 查询、drawing_file 查询、part 新建模板、drawing_file UPSERT 模板
- 编写 `part_tree.sql`：SQLite CTE 递归查询，从顶层图纸出发遍历所有父子层级，附单层子节点查询
- 编写 `add_drawing.sql`：Add Drawing 对话框的精确查询、模糊搜索、part 新建模板、drawing_file UPSERT 模板
- 编写 `save_tree.sql`：Save 树形关系的存在检查、diff 查询、INSERT/UPDATE 模板、孤立关系检测查询（Case C WARNING）

---

## 操作及决策细节

- **revision 为 `'2'` 而非 `'D'`**：数据库中 `RT-87630-71254-1000-1-GA-D` 的实际 revision 字段值为 `'2'`，与图纸号中的字母后缀无关；在 `part_tree.sql` 及后续脚本中统一修正

- **drawing_file UNIQUE 约束冲突**：`drawing_file.file_path` 有 UNIQUE 约束，G 盘扫描时已将文件路径导入表中，直接 `INSERT` 会报 `SQLITE_CONSTRAINT_UNIQUE`；改用 `INSERT ... ON CONFLICT(file_path) DO UPDATE SET` UPSERT，文件路径已存在时更新 `part_id / is_active / revision / updated_at`

- **DBeaver auto-commit 与 BEGIN/COMMIT 冲突**：DBeaver 默认 auto-commit 模式下每条语句自动提交，显式 `BEGIN/COMMIT` 导致 `cannot commit - no transaction is active` 错误；移除脚本中的事务控制语句，改为注释说明"C# 端用 `BeginTransaction()` 包裹"，Step A 和 Step B 在 DBeaver 中分别执行

- **脚本定位**：所有 `.sql` 文件作为 C# Repository 实现的规格参考，存放于 `db/queries/`；C# 集成时参数化查询直接对应脚本中的硬编码测试值
