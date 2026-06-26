# Project Session Summary

## Last Updated
2026-06-26 | 完善 MP 文件创建流程（客户配置、禁止覆盖、DB 记录）并抽象加载动画为可复用控件

## Current Status
- `MpConfig` 管理 config.txt 的 `[CustomerFolderMappings]` 区域；创建 MP 前自动同步 DB 客户列表，缺失映射时提示用户填写并终止
- MP 文件创建禁止覆盖同名文件；成功后写入 `part_attachment`（file_type='MP'）；文件列表从 DB 查询而非文件系统扫描；打开时文件缺失询问是否删除 DB 条目
- `LoadingOverlayControl` 统一旋转圆圈遮罩；MP Schedule 和 TreeBuilder 均已接入；静态 frozen brush 优化消除渲染期间动画冻结问题
- All POs 界面支持 OE 视图与简洁视图切换；Import Drawing → Edit Parts → Build Tree 三步工作流完整
- MP Schedule 子节点甘特图条形图支持点击 StepAssignmentDialog

## Recent Sessions

2026-06-26 - 完善 MP 文件创建流程并抽象加载动画为可复用控件
- `MpConfig.cs` 新增 `[CustomerFolderMappings]` 区域读写；`GetFolderName` 查映射，`SyncCustomerKeys` 将 DB 客户同步到 config 并返回未配置列表；定义 `MpFolderNotConfiguredException`
- `MpContext` record 新增 `PartId`/`OrderItemId`；`MpFileService.CreateAndOpen` 改为配置驱动路径、禁止覆盖（文件已存在抛异常）、成功后调用 `PartRepository.AddMpAttachment`
- `PartRepository` 新增 `GetMpAttachments`/`AddMpAttachment`/`RemoveMpAttachment` 和 `MpAttachmentRow` record；MP 文件列表从文件系统扫描切换为 DB 查询
- `PartDetailControl` MP 打开按钮：文件不存在时弹窗询问是否删除 DB 条目，确认后调用 `RemoveMpAttachment` 并刷新
- MP Schedule 加载动画冻结修复：`DrawRowBands`/`DrawStepBar`/`DrawDueDateLines` 改用静态 frozen brush + shop brush 缓存；`LoadDataAsync` 遮罩折叠移至 `DispatcherPriority.Loaded` 保证 canvas 渲染完毕后再消失
- 新建 `LoadingOverlayControl`（旋转弧形 + Storyboard 自包含）；`ManufacturingScheduleControl` 和 `TreeBuilderControl` 均改用此控件；CLAUDE.md 新增加载动画使用说明

2026-06-26 - 实现 Issue #2：PartDetailControl 新增 Manufacturing Process 区域
- `PoRepository` 新增 `GetMpContext(orderItemId)` 方法，JOIN order_item→job→purchase_order→customer→part 返回 MP 所需全部字段；新增 `MpContext` record
- 新建 `Services/MpFileService.cs`：路径生成（`BuildTargetFolder`/`BuildFileName`）、已有文件扫描（`GetExistingFiles`）、模板复制+COM填单元格+打开（`CreateAndOpen`）
- `DrawingTree.csproj` 新增 Content 引用，将 `reference/mp_template/Manufacturing Process with Tool.xlsm` 复制到构建输出 `Resources/mp_template.xlsm`
- `PartDetailControl.xaml` 在 "Drawing PDF" 之后插入 "Manufacturing Process" Border 卡片（含 `NewMpButton` 和 `MpFilesPanel`）
- `PartDetailControl.xaml.cs` 新增 `LoadMpSection()`、`NewMpButton_Click`、`BuildMpFileRow` 等方法；COM Interop 通过 `dynamic` 无需额外 NuGet 包

2026-06-25 - 优化 MP Schedule 工具栏样式并修复搜索框输入延迟
- 添加 FilterListGeo 路径资源，Filter 按钮改用 filter_list 图标 + 文字，高度 24px
- 搜索框宽度从 240 扩至 360，高度增至 28，Placeholder 改为"Job / Drawing / P.O."
- 返回按钮缩小至 28×28，内部图标 14×14
- Day/Week/Month 单选框加 `VerticalContentAlignment="Center"` 确保垂直居中
- 搜索框 TextChanged 改用 300ms DispatcherTimer debounce，消除第一次输入的严重延迟
- 搜索字段移除 Description，改为匹配 `vm.Row.PoNumber`

2026-06-25 - 修复 MP Schedule 展开按钮尺寸、子节点最新数据查询及子节点甘特图点击响应
- XAML 中展开按钮从 20×20 缩小为 18×18，内部图标从 14×14 缩小为 12×12
- `ExpandToggle_Click` 将 `Render()` 推迟到 `DispatcherPriority.Background`，让左面板行更新先完成后再重绘甘特画布
- `GetAllScheduleChildItems` SQL 改为 JOIN 最新 revision（`UPPER(drawing_number) ORDER BY revision DESC LIMIT 1`），`ChildPartId` 指向最新 part 记录
- `GanttCanvas_MouseLeftButtonDown` 重构：新增 `IsChild` 分支，用 `ChildPartId`/`ParentOiId` 查模板、保存步骤、刷新 `_childStepMap`

2026-06-25 - 实现 MP Schedule 展开功能并修复 WPF 资源前向引用崩溃
- `ScheduleRepository` 新增 `GetChildStepTrackers(orderItemIds, childPartIds)`：JOIN step_tracker 与 process_template，返回 `(OiId, ChildPartId) → List<StepTracker>` 字典
- `ScheduleDisplayRow` 新增 `Steps` 属性；`ManufacturingScheduleControl` 新增 `_childStepMap` 字段，`PrefetchChildrenAsync` 在单一 Task.Run 中批量加载 BOM 和子步骤数据
- `BuildDisplayRows` 子行改用真实步骤计算 StatusText（`{completed}/{total}`），`Steps` 属性传入子行
- `DrawBars` 重构为 `DrawStepBar` 辅助方法，父子行均可渲染 Gantt 条形图
- 修复崩溃（`XamlParseException: Cannot find resource 'LeftRowBorder'`）：将四个样式定义从 DataTemplate 之后移至之前

## Key Decisions
- WPF `UserControl.Resources` 内 DataTemplate 引用的 `StaticResource` 样式必须定义在 DataTemplate **之前**；若样式在 DataTemplate 之后，WPF 在 Dispatcher layout pass 应用模板时找不到资源，抛出 `XamlParseException`（crash）
- Import Drawing / Edit Parts / Build Tree 的入口统一改到 All POs 界面的 PO 三点菜单"Input Data"，三个独立工具栏按钮已移除；工作流通过事件链自动跳转，无需手动切换
- ContextMenu 的 MenuItem 在 WPF 中不继承父 Button 的 DataContext；统一从 `ContextMenu.PlacementTarget`（即三点 Button）的 Tag 取数据
- drawing_number 大小写问题修复顺序：先清理数据再改 Schema，因为 NOCASE 约束会拒绝已有重复数据的导入
- revision 占位版本（rev="-"）与真实 revision 并存时，加载优先选最高 revision（ORDER BY revision DESC）；保存时重定向 order_item 而非批量删除占位版本
- MP Schedule 展开延迟是架构限制：AllPo 用嵌套 hidden 元素（bool 翻转即可），MP Schedule 用平铺列表 + canvas，子行插入必须重建列表并重绘整个画布，无法用 WPF binding 直接优化
- WPF `DoubleAnimation` 在 `RotateTransform.Angle` 上是 UI 线程依赖动画（非合成线程独立），Canvas 大量 `Children.Add` 会冻结动画；解决方案：静态 frozen brush + 遮罩在 `DispatcherPriority.Loaded` 后折叠
