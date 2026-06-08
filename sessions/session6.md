# Session 6: 修复构造界面滚动与多选拖拽

## 任务描述

本 session 主要任务：修复构造界面
- 鼠标移动到构造区的上方滚动滑轮应该可以使构造区滚动
- 左侧的图纸多选后，拖动到构造区中应该可以同时对这些图纸进行构造操作，他们的目标位置应该一致

---

## 一句话总结

修复构造区鼠标滚轮无响应，以及左侧多选后拖拽仅移动单个图纸的两个缺陷。

---

## 理解与推断

- **构造区滚动**：XAML 中 TreeView 已设置 `ScrollViewer.VerticalScrollBarVisibility="Disabled"`，外层有 ScrollViewer，但 WPF 的 TreeView 路由事件机制仍会拦截 `MouseWheel` 事件，阻止外层 ScrollViewer 响应。修复方案：给 TreeView 绑定 `PreviewMouseWheel` 事件，在处理器中找到外层 ScrollViewer 并手动触发滚动
- **多选拖拽**：当前 `LeftPanel_PreviewMouseMove` 中 `DragDrop.DoDragDrop` 只传递鼠标下方的单个 `DrawingInfo`（`DragFormatInfo`），即使已选中多张图纸也不会一起传递。修复方案：新增 `DragFormatInfoList` 格式，拖拽时若被拖项在 `_selectedDrawings` 中且数量 > 1，则传递整个列表；`TreeView_Drop` 和 `TreeView_DragOver` 中处理列表格式；拖拽预览显示图纸数量（如 "3 drawings"）

---

## TODO 步骤

- [x] 1. 修复构造区滚动：给 TreeView 绑定 `PreviewMouseWheel`，手动触发外层 ScrollViewer 滚动
- [x] 2. 修复多选拖拽：新增 `DragFormatInfoList`，`LeftPanel_PreviewMouseMove` 传递全部选中列表，`TreeView_DragOver` / `TreeView_Drop` 处理列表格式，拖拽预览显示图纸数量

---

## Session 内容总结

- 修复构造区鼠标滚轮无响应：给外层 ScrollViewer 命名并通过 `PreviewMouseWheel` 事件手动转发滚动
- 修复多选拖拽：多选图纸拖入构造区时，所有选中图纸均被添加为同一目标节点的子节点

---

## 操作及决策细节

- **构造区滚动**：
  - WPF TreeView 的路由事件机制会拦截 `MouseWheel`，即使内部 ScrollViewer 已设为 `Disabled` 仍无法冒泡到外层
  - 方案：给外层 `ScrollViewer` 添加 `x:Name="TreeScrollViewer"`，在 `TreeView_PreviewMouseWheel` 中调用 `TreeScrollViewer.ScrollToVerticalOffset(offset - e.Delta)` 并置 `e.Handled = true`

- **多选拖拽**：
  - 新增常量 `DragFormatInfoList = "DrawingTree.DrawingInfoList"`
  - `LeftPanel_PreviewMouseMove`：判断 `_selectedDrawings.Count > 1 && _selectedDrawings.Contains(info)` 时构造 `List<DrawingInfo>` 并以 `DragFormatInfoList` 放入 `DataObject`；拖拽气泡显示 "N drawings"
  - `TreeView_DragOver`：接受条件加入 `DragFormatInfoList`
  - `TreeView_Drop`：优先处理 `DragFormatInfoList`，遍历列表对每张图纸创建 `DrawingNode` 并添加到目标节点，再统一调用 `SortCollection`
