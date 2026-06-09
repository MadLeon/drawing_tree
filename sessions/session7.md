# Session 7: 添加图纸查看页面

## 任务描述

本session主要任务: 添加图纸查看页面

- 在主界面添加一个新的按钮 "View Drawings", 点击后进入图纸查看页面
- 图纸查看页面包含一个工具栏和一个主体区域
    - 工具栏与构造页面类似, 包含
        - 一个返回主页按钮 (house 图标), 点击后返回主界面
        - 一个打印按钮 (print 图标), 点击后可以打印当前显示的图纸
    - 主体区域包括:
        - 一个图纸树视图位于左侧, 显示一个顶层图纸和其下方的所有子图纸, 用户点击树视图中的图纸, 则图纸的pdf文件的内容会显示在图纸显示区域中
            - 该试图应该与构造树界面的构造区结构相同, 不同在于图纸树视图是只读的, 用户不能在其中进行拖拽操作
            - 该视图的元素如果存在子节点, 则应该显示一个可以展开收起的图标, 用户点击后可以展开或收起该节点的子节点
                - 使用 add_box 图标表示已收起, 使用 indeterminate_check_box 图标表示已展开
        - 一个图纸显示区域位于中间, 用于显示当前选中图纸的PDF文件, 用户可以缩放(默认为鼠标滑轮)/拖拽移动来查看图纸的不同部分
        - 一个图纸信息区域位于右侧, 与构造树界面的图纸信息区相同
- 当前开发阶段, 图纸数据来源于构造树界面构建的图纸关系, 可以使用 RT79-87630-PN-R005_tree.json 中的 RT-87630-71254-1000-1-GA-D 作为顶层节点
    - 后续将使用数据库中的数据来构建图纸树视图, 注意保留接口

---

- 返回主页按钮和打印按钮应该右对齐, 左侧只不保留订单号的展示
- 返回主页按钮颠倒显示, 不要改变其角度
- 左侧树试图界面的宽度增多一点
- 树视图界面, 节点如果左侧没有展开/收起图标, 则横线应该直通节点左侧, 而不是在节点左侧留出一个小空白
- PDF view 界面, 按住鼠标左键无法拖动 PDF 试图
- PDF view 界面, 鼠标滚轮缩放时, 能感到明显延迟, 首先该界面会没有任何显示, 然后才会显示 PDF 的内容, 需要优化性能, 使得用户能够流畅地缩放 PDF 文件
	- 给出此修改的建议, 我需要流畅的缩放体验

---

## 一句话总结

新增图纸查看页面（DrawingViewerControl），含工具栏（返回/打印）、可展开/收起的只读图纸树、内嵌 PDF 渲染区（滚轮缩放 + 拖拽平移）和只读信息面板，数据当前从 `*_tree.json` 加载，预留数据库接口。

---

## 理解与推断

- **主界面按钮**：在 `MainWindow.xaml` 工具栏添加 "View Drawings" 按钮；点击后扫描 app 目录下 `*_tree.json` 文件，通过类似 `PoSelectionDialog` 的对话框选择要查看的树，进入 `DrawingViewerControl`；若无文件则提示用户先构建树
- **新建 `DrawingViewerControl`**：顶部工具栏（house 返回、print 打印）+ 三栏主体（左只读树 | 中 PDF 区 | 右信息面板），整体与 `TreeBuilderControl` 布局一致
- **只读树视图（左侧）**：
  - 复用 `DrawingNode` 模型和相同的连接线样式（HierarchicalDataTemplate + TreeViewItem 自定义模板）
  - 无拖拽：不设 `AllowDrop`，不绑定 DragOver/Drop/PreviewMouseMove 事件
  - 有子节点时在节点左侧显示展开/收起图标：`IsExpanded=False` 显示 add_box（绿），`IsExpanded=True` 显示 indeterminate_check_box（红），通过 DataTrigger 切换，`HasChildren=False` 时图标不可见
  - TreeViewItem 模板中 `ChildrenArea.Visibility` 绑定到 `IsExpanded`（替代原来绑定 `HasChildren`）
  - 点击节点本身（非图标区）→ 选中该节点，加载对应 PDF，更新右侧信息面板
- **PDF 显示区（中间）**：
  - 使用 WinRT API `Windows.Data.Pdf.PdfDocument` 将各页渲染为 `BitmapImage`，垂直叠放在 `StackPanel` 上，外层包裹 `ScrollViewer`
  - 鼠标滚轮（`PreviewMouseWheel`）：对内容应用 `ScaleTransform` 缩放，Ctrl 无关
  - 鼠标左键拖拽：通过 `ScrollViewer.ScrollToHorizontalOffset/VerticalOffset` 平移
  - 切换图纸时异步重新渲染所有页面
  - 需将 csproj TFM 从 `net10.0-windows` 改为 `net10.0-windows10.0.19041.0` 以访问 WinRT API
- **右侧信息面板**：字段与 `TreeBuilderControl` 的 InfoPanel 完全相同，所有控件强制只读，无 TextChanged/Checked 事件
- **数据加载接口**：`LoadFromTreeNodes(IList<DrawingNode>)` 和 `LoadFromJsonFile(string)` 两个入口，当前从 JSON 加载，后续数据库实现替换 `LoadFromTreeNodes` 即可
- **打印**：点击 print 按钮，用 `Process.Start(UseShellExecute=true)` 打开当前 PDF，触发系统默认打印对话框

---

## TODO 步骤

- [x] 1. 更新 csproj TFM 为 `net10.0-windows10.0.19041.0`
- [x] 2. 在 `MainWindow.xaml` 工具栏添加 "View Drawings" 按钮；在 `MainWindow.xaml.cs` 添加 `ShowDrawingViewer()` 方法和返回事件处理
- [x] 3. 新建 `Controls/DrawingViewerControl.xaml`（工具栏 + 三栏布局，只读树视图 + PDF 区占位 + 信息面板）
- [x] 4. 新建 `Controls/DrawingViewerControl.xaml.cs`（数据加载、树节点点击、展开/收起、右侧信息面板）
- [x] 5. 实现 PDF 渲染区（Windows.Data.Pdf 渲染页面、滚轮缩放、拖拽平移）
- [x] 6. 实现打印功能（print 按钮调用系统打印）

---

## Session 内容总结

- 新建 `DrawingViewerControl`（XAML + CS），实现图纸查看页面的完整功能
- 主界面添加 "View Drawings" 按钮，扫描 `*_tree.json` 文件并通过选择对话框进入查看器
- 只读树视图：复用 `DrawingNode` 模型和连接线样式，支持展开/收起（add_box / indeterminate_check_box 图标）
- PDF 渲染：使用 `Windows.Data.Pdf` WinRT API 渲染页面为 `BitmapImage`，LayoutTransform 缩放实现即时 zoom
- 修复工具栏布局（按钮右对齐）、house 图标倒置、叶节点横线空白、拖拽无响应等 UI 问题
- 更新 TFM 为 `net10.0-windows10.0.19041.0` 以访问 WinRT API，同时配置输出路径维持原目录 `net10.0-windows\`

---

## 操作及决策细节

- **TFM 更新**：从 `net10.0-windows` 改为 `net10.0-windows10.0.19041.0`，以访问 `Windows.Data.Pdf` WinRT API；为避免输出目录改变，在 csproj 中增加 `AppendTargetFrameworkToOutputPath=false` 和 `OutputPath=bin\$(Configuration)\net10.0-windows\`

- **数据加载接口设计**：提供 `LoadFromJsonFile(string)` 和 `LoadFromTreeNodes(IList<DrawingNode>)` 两个入口；当前从 JSON 加载，后续数据库实现只需替换 `LoadFromTreeNodes` 调用方，不改控件内部逻辑

- **house 图标倒置问题**：`IconPath` style 的 `ScaleY=-1` 对对称图标（AddCircle、DoNotDisturbOn）无视觉影响，但对 house 这类非对称图标会翻转。WPF 的 `Stretch=Uniform` 已正确将 Material Icons 的负 Y 坐标映射到正值区间，因此 house / print 图标无需额外翻转，去掉 `IconPath` style 改为直接 `Stretch="Uniform"` 即可

- **叶节点横线空白**：`ExpandBtn` 默认 `Visibility="Hidden"` 仍占 20px 宽度，导致 HLine 与 NodeBorder 之间有 20px 空隙；改为 `Visibility="Collapsed"` 后叶节点不占位，横线直通节点左侧

- **PDF 缩放延迟优化**：原方案每次缩放调用 `Windows.Data.Pdf` 重新渲染（异步 IO + CPU 解码），延迟明显且有空白闪烁；新方案仅在加载时以 `BaseRenderScale=1.5` 渲染一次，缩放通过修改 `StackPanel.LayoutTransform` 的 `ScaleTransform.ScaleX/Y` 实现，由 GPU 完成矩阵变换，响应即时无延迟；代价是极高倍率放大后图像略模糊，可接受

- **PDF 拖拽失效**：原用 `MouseLeftButtonDown/Move/Up` + `CaptureMouse()`，Image 子元素可能拦截事件导致 ScrollViewer 的 handler 未能稳定触发；改为 `PreviewMouseLeftButtonDown/Move/Up`（tunnel 方向优先于子元素），在 `PreviewMouseMove` 中检查 `e.LeftButton == Pressed` 并手动调用 `ScrollToOffset`，不依赖 CaptureMouse，更可靠
