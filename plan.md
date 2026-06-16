# Manufacturing Schedule 实现方案

## Context

为了追踪每个订单零件的生产实际进度，新增一个甘特图风格的 Manufacturing Schedule 页面。
通过 MainWindow 工具栏的同名按钮进入，界面左侧为订单行基本信息，右侧为 Canvas 绘制的甘特条。

---

## 关键设计决策（已与用户确认）

| 决策点 | 结论 |
|---|---|
| 行实体 | 一行 = 一个 `order_item`，相同图纸在不同 PO 各自独立 |
| 甘特交互 | Canvas 横条（非格子下拉），点击空白弹对话框 |
| Status 列 | 只读，从 step_tracker 推导（第一个有 start_time 但无 end_time 的步骤） |
| 甘特对话框字段 | 工序步骤（必填） + 实际开始日期（必填） + 实际结束日期（可选） |
| Memo 列 | 对应 `part_note`，截断显示，点击弹悬浮面板展示全部 |
| Due Date | `order_item.delivery_required_date` |
| 横条颜色 | 按 `shop_code` 映射固定调色板 |
| 高亮规则 | 超过 Due Date 的行标红；右侧绘制今日竖线 |
| 默认时间单位 | 天，视口初始居中于今天（前后各 30 天） |
| 数据范围 | 仅活跃 PO（`purchase_order.is_active = 1`） |
| 数据库变更 | 无需新增字段，沿用 step_tracker.start_time / end_time |
| 追踪阶段 | 仅追实际，后续再加计划字段 |

---

## 布局结构

```
ManufacturingScheduleControl
├── DockPanel
│   ├── [Top] StackPanel - 工具栏
│   │   ├── Back 按钮（IconBtn 样式）
│   │   ├── TextBlock "Manufacturing Schedule"（标题）
│   │   └── [右对齐] Settings 图标按钮 → 弹出时间单位选择面板
│   └── Grid (2 列)
│       ├── Col 0 (固定 ~640px): 左侧面板
│       │   ├── Row 0: 列标题行 (PO/Job/Customer/Drawing/Desc/Qty/Due/Memo/Status)
│       │   └── Row 1: 数据行 ItemsControl
│       └── Col 1 (star): 右侧面板
│           ├── Row 0: 时间刻度标题 Canvas
│           └── Row 1: 甘特 Canvas（包含横条 + 今日竖线）
└── 垂直滚动：共享外层 ScrollViewer
    水平滚动：仅右侧面板独立 ScrollViewer
```

左侧**不使用 DataGrid**，改用固定行高（32px）的 ItemsControl，
每行是一个手动定义列宽的 Grid，与现有 AllPosControl 的样式保持一致。

---

## 文件清单

### 新建文件

#### `src/DrawingTree/Data/ScheduleRepository.cs`

方法：
- `GetScheduleRows()` → `List<ScheduleRow>`
  - JOIN: order_item → job → purchase_order → customer_contact → customer → part
  - 过滤: `purchase_order.is_active = 1`
  - 排序: po_number, job_number, line_number
- `GetStepTrackers(int orderItemId)` → `List<ScheduleStepTracker>`
  - JOIN: step_tracker → process_template（取 shop_code / description）
  - 仅取有 start_time 的记录
- `UpsertStepTracker(int orderItemId, int processTemplateId, string startTime, string? endTime)`
  - 先查是否存在记录；存在则 UPDATE，不存在则 INSERT
- `GetProcessTemplate(int partId)` → `List<ProcessTemplateStep>`
  - 复用 PartRepository 查询逻辑，返回 (id, row_number, shop_code, description)
- `GetPartNotes(int partId)` → 复用 `PartRepository.GetPartNotes()`，不重复写

数据模型（Records）：
```csharp
record ScheduleRow(
    int OrderItemId, int PartId,
    string PoNumber, string JobNumber, string? CustomerName,
    string? DrawingNumber, string? Description, int Quantity,
    string? DueDate);

record ScheduleStepTracker(
    int Id, int ProcessTemplateId,
    string ShopCode, string? Description,
    string? StartTime, string? EndTime);

record ProcessTemplateStep(int Id, int RowNumber, string ShopCode, string? Description);
```

#### `src/DrawingTree/Controls/ManufacturingScheduleControl.xaml/.cs`

核心组件：
1. **左侧列表**：ItemsControl，每行固定高度 32px，列宽参考：
   - PO(100) / Job(80) / Customer(100) / Drawing(160) / Desc(150) / Qty(50) / Due(90) / Memo(80) / Status(120)

2. **Memo 单元格**：TextBlock（单行截断） + 点击后弹出 Popup，列出所有 part_note

3. **Status 列**：只读 TextBlock，从 step_tracker 推导：
   - 无任何 start_time → "Not Started"
   - 有 start_time 但 end_time 为空的第一步 → "Step N: [shop_code]"
   - 所有步骤均有 end_time → "Complete"

4. **右侧甘特 Canvas**：
   - `DrawTimeHeader()` — 绘制天/周/月刻度
   - `DrawTodayLine()` — 绘制红色今日竖线
   - `DrawBarsForRow(row, yOffset, steps)` — 按 shop_code 着色绘制横条
   - `HitTestBar(x, y)` — 返回点击位置对应的 bar（用于编辑）
   - 点击空白 → `StepAssignmentDialog`

5. **颜色映射**（shop_code → Color）：
   ```csharp
   static readonly Dictionary<string, Color> ShopColors = new() { ... };
   // 未知 shop_code 用哈希值取调色板颜色
   ```

6. **滚动同步**：
   - 外层 ScrollViewer 控制垂直（左右一起动）
   - 内层 ScrollViewer（仅右侧）控制水平（时间刻度头和甘特行联动）
   - 水平 offset 变化时重绘今日线位置

7. **Settings 面板**（ToggleButton + Popup）：
   - 时间单位：天 / 周 / 月（RadioButton 选择）
   - 切换后重新计算列宽并刷新 Canvas

#### `src/DrawingTree/Controls/StepAssignmentDialog.xaml/.cs`

新建简单的 Window 对话框：
- 下拉列表：process_template 步骤（来自 ScheduleRepository.GetProcessTemplate）
- DatePicker：实际开始日期（必填）
- DatePicker：实际结束日期（可选）
- OK / Cancel 按钮
- 返回 `(processTemplateId, startDate, endDate?)` 或 null（取消）

### 修改文件

#### `src/DrawingTree/MainWindow.xaml`

在 `AllPosButton` 后新增：
```xaml
<Button x:Name="ManufacturingScheduleButton"
        Content="Manufacturing Schedule" Width="180"
        Margin="0,0,10,0" Click="ManufacturingScheduleButton_Click"/>
```

#### `src/DrawingTree/MainWindow.xaml.cs`

新增 `ManufacturingScheduleButton_Click`，模式与 `AllPosButton_Click` 一致：
```csharp
private void ManufacturingScheduleButton_Click(object sender, RoutedEventArgs e)
{
    MainDisplayArea.Children.Clear();
    var ctrl = new ManufacturingScheduleControl();
    ctrl.BackRequested += (_, _) => { MainDisplayArea.Children.Clear(); };
    MainDisplayArea.Children.Add(ctrl);
}
```

---

## Status 推导逻辑（伪代码）

```csharp
string DeriveStatus(List<ScheduleStepTracker> steps)
{
    if (steps.Count == 0) return "Not Started";
    var inProgress = steps.FirstOrDefault(s => s.StartTime != null && s.EndTime == null);
    if (inProgress != null) return $"Step {inProgress.RowNumber}: {inProgress.ShopCode}";
    if (steps.All(s => s.EndTime != null)) return "Complete";
    return "Not Started";
}
```

---

## 甘特条绘制逻辑（伪代码）

```csharp
// Each day = _dayWidth pixels (e.g., 30px at day view)
double DateToX(DateTime date) =>
    (date - _viewportStart).TotalDays * _dayWidth;

void DrawBar(ScheduleStepTracker step, double yOffset)
{
    var x1 = DateToX(ParseDate(step.StartTime));
    var x2 = step.EndTime != null
        ? DateToX(ParseDate(step.EndTime))
        : DateToX(DateTime.Today);  // 进行中的条延伸到今天
    var color = GetShopColor(step.ShopCode);
    // Draw rectangle on Canvas at (x1, yOffset, width=x2-x1, height=28)
    // Draw step.ShopCode text inside if width > 40px
}
```

---

## 数据库变更

无需修改 schema。

沿用 `step_tracker` 现有字段：
- `start_time` / `end_time`（TEXT，ISO 日期格式）
- `status`（暂时不使用，Status 列从 start_time/end_time 推导）

UPSERT 策略：基于 `(order_item_id, process_template_id)` 查找已有记录，
存在则 UPDATE，不存在则 INSERT（应用层处理，不加 UNIQUE 约束，
避免影响条码扫描可能产生的多条记录）。

记录于 `data/db_changes.sql`：`-- No schema changes for Manufacturing Schedule Phase 1`

---

## 验证方案

1. 构建应用，点击 "Manufacturing Schedule" 按钮进入页面
2. 确认左侧列表正确显示活跃 PO 的 order_item（PO/Job/Drawing/Qty/Due Date）
3. 在右侧甘特空白区域点击 → 确认对话框弹出并可选步骤和日期
4. 确认后，右侧出现对应颜色横条，左侧 Status 列自动更新
5. 再次点击同一步骤 → 确认可以修改结束日期（UPSERT 生效）
6. 确认今日竖线位于正确位置
7. 切换时间单位（天/周/月）→ 确认甘特条相对位置保持正确
8. 确认超过 Due Date 的行显示红色标记
9. 点击 Memo 单元格 → 确认弹出笔记面板
10. 点击 Back → 确认返回正常
