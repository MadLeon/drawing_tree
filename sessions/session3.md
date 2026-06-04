# Session 3: 添加图纸关系构建功能

## 任务描述

本session主要任务: 添加图纸关系构建功能

用例: 
1. 用户点击 build drawing tree 后, 
    - 先判断当前程序目录下是否包含"{PO}_import.json"文件, 如果没有, 则弹出一个提示框提示用户先使用导入图纸功能生成图纸文件
    - 若.json文件存在, 弹出一个对话框, 包含一个提示, 一个下拉菜单和一个确认按钮    
    - 下拉菜单的选项取值为当前程序目录下的所有.json文件名, 该文件由导入图纸功能生成
    - 用户选择一个PO号后点击确认, 进入图纸关系构建界面

2. 图纸关系构建界面包含一个上边栏, 类似导入图纸界面, 包含以下元素:
    - 一个文本提示, 显示当前选择的PO号, 左对齐
    - 按钮组, 包含以下按钮, 右对齐:
        - "保存"按钮: 点击后将当前构建的图纸关系保存到一个新的.json文件中, 文件名为"{PO}_tree.json", 该文件包含图纸之间的层级关系信息
        - "返回"按钮: 点击后停留在当前界面

3. 图纸关系构建界面分为左右两个部分, 左侧为图纸名列表, 右侧为关系构建区域
    - 用户可以通过拖拽的方式将左侧图纸列表中的图纸添加到右侧关系构建区域
    - 图纸列表的每个图纸项都包含图纸号, 单击该图纸号会打开该图纸的PDF文件, 拖拽则可以将其移动到右侧的关系构建区域
    - 右侧区域呈现文件夹树结构, 用户第一次拖拽的图纸成为根节点
    - 右侧的图纸元素也能被单击以打开PDF, 被拖拽以调整树中位置
    - 右侧的图纸元素包含+/-按钮（仅有子节点时显示）
    - 拖拽时目标元素显示高亮下边框提示
    - 将元素A放置在元素B上时, A成为B的子节点

### 第一次迭代

需要修复的问题:
1. PO 选择界面:
	- 下拉菜单宽度减少40%, 高度减少20%
	- 整个对话框高度增加20%, 下方的按钮没有完全显示
2. 图纸关系构建界面:
	- 在最右边添加一栏, 用于显示图纸的信息, 它应该是一个表单样式, 包含以下字段:
		- Drawing Number
		- Revision
		- Description
		- Quantity In Assembly
		- Is Assembly
		- File Path
	- 其中, revision, description, quantity in assembly, is assembly 应该可以被用户编辑, 而其他字段则为只读, 用于显示当前选中图纸的基本信息
	- is assembly 字段应该是一个复选框, 用户通过勾选或取消勾选来表示该图纸是否为一个装配图纸
3. 图纸元素:
	- 构筑区的图纸元素只有图纸号文本, 应该仿照左侧的元素添加边框, 在视觉上更易区分
	- drawing/drawing tree 区域的图纸元素中, 文本的右侧应该添加一个小按钮"↗", 点击后打开对应的图纸PDF文件, 取代单击元素即打开PDF文件的功能
	- 元素边框现在应该包裹文本和图标, 而不是只包裹文本
	- 构筑区元素"-"按钮的下方应该根据具体结构显示连接线, 以更清晰地展示树结构的层级关系
4. 拖拽功能:
	- 在拖拽的过程中元素应该显示一个半透明的预览
	- 在拖拽过程中, 元素应该随着鼠标移动而移动, 其原本的位置应该显示一个空白的占位符, 以提示用户该位置原本有一个元素
	- 当一个元素被放置在另一个元素上时, 下方元素的下边框不应占据整个显示区域, 而是只占据被放置元素的宽度范围, 以更准确地提示用户放置位置
	- 在拖拽结束, 释放鼠标后, 被放置元素的同级元素应该进行排序, 以保持树结构的有序性

### 第二次迭代

- 当一个元素被拖拽到另一个元素上时, 下方的元素显示的高亮的下边框会同时增加下边框的高度, 造成元素位置的抖动, 修改为高亮整个边框, 且不增加边框的宽度
- 构造区元素选中后, drawing info 区显示信息, 但是没有高亮显示被选中的元素
- 构造区的加减号不应该在元素内部, 而应该在元素的左侧, 以避免与元素文本重叠
- 构筑区非总装图的元素左侧应该有一条水平连接线, 连接到最近的由上级元素延申的垂直连接线
- 将部分按钮修改成透明加图标的样式, 使用public/icons文件夹下的图标
	- 图纸号右边的打开图纸按钮使用 arrow_outward, 蓝色
	- 构造区图纸号元素左侧的"+"按钮使用 add_box, 黑色, 字体小一号
	- 构造区图纸号元素左侧的"-"按钮使用 indeterminate_check_box, 黑色, 字体小一号
- 当元素被拖到构造区后, 在打开图纸按钮的右边添加"-"按钮, 使用 do_not_disturb_on, 红色, 点击后可以将该元素从构造区移动到左侧图纸列表, 用户可以在左侧图纸列表中重新拖拽该元素到构造区
- drawing info 区的 Is Assembly 字段只保留标题文字和复选框, 复选框自带的文本应该被隐藏

---

## 一句话总结

为图纸管理软件添加图纸关系构建功能，包括 PO 选择对话框和带拖拽交互的树形关系构建界面。

---

## 理解与推断

- **前置条件检查**：点击"Build Drawing Tree"后，先检查程序目录是否存在 `*_import.json` 文件；若无，提示用户先导入图纸；若有，弹出 PO 选择对话框
- **PO 选择对话框**：下拉菜单列出目录下所有 `*_import.json` 文件名作为选项，用户选择后进入构建界面
- **构建界面顶栏**：与 DrawingEditorControl 风格一致，左侧显示当前 PO 号，右侧有"保存"和"返回"按钮
- **左侧图纸列表**：来自所选 JSON 文件的图纸数据；单击图纸号打开 PDF，拖拽可将图纸移至右侧树区域
- **右侧树形区域**：第一个拖入的图纸成为根节点；后续图纸拖拽到某节点上方时，显示高亮下边框；放置后成为该节点的子节点；树节点支持展开/折叠（+/- 按钮，仅有子节点时显示）；树内节点也可拖拽重排位置；单击文字打开 PDF
- **保存功能**：将树形结构导出为 `{PO}_tree.json`，包含层级关系数据
- **返回按钮**：当前界面有未保存内容时询问用户是否确认离开
- **技术选型**：新建 `TreeBuilderControl.xaml` 用户控件，复用已有 `DrawingInfo` 模型，新增 `DrawingNode` 树节点模型

---

## TODO 步骤

- [x] 1. 新增 DrawingNode 数据模型（树节点，包含子节点列表和展开状态）
- [x] 2. 新增 PO 选择对话框窗口 PoSelectionDialog.xaml
- [x] 3. 新建 TreeBuilderControl.xaml 用户控件（顶栏 + 左右分栏布局）
- [x] 4. 实现左侧图纸列表（从 JSON 加载，支持单击打开 PDF、拖拽启动）
- [x] 5. 实现右侧树形区域（节点渲染、+/- 展开折叠、拖拽放置逻辑）
- [x] 6. 实现保存功能（导出 `{PO}_tree.json`）
- [x] 7. 在 MainWindow 的 BuildDrawingTreeButton_Click 中接入完整流程
- [x] 8. 测试完整流程（编译通过，0 错误）

---

## Session 内容总结

- 新增 `DrawingNode` 树节点模型，包含 Children、IsExpanded、IsDropTarget、IsDragging、IsSelected 属性，支持 WPF 数据绑定
- 新增 `PoSelectionDialog` 弹出对话框，下拉菜单列出程序目录下所有 `*_import.json` 文件，供用户选择 PO
- 新增 `TreeBuilderControl` 用户控件，实现完整图纸关系构建界面：
  - 顶栏显示 PO 号，右对齐保存/返回按钮
  - 左侧图纸列表，从 `_import.json` 加载，支持拖拽和 ↗ 打开 PDF
  - 中间树形构建区，支持左侧→树、树内节点重排两种拖拽方式
  - 右侧 Drawing Info 信息面板，可编辑 Revision、Description、Quantity、IsAssembly 字段
- 在 `MainWindow` 的 `BuildDrawingTreeButton_Click` 中接入完整流程：检查文件存在 → 选择 PO → 显示构建界面
- 经两轮迭代修复多项 UI 问题：
  - 拖拽预览：半透明浮动标签（Popup）随鼠标移动，原位置显示占位符（Opacity 0.25）
  - 连接线：树节点展开时显示垂直连接线；子节点左侧显示 10px 水平连接线，对接父节点垂直线
  - 图标按钮：使用 Material Icons SVG 路径数据，通过 `ScaleY="-1"` 修正坐标系，实现 arrow_outward（蓝）/ add_box（黑）/ indeterminate_check_box（黑）/ do_not_disturb_on（红）图标
  - 选中高亮：点击节点后 NodeBorder 显示淡蓝背景 + 深蓝边框
  - 移除按钮（do_not_disturb_on）：将节点从树移回左侧列表
  - 放置后同级节点按图纸号字母顺序自动排序

---

## 操作及决策细节

**数据模型设计：**
- `DrawingNode` 只持有 `DrawingInfo` 引用（不复制数据），树节点移动时 DrawingInfo 保持唯一性
- `DrawingInfo` 新增 Revision、Description、QuantityInAssembly、IsAssembly、IsDragging 五个属性，全部实现 INotifyPropertyChanged
- `DrawingNode` 的 `HasChildren` 属性通过 `Children.CollectionChanged` 自动触发通知，DataTrigger 无需手动刷新

**拖拽实现：**
- 使用 WPF 原生 `DragDrop.DoDragDrop`（OS 级拖拽），搭配 `PreviewMouseMove` 检测位移阈值启动
- 区分两种拖拽格式：`"DrawingTree.DrawingInfo"`（左侧拖入）和 `"DrawingTree.DrawingNode"`（树内重排）
- `PreviewDragOver` 挂在 RootGrid（UserControl 根 Grid）上，利用 WPF 所有元素均能接收 Drag 事件的特性，在整个控件范围内实时更新 Popup 位置
- `IsAncestorOf` 检查防止将节点拖到自身后代，避免循环引用

**树形渲染方案：**
- 使用 WPF `TreeView` + `HierarchicalDataTemplate`，完全覆盖 `TreeViewItem` ControlTemplate，去除默认展开箭头
- 节点模板采用三列 Grid：Col0（10px 水平连接线）/ Col1（22px 切换按钮，在 NodeBorder 外）/ Col2（内容边框）
- ChildrenArea `Margin-left=21` 使垂直连接线对齐父节点切换按钮中心（10px hconn + 11px 半按钮宽 = 21px）
- 展开/折叠由 `TreeViewItem.IsExpanded` 的 Trigger 控制 ItemsPresenter 可见性；IsExpanded 通过 TwoWay 绑定同步到 DrawingNode

**图标方案：**
- Material Icons 使用 `viewBox="0 -960 960 960"` 坐标系（Y 轴向上），WPF Path 坐标系 Y 轴向下
- 使用 `RenderTransform: ScaleTransform(ScaleY=-1)` + `RenderTransformOrigin="0.5,0.5"` 修正翻转
- 将四个 SVG `d` 属性值定义为 `<Geometry>` 资源，Path 通过 `Data="{StaticResource ...}"` 引用
- 切换按钮两态（add_box / indeterminate_check_box）用两个叠加的 Path 实现，DataTrigger 切换 Visibility

**命名空间冲突处理：**
- 项目同时引用 WPF 和 WinForms（UseWindowsForms=true），导致 UserControl、MouseEventArgs、DragEventArgs、Point、MessageBox 等类型产生歧义
- 采用文件顶部 `using Alias = FullyQualifiedName` 方式解决，不修改项目配置

**保存格式：**
- `{PO}_tree.json` 包含 DrawingNumber、PdfPath、Revision、Description、QuantityInAssembly、IsAssembly、Children 递归结构
- 使用 `JsonSerializer` + 匿名类型递归序列化，避免引入额外依赖
