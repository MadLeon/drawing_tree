# Session: Search AllPO Schedule

## 1. All PO 页面排序与分组

对 All POs 页面的条目按 PO 分组、组间按 OE 升序排列、组内按 Line Number 升序排列，并在组间添加半行间距。

- 修改 `AllPosControl.xaml.cs`：用 `ListCollectionView` 替代直接赋值，添加 `PropertyGroupDescription`（按 PoNumber 分组）和 `SortDescription`（按 OeNumber、LineNumber 排序）。
- 修改 `AllPosControl.xaml`：添加自定义 `DataGrid.GroupStyle`，通过 `ControlTemplate` 在每组顶部插入 12px 透明 `Border` 作为组间间距，去除默认折叠头。

## 2. Search 页面支持 PO 和 Job 搜索

扩展搜索功能，使输入 PO 号或 Job 号也能返回相关条目。

- 修改 `DrawingRepository.cs` 中 `SearchPartsWithJobContext`：将原单分支 UNION 改为 UNION ALL 三分支（drawing / po / job），各携带 `match_source` 整数标识；在 C# 层按 `(PoNumber, JobNumber, DrawingNumber)` 去重，drawing 匹配优先级最高。
- 新增 `SearchMatchSource` 枚举（`Drawing=1, Po=2, Job=3`）和 `PoId` 字段到 `SearchResultRow` record。

## 3. Search 结果按匹配类型分流导航

点击搜索结果的 View 按钮时，drawing 匹配跳转图纸查看页，PO/Job 匹配跳转单 PO 详情页。

- 修改 `SearchControl.xaml`：View 按钮 `Tag` 由 `{Binding PartId}` 改为 `{Binding}`（绑定整行）。
- 修改 `SearchControl.xaml.cs`：新增 `NavigateToPoRequested` 事件；`ViewButton_Click` 根据 `MatchSource` 分别触发 `NavigateToPartRequested`（drawing）或 `NavigateToPoRequested`（po/job）。
- 修改 `MainWindow.xaml.cs`：`ShowSearch()` 订阅新事件；新增 `OnSearchNavigateToPo`（显示 PO 详情）和 `OnPoDetailBackToSearch`（返回搜索）处理器。

## 4. Manufacturing Schedule 圆环 Loading 动画

将 Loading overlay 中的线性 ProgressBar 替换为旋转圆环动画，并修复动画不启动的问题。

- 修改 `ManufacturingScheduleControl.xaml`：用 48×48 `Canvas`（含 270° 圆弧 `Path` + `RotateTransform x:Name="SpinnerRotate"`）替换 `ProgressBar`。
- 修复触发器位置：将 `EventTrigger RoutedEvent="FrameworkElement.Loaded"` 从 `Canvas.Triggers` 移至 `UserControl.Triggers`，避免父级 `Collapsed` 导致 Loaded 事件不触发；`Storyboard` 在 UserControl 加载时立即启动并持续旋转（RepeatBehavior=Forever，周期 0.9s）。
