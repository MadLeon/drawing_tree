# OE / Simple View 加载性能优化 —— 讨论结论（待下个 session 细化为实施计划）

> 本文件替换了此前"drawing_number 大小写重复"的 plan.md 内容——该问题已确认修复完成
> （`DrawingRepository.cs:131` 已有 `ToUpperInvariant()`，`data/db_changes.sql` 已含 `COLLATE NOCASE` 迁移），
> 相关记录见 `reference/dev_history.md` / git 历史，不再需要保留在 plan.md 中。

## 背景

用户反馈：切换 OE view / Simple view，以及首次点击 OE view 时明显卡顿。本 session 先做了现状调研，
再由用户提出了一套"首次硬加载 + 缓存 + 局部更新"的改造方案，本文件记录双方达成的结论，供下个 session
直接开始细化/实施，不需要重新调研。

---

## 一、现状分析（已核实的代码事实，非猜测）

代码位置：`src/DrawingTree/Controls/AllPosControl.xaml.cs`（"All POs" 页面，code-behind 风格，
OE view 和 Simple view 共用同一份顶层数据 `_allRows`）。

### 1. 控件实例被频繁重建（当前最可能的"首次点击卡顿"主因）

- `MainWindow.xaml.cs` 的 `ShowAllPos()`：每次通过工具栏进入都 `_allPosControl = new AllPosControl();`，
  丢弃已加载的 `_allRows` / `_pdfPartIds`，重新触发 `LoadDataAsync()`。
- 对比：从 PO 详情页点"返回"回到 All POs 时（`OnPoDetailBackToAllPos`），是复用现有实例的，不重新加载。

### 2. 加载入口：`LoadDataAsync()`（`AllPosControl.xaml.cs:332`）

```csharp
(_allRows, _pdfPartIds) = await Task.Run(() => {
    var rows = _repository.GetAllPoLines(activeOnly: activeOnly);   // 全部活跃 order_item 级联查询
    var partIds = rows.Where(r => r.PartId.HasValue)....Distinct();
    var pdfSet = _repository.GetPartIdsWithPdf(partIds);
    return (rows, pdfSet);
});
```

- `GetAllPoLines()`（`PoRepository.cs:244`）就是用户所说的"输出所有活跃 order_item 信息的大级联查询"，
  已存在，直接复用，不需要重新设计。
- 已经用 `Task.Run` 包裹，不阻塞 UI；但此时**不含**子节点信息（符合用户方案里"首次只加载上层节点"的预期）。

### 3. `GetPartIdsWithPdf` 的真实行为（用户要求确认的点，已核实）

`PoRepository.cs:512-543`：

```csharp
cmd.CommandText = "SELECT part_id, file_path FROM drawing_file WHERE is_active = 1 AND part_id IN (...)";
...
if (!string.IsNullOrEmpty(path) && File.Exists(path))   // <- 第534行，对共享盘做存在性探测
    result.Add(partId);
```

**结论**：它已经是"查数据库 `drawing_file` 表"，并不是纯粹在网络盘里找。但查完之后**额外**对每个
`file_path` 做了一次 `File.Exists()`（路径指向 SMB 共享盘，如 `G:\A.E.C.L (CANDU)\...`），这一步才是
真正的网络 IO 开销来源。

**待确认的取舍**（未达成最终结论，需要下个 session 开始前先定）：
- 方案 A：列表加载阶段只信任数据库记录（快，不做 `File.Exists`），真正打开 PDF 时才现场校验 + 报错兜底。
- 方案 B：完全保留现状（准确但慢）。
- 推荐 A，但需要用户拍板确认可接受"列表显示可用、点开时才发现文件缺失"的体验。

### 4. OE view（现状：懒加载，per-row）

- `ApplyOeView()`（`AllPosControl.xaml.cs:494`）：纯内存对 `_allRows` 分组排序，不查库。
- 点击展开箭头 → `OeExpandItem_Click`（`AllPosControl.xaml.cs:823`）：
  `item.CachedChildren ??= _repository.GetChildDrawings(partId)` —— 单根递归 CTE，**同步跑在 UI 线程**，
  只查这一行，代价小但仍会卡一下。
- 问题：`ApplyOeView` 每次调用都 `_oeItems.Clear()` 重建，之前展开行的 `CachedChildren` 一并清空
  （搜索/切换视图后再展开要重新查）。

### 5. Simple View（现状：切视图时批量预取，非纯渲染）

- `ApplySimpleView()`（`AllPosControl.xaml.cs:529`）：每次调用都重建全部行对象。
- 紧接着**同步**调用（未包 `Task.Run`，会阻塞 UI 线程）：
  `GetPartIdsWithMp(topPartIds)`、`GetPartIdsWithDir(topPartIds)`（`AllPosControl.xaml.cs:538-539`）。
- 然后异步调用 `PrefetchChildrenAsync()`（`AllPosControl.xaml.cs:565`，已用 `Task.Run`）：
  `GetAllChildDrawings(所有顶层 partIds)` —— 一次批量递归 CTE，覆盖当前全部有子件的顶层 part
  （逻辑与用户方案里"首次加载时异步预取子节点"的思路一致，可直接复用/挪用这段逻辑）。
- 触发时机：每次切换进入 Simple View、每次搜索防抖、每次筛选——都会重新触发以上查询。

### 6. Edit 后的刷新（当前：全量重载）

- `AllPosControl.xaml.cs:921-923`：`EditItemDialog` 保存成功后，调用方直接 `Reload()` → 完整重跑
  `LoadDataAsync()`（重新查全部 PO 行 + PDF 标记），没有做局部更新。
- `PoRepository.UpdateOrderItemCascade()`（`PoRepository.cs:959`）：级联更新 `order_item` + `part` 字段。

### 7. 数据规模（供评估"首次硬加载"是否可接受）

134 个在职 PO、约 2,000 个 part、1,416 条 `part_tree` 边、78 个有子件的顶层 part。SQL 层面用了带索引的
recursive CTE，单次查询不慢；卡顿主要来自"同步 UI 线程查询" + "重复触发" + "网络盘 File.Exists"的叠加，
不是数据量本身的问题。

---

## 二、用户提出的方案（讨论确认，方向认可）

**前提**：OE view 和 Simple view 使用同一套数据，切换时只改变呈现方式，不重新取数。

### 首次加载

- 触发条件：① 首次点击 Order Entry 按钮；② 用户已在此界面点刷新按钮（**刷新按钮本身后续实现，本次不需要做**）。
- 加载逻辑：
  1. 加载全部活跃 `order_item` 上层节点信息（复用现有 `GetAllPoLines()`，已存在）。
  2. 子节点信息（可展开的部分）放入异步预取，逻辑参照现有 `PrefetchChildrenAsync()`
     （即：把该方法从"仅 Simple View 触发"改为"首次加载时对全部顶层 part 触发一次，OE/Simple 共用结果"）。
  3. MP / DIR 标记查询同样放入异步（即修复现状第 5 点里的同步阻塞问题）。
  4. `GetPartIdsWithPdf()` 的网络盘校验问题——见上文"待确认的取舍"。
  5. 加载结束后，缓存全部查到的信息（顶层行 + 子节点 + MP/DIR/PDF 标记），OE view 展开、Simple view 渲染都只读缓存。

### 视图切换

- 只做界面重构（分组方式不同），不触发任何数据库操作——现状 `ApplyOeView` 已符合，`ApplySimpleView`
  需要把内部的 MP/DIR 查询和预取调用挪出去（挪到首次加载阶段）。

### 更新

- `Edit` 保存成功后：只更新数据库成功对应的那条缓存记录 + 触发一次局部界面重渲染，**不**重新读取全部数据
  （替代现状的 `Reload()` 全量重载）。

---

## 三、讨论中标记的待确认问题（进入实施前需要先拍板）

> **已通过 `/grill-me` 逐一拍板并实施完成**（2026-07-08）：1 采用方案 A（列表阶段跳过 `File.Exists`，
> 点开时发现文件缺失改为弹 `MessageBox` 提示，而非现状的静默日志）；2 OE view 也改为首次全量预取；
> 3 Edit 局部打补丁按 part_id（覆盖 `UpsertPart` ON CONFLICT 影响的共享行）+ po_id（Customer/Contact
> 为 PO 级别共享字段）全局打补丁。详见下方新增的第五节，记录了实施过程中额外发现、本次未处理的两个问题。

1. **`GetPartIdsWithPdf` 的 `File.Exists` 去留**（见上文方案 A/B）。
2. **OE view 是否也要变成"首次全量预取子节点"**：现状 OE view 是按需单行懒加载（用户通常只展开少数行），
   改成和 Simple View 一样首次全量预取，意味着首屏加载变重（虽是异步），换取切换/展开全程无查询。
   需要用户确认这个取舍是有意为之。
3. **Edit 局部更新缓存的粒度**：`UpdateOrderItemCascade` 是级联更新 `order_item` + `part` 字段。若编辑改动了
   `part.drawing_number` / `revision` 这类会影响 `part_tree` 递归结果、且该 part 被多个 `order_item`/PO 共享的
   字段，只 patch "当前这一行"缓存可能不够——需要先确认 `EditItemDialog` 实际允许改动哪些字段，
   判断改动范围是否会波及缓存里的其他条目（如共享同一 part 的其他行、该 part 的子树缓存）。

---

## 四、涉及的关键文件（下个 session 直接定位）

- `src/DrawingTree/Controls/AllPosControl.xaml.cs` —— 主要改造对象：`LoadDataAsync`、`ApplyOeView`、
  `ApplySimpleView`、`PrefetchChildrenAsync`、`OeExpandItem_Click`、Edit 回调（约 921-923 行）。
- `src/DrawingTree/MainWindow.xaml.cs` —— `ShowAllPos()`（实例重建问题）、`OnPoDetailBackToAllPos()`（复用实例的现有范例）。
- `src/DrawingTree/Data/PoRepository.cs` —— `GetAllPoLines`、`GetChildDrawings`、`GetAllChildDrawings`、
  `GetPartIdsWithPdf`、`GetPartIdsWithMp`、`GetPartIdsWithDir`、`UpdateOrderItemCascade`。
- `src/DrawingTree/Dialogs/EditItemDialog.xaml.cs` —— Edit 保存流程，局部更新需要从这里返回足够信息以 patch 缓存。

---

## 五、本次实施中发现、推迟到下次讨论的问题（2026-07-08）

在把第二/三节的方案落地为代码时，发现两个不在原讨论范围内、但同源的问题。本次**未处理**，先记录：

1. **Import Drawing / Edit Parts 工作流返回 All POs 后不刷新**：`OnDrawingEditorReturn` /
   `OnPartEditorReturnToAllPos` / `OnTreeBuilderReturnToAllPos`（`MainWindow.xaml.cs` 约 425-451 行）
   目前复用 `_allPosControl` 实例但不触发任何 reload。这个数据陈旧问题**现状就存在**，只是过去被
   "`ShowAllPos()` 每次都 new 实例"意外掩盖（用户再点一下工具栏 All POs 就能刷新）。本次改造把
   `ShowAllPos()` 也改成了复用实例（不再每次 new），这个"意外刷新"机制随之消失——Import Drawing /
   Edit Parts 后新建的 PO/part 不会再自动出现在列表里，除非未来实现了刷新按钮或专门处理这几个 return
   路径。下次 session 需要决定：是否在这几个 return handler 里显式调用 `_allPosControl.Reload()`。
2. **Edit 改 drawing_number/revision 后 `HasChildren` 短暂不准**：如果编辑把一行的 drawing_number/
   revision 换成另一个"是否有子件"状态不同的 part，`PoListRow.HasChildren`（展开箭头是否显示）不会
   实时更新，会保持编辑前的旧值，直到下次真正刷新才纠正。原因：`HasChildren` 的准确值要靠
   `part_tree` 查询才能知道，而 Edit 局部打补丁的前提就是不再查库。属于低概率边缘场景（需要同时满足
   "改了 drawing_number/revision"且"新旧两个 part 的子件状态不同"），暂不处理。
