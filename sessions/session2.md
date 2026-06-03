# Session 2: 添加图纸提取功能

## 任务描述

本session主要任务: 添加图纸提取功能
- 每个项目的图纸PDF文件储存在内网的一个文件夹中
- 需要实现图纸PDF文件名的提取功能
- 目前已有一个类似的vba模块, 可以参考其代码实现 [vba 代码](src/references/*.bas)
- 用例: 
	- 用户点击 "导入图纸" 按钮后, 系统会弹出一个文件夹选择对话框, 用户选择包含图纸PDF文件的文件夹后点击确认
	- 系统会扫描该文件夹中的所有PDF文件, 提取它们的文件名并显示在主显示区域
	- 提取出的图纸信息以可编辑的纯文本形式显示在主显示区域, 用户可以在此界面内修改/添加/删除图纸信息, 确认后进入图纸关系构建界面
	- 主显示区域还应该包含
		- "确认" 按钮, 用户点击后表示图纸信息确认无误, 可以进入下一步的图纸关系构建界面
		- "返回" 按钮, 用户点击后表示放弃当前图纸信息的修改/添加/删除操作, 返回到主界面
		- "添加图纸" 按钮, 用户点击后可以在当前界面内添加新的图纸信息
	- 图纸信息的提取和显示要尽可能准确和清晰, 以便用户能够快速理解和操作
		- 每个图纸信息占一行, 每行的内容为
		- 一个"-"按钮, 用户点击后可以删除该行的图纸信息
			- 点击后弹出一个确认对话框, 询问用户是否确认删除该图纸信息, 确认后删除该行
		- 图纸号, 从文件名中提取的图纸号
		- 图纸路径, 图纸PDF文件的完整路径 (提取时记录下来, 后续预览图纸时需要用到)
		- 图纸路径后应该包含一个按钮
			- 点击该按钮后, 弹出一个文件选择对话框, 让用户选择一个图纸PDF文件, 选择后将该文件的完整路径覆盖到图纸路径中, 实现修改操作
		- 图纸号应该呈现可编辑状态, 用户可以根据需要修改图纸号信息
		- 点击"添加图纸"按钮后, 在主显示区域所有行的最下方添加一个新的空白行, 用户可以在该行输入新的图纸号和图纸路径, 图纸路径最后包含一个按钮
			- 点击该按钮后, 弹出一个文件选择对话框, 让用户选择一个图纸PDF文件, 选择后将该文件的完整路径填入图纸路径中
	- 用户点击确认后, 目前先将所有图纸信息以log的形式输出到log文件中, 后续会根据需求进行设计和添加功能

1. 打开select folder对话框后, 移除提示: select folder containing PDF drawings;
2. 每一个信息行的图纸路径修改为文件名, 但是应该记录完整路径便于后续调用
3. 主显示区域的3格按钮应该右对齐, 并且在第一行添加列标题, 标题行按钮的列不需要标题
4. 在移除按钮后面添加一个序号, 格式为"1."
5. 在首次添加所有图纸后和修改任意图纸号和修改文件路径后, 应该添加检查, 如果存在两个相同的图纸号或两个相同的文件路径, 则应该弹出提示, 并且在相关行添加红色细边框直到问题消失
	- 首次添加触发对所有记录的查重
	- 图纸号和路径仅触发对这个图纸的查重
   或者可以通过哈希表等储存结构使逻辑简单
6. 点击return后, 如果显示区有内容, 询问用户是否继续, 在确认后进行
7. 点击confirm后, 应该检查相同文件名和图纸号的问题, 如果问题存在则提示用户, 并拒绝确认
8. add drawing后, 应该在主显示区域三个按钮的左侧(左对齐)简单显示运行情况, 即多少个结果被添加到显示区
9. 修改select file这个按钮的文本为Browse

使序号列右对齐, 规定文件名列的宽度为400, 而不是占据剩余所有空间; 让每行内容相对于主显示区域总体居中

---

## 一句话总结

为图纸管理软件添加图纸PDF文件提取功能，包括文件夹扫描、图纸信息编辑界面和数据确认流程。

---

## 理解与推断

- **核心功能**: 实现图纸PDF文件名提取功能，允许用户选择文件夹、扫描PDF文件、编辑图纸信息、确认后保存
- **参考代码**: 可参考 `src/references/mod_scanning_script.bas` 中的VBA实现逻辑
  - `ExtractDrawingNumber` 函数提取图纸号（提取空格前的文本）
  - `ScanNetworkPath` 函数扫描文件夹中的PDF文件
  - 实现了去重和排序功能
- **用户交互流程**:
  1. 用户点击"导入图纸"按钮
  2. 弹出文件夹选择对话框
  3. 系统扫描文件夹内所有PDF文件并提取图纸号
  4. 在主显示区域显示可编辑的图纸信息列表
  5. 用户可以修改/添加/删除图纸信息
  6. 点击"确认"按钮后，将信息输出到日志文件
- **UI界面要求**:
  - **主显示区域上方**包含三个按钮：
	- "添加图纸"按钮：添加新的空白行
	- "确认"按钮：确认信息无误，输出到日志
	- "返回"按钮：放弃修改，返回主界面
  - 主显示区域包含图纸信息列表，每行显示：
	- "-"按钮（删除该行，需确认对话框）
	- 图纸号（可编辑文本框）
	- 图纸路径（显示完整路径）
	- "选择文件"按钮（修改图纸路径）
- **技术实现**:
  - 使用WPF的FolderBrowserDialog或自定义对话框选择文件夹
  - 使用C#的`Directory.GetFiles`扫描PDF文件
  - 图纸号提取逻辑参考VBA代码（提取空格前的文本，转为大写）
  - 使用WPF的ItemsControl或ListBox显示可编辑列表
  - 每行使用自定义UserControl或DataTemplate实现
  - 图纸信息临时存储在ObservableCollection中
- **数据结构**:
  - 需要创建DrawingInfo类，包含DrawingNumber和PdfPath属性
  - 使用ObservableCollection<DrawingInfo>进行数据绑定
- **日志输出**: 确认后将所有图纸信息输出到Logger（后续会扩展为进入关系构建界面）

---

## TODO步骤

- [x] 1. 创建DrawingInfo数据模型类
- [x] 2. 创建图纸提取服务类DrawingExtractor
- [x] 3. 创建图纸编辑界面用户控件DrawingEditorControl
- [x] 4. 实现MainWindow中的Import Drawing按钮功能
- [x] 5. 实现DrawingEditorControl的交互功能
- [x] 6. 测试完整流程
- [x] 7. 实现所有UI改进需求（9项）
- [x] 8. 添加Purchase Order输入功能
- [x] 9. 实现JSON文件导出功能
- [x] 10. 优化确认流程（停留在当前页面）

---

## Session 内容总结

- 成功实现了图纸PDF文件提取功能，包含完整的文件夹扫描、图纸号提取和编辑界面
- 创建了DrawingInfo数据模型，支持图纸号、完整路径、文件名显示和重复检测标记
- 实现了DrawingExtractor服务类，包含图纸号提取、文件夹扫描和去重功能
  - 参考VBA代码实现了ExtractDrawingNumber方法，提取空格前文本作为图纸号
  - 处理了"rt-"文件名过滤和版本号去除逻辑
  - 使用HashSet实现了高效的去重机制
- 创建了DrawingEditorControl用户控件，提供完整的图纸信息编辑界面
  - 支持图纸信息的添加、删除、修改操作
  - 实现了实时重复检测和视觉反馈
  - 提供了状态显示和用户确认机制
  - 添加了Purchase Order输入框，作为必填字段
  - 实现了JSON格式数据导出功能
- 实现了MainWindow与DrawingEditorControl的集成
  - 使用WinForms的FolderBrowserDialog进行文件夹选择
  - 动态切换主显示区域内容
  - 处理了WPF和WinForms混用的命名空间冲突
- 完成了所有UI改进需求（9项）：
  1. ✅ 移除了文件夹选择对话框的提示文本
  2. ✅ 图纸路径显示为文件名，完整路径通过tooltip查看
  3. ✅ 控制按钮右对齐，添加了列标题行
  4. ✅ 在删除按钮后添加了序号列（格式：1.，右对齐）
  5. ✅ 实现了重复检查机制，使用红色边框标记重复项
  6. ✅ Return按钮添加了确认对话框（有内容时）
  7. ✅ Confirm按钮添加了重复检查，拒绝确认有重复的数据
  8. ✅ 添加了状态显示区域，显示当前图纸数量
  9. ✅ 将"Select File"按钮文本改为"Browse"
- 完成了最终布局优化：
  - 序号列右对齐显示
  - 文件名列固定宽度400px
  - 整个表格在主显示区域居中显示
- 实现了Purchase Order和JSON导出功能：
  - Purchase Order输入框位于控制按钮区域最左侧
  - Confirm前验证Purchase Order是否填写
  - 数据以JSON格式导出到程序目录
  - 文件名自动转换为大写
  - Confirm成功后停留在当前页面，不返回主界面

---

## 操作及决策细节

**数据模型设计**:
- DrawingInfo类实现了INotifyPropertyChanged接口，支持MVVM模式的数据绑定
- 添加了FileName只读属性，从PdfPath自动提取文件名
- 添加了HasDuplicate标志和BorderBrush属性，用于UI重复标记反馈
- 使用System.Windows.Media.Brush明确命名空间，避免与System.Drawing冲突

**服务类实现**:
- DrawingExtractor参考VBA代码实现了图纸号提取逻辑
- ExtractDrawingNumber方法：提取空格前的文本，过滤"rt-"，处理下划线版本号格式
- ScanFolder方法：使用Directory.GetFiles扫描PDF文件，使用HashSet去重
- 实现了IsRevisionFormat辅助方法识别版本号格式（Rev1, rev.2等）

**UI控件实现**:
- DrawingEditorControl使用ObservableCollection<DrawingInfo>作为数据源
- 实现了IndexText_Loaded事件动态设置每行序号
- 使用Dictionary进行O(n)时间复杂度的重复检测
- CheckAllDuplicates方法在加载、修改图纸号、修改路径、删除后触发
- 实现了UpdateStatusDisplay方法更新左侧状态文本

**重复检测机制**:
- 使用Dictionary<string, int>统计图纸号和路径出现次数
- 同时检查图纸号和文件路径的重复性
- 重复项通过BorderBrush属性绑定显示红色边框
- Confirm操作前调用HasDuplicates()检查，存在重复则拒绝确认

**布局设计**:
- 使用Grid布局实现精确的列宽控制
- 列宽分配：40(删除) + 60(序号) + 250(图纸号) + 400(文件名) + 100(按钮) = 850px
- 列标题和数据行Grid设置HorizontalAlignment="Center"实现整体居中
- ItemsControl设置HorizontalAlignment="Center"确保列表居中显示
- 序号列使用HorizontalAlignment="Right"实现右对齐

**命名空间处理**:
- 解决了WPF和WinForms混用时的命名空间冲突
- 在csproj中添加了UseWindowsForms属性启用WinForms支持
- 明确指定System.Windows.Controls.Button、System.Windows.MessageBox等类型
- 使用System.Windows.Forms.FolderBrowserDialog进行文件夹选择

**用户体验优化**:
- 添加了多个确认对话框防止误操作（删除、返回）
- 重复项用红色边框高亮显示，直观明确
- 文件名显示简洁，完整路径通过tooltip查看
- 状态栏实时显示图纸数量
- Purchase Order作为必填字段，Confirm前验证
- 成功提示显示完整的JSON文件保存路径
- Confirm成功后停留在当前页面，允许继续编辑
- 所有图纸信息同时输出到日志文件和JSON文件

**JSON导出功能**:
- 使用System.Text.Json进行序列化
- 导出格式包含：PurchaseOrder、ExportDate、TotalDrawings、Drawings数组
- 文件名使用Purchase Order值，自动转换为大写
- 保存到程序实际目录（AppDomain.CurrentDomain.BaseDirectory）
- JSON格式化输出，易于阅读和解析
- 每个Drawing包含：DrawingNumber、PdfPath、FileName

**确认流程优化**:
- Confirm按钮执行三重验证：
  1. Purchase Order是否填写
  2. 是否存在重复的图纸号或路径
  3. JSON文件是否成功导出
- 验证失败时给出明确的错误提示
- 成功后停留在当前界面，不返回主页
- 用户可以查看结果、继续编辑或点击Return返回

**技术难点解决**:
- 解决了WPF ItemsControl中动态显示序号的问题（使用Loaded事件）
- 处理了System.Windows.Media.Brush和System.Drawing.Brush的命名冲突
- 解决了编译时进程占用文件的问题（需要先关闭运行的应用）
- 实现了数据绑定与实时验证的结合

---

