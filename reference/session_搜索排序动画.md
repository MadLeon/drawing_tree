# 搜索排序动画

## All PO 页面排序与分组
按 PO 分组展示，组间半行间距，组内按 Line Number 排序，组间按 OE 升序。
- `AllPosControl.xaml.cs`：改用 `ListCollectionView`，加 `GroupDescriptions`（按 PoNumber）和 `SortDescriptions`（OE 升序 → LineNumber 升序）
- `AllPosControl.xaml`：添加 `DataGrid.GroupStyle`，自定义 `GroupItem` 模板，移除折叠按钮并在每组顶部加 12px 间距

## Search 页面新增 PO / Job 搜索
搜索框支持图纸号、PO 号、Job 号三种搜索模式，单次查询同时覆盖。
- `DrawingRepository.cs`：将 `SearchPartsWithJobContext` 的 SQL 改为 UNION ALL 三个分支（drawing / po / job），各带 `match_source` 整数列；C# 层按 (PoNumber, JobNumber, DrawingNumber) 去重，drawing 匹配优先级最高
- `SearchResultRow` 记录新增 `PoId` 和 `MatchSource`（`SearchMatchSource` 枚举）两个字段

## Search 结果 View 按钮导航分流
点击 View 时，drawing 匹配跳到图纸查看页，PO/Job 匹配跳到单 PO 详情页。
- `SearchControl.xaml`：View 按钮 Tag 从 `{Binding PartId}` 改为 `{Binding}`（绑定整行）
- `SearchControl.xaml.cs`：新增 `NavigateToPoRequested` 事件，`ViewButton_Click` 按 `MatchSource` 分流触发不同事件
- `MainWindow.xaml.cs`：接线 `NavigateToPoRequested`，新增 `OnSearchNavigateToPo` 和 `OnPoDetailBackToSearch` 两个处理方法

## MP Schedule 圆环 Loading 动画
将线性 ProgressBar 替换为旋转圆弧动画，并修复动画不启动的问题。
- `ManufacturingScheduleControl.xaml`：用 48×48 `Canvas` + 270° 白色圆弧 `Path` + `RotateTransform` 替换 `ProgressBar`
- 修复：`EventTrigger Loaded` 从 `Canvas.Triggers`（父级 Collapsed 不触发）移至 `UserControl.Triggers`，确保动画在控件加载时立即启动
