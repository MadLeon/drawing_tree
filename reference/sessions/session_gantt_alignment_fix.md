# Gantt Alignment Fix

## 上一会话（前置背景）

修复 MP Schedule 搜索过滤后 Gantt 图行带与左侧行错位的 bug（条纹跑到屏幕底部）。

- 将右侧 Gantt 面板从 `GanttHScroll`（ScrollViewer）改为 `GanttClip`（Border + `ClipToBounds="True"`），消除嵌套 ScrollViewer 的垂直对齐冲突
- 给 `ContentGrid` 和 `GanttCanvas` 添加 `VerticalAlignment="Top"`，使容器高度收缩到行内容高度而不是拉伸到 ScrollViewer 高度
- 新增 `_ganttOffset` 字段和 `SetGanttOffset()` 方法，用 `RenderTransform`（TranslateTransform）实现水平平移，替换原来的 `ScrollToHorizontalOffset` 调用
- 在 `ApplySortAndFilter()` 和 `Render()` 中加入诊断日志，记录 OuterScroll offset、GanttClip/Canvas/LeftRows 的实际位置和尺寸

## 本次会话

修复前一会话遗留的 `Point` 类型歧义编译错误，使项目成功 build。

- 诊断日志代码中 `new Point(0, 0)` 与 `System.Drawing.Point` / `System.Windows.Point` 发生歧义，导致 3 处 CS0104 编译错误
- 在文件顶部 using alias 列表中添加 `using Point = System.Windows.Point;` 解决歧义
- Debug build 成功，剩余 CS4014 warning（`_ = LoadDataAsync()` fire-and-forget）可忽略
