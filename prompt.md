未来实现
- customer 按照使用情况进行排序的功能
- 写脚本, 使用DIR Log中的数据批量更新图纸状态
- 在图纸界面, 用户能够点击创建按钮使用软件 UI 编写 MP (未来实现)



---

## 已实现

---

[x] Import Drawing

- 该界面将从 All PO 进入
- Import 结束以后, 点击 Save 自动切换到 Edit Parts 界面
- Edit Parts 界面入口同样改为 All PO
- 编辑结束后点击 save all, 自动切换到 Build Drawing Tree 界面 
- 移除import drawing/edit part/build drawing tree三个按钮
- Edit Parts 界面的 Drawing Number 抓取有时会出现错误, 因为文件名并不正确
  - 将该文本框改为可编辑状态, 一旦失去焦点检查值是否发生改变
  - 如果发生改变, 则修改 json 文件中该项目的值, 然后重新load这行元素 (如果无法只重新加载这一个元素, 刷新页面)

---

[x] MP Schedule
- 为什么 MP Schedule 初始加载超级慢
- 甘特图选择步骤后, 手动将之前的所有步骤标记为完成
- 增加 PO 列宽 1/4

写可以复用的脚本, 用于手动更新数据库中 purchase_order.is_active 字段的值.

- 数据来自文件, 位置为 "\\rtdnas2\OE\Order Entry Log.xlsm"
- 使用开发数据库, 但应该很容易修改目标数据库的地址
- 该文件极度重要, 每次操作必须使用只读方式, 避免破坏该文件造成公司损失
- 该文件通常被其他用户打开, 即使用 UI 也只能选择只读方式
- 数据库中的 purchase_order.is_active 已经有值, 只需要对状态进行更新
- 该文件 AA 列记录了每个 order_item.id
- 提取该列信息并使用这些 id 更新数据库中 PO 的状态
  - 只有id对应的PO应该被认为是活跃的, 否则应标记为不活跃

---

[x] Import Drawing

- 该界面将从 All PO 进入
- Import 结束以后, 点击 Save 自动切换到 Edit Parts 界面
- Edit Parts 界面入口同样改为 All PO
- 编辑结束后点击 save all, 自动切换到 Build Drawing Tree 界面 
- 移除import drawing/edit part/build drawing tree三个按钮
- Edit Parts 界面的 Drawing Number 抓取有时会出现错误, 因为文件名并不正确
  - 将该文本框改为可编辑状态, 一旦失去焦点检查值是否发生改变
  - 如果发生改变, 则修改 json 文件中该项目的值, 然后重新load这行元素 (如果无法只重新加载这一个元素, 刷新页面)

---

[x] MP Schedule
- 为什么 MP Schedule 初始加载超级慢
- 甘特图选择步骤后, 手动将之前的所有步骤标记为完成
- 增加 PO 列宽 1/4

写可以复用的脚本, 用于手动更新数据库中 purchase_order.is_active 字段的值.

- 数据来自文件, 位置为 "\\rtdnas2\OE\Order Entry Log.xlsm"
- 使用开发数据库, 但应该很容易修改目标数据库的地址
- 该文件极度重要, 每次操作必须使用只读方式, 避免破坏该文件造成公司损失
- 该文件通常被其他用户打开, 即使用 UI 也只能选择只读方式
- 数据库中的 purchase_order.is_active 已经有值, 只需要对状态进行更新
- 该文件 AA 列记录了每个 order_item.id
- 提取该列信息并使用这些 id 更新数据库中 PO 的状态
  - 只有id对应的PO应该被认为是活跃的, 否则应标记为不活跃

---

[x] All POs 界面
- All POs 按钮 -> Order Entry

- 工具栏添加 History 按钮, 点击进入 **PO History 界面**
  - 此界面与 All PO 界面相同, 但应该显示 purchase_order.is_active=false 的条目
- 工具栏添加简洁视图按钮, 点击从OE视图切换为简洁视图模式

简洁视图模式
- 当前 OE 视图模式存在大量冗余内容, 每个区域的 PO, OE, Customer, Contact 都是重复信息
- 对于每个相同的 PO 区域, 把这些内容单列一行, 放在区域最上方, 采用 {标题: 字段内容} 的样式
  - 右对齐位置添加 Tree 按钮, 点击可以直接进入图纸树界面
  - Tree 按钮右边添加 Package Tracker 按钮, 点击进入单 PO 页
  - 区域的每行对应一个 order_item, 如果 order_item 对应的图纸号下方有子节点, 则最左侧应该有一个展开图标, 点击后
    - 会将所有子节点显示出来, 每行一个, 不需要采用树形结构, 按图纸号进行默认排序即可
    - 首次渲染时可酌情不加载下级图纸, 等点击展开时再进行加载, 你可以视情况自行决定
    - 每行内容包括以下列
      - Job Number
      - Line
      - Qty
      - Drawing Number
      - Description
      - Rls Date
      - Due Date
      - 一个 View 按钮, 点击进入图纸页面

用例:
- 用户能够对PO数据进行增删改查
  - 为每个 PO 标题行最右边添加纵向的三个点图标, 点击出现下拉菜单, 项目包括
    - Mark As Shipped
    - Import Data: 点击进入 Import Drawing 界面, 这是该功能的新入口
    - Edit Parts: 点击进入 Edit Parts 界面
  - 为每个 order 行最右边添加三点图标, 点击出现下拉菜单, 按钮包括
    - Edit Item
  - 除了点击三点图标, 在每个 order item 行点击右键, 应该也可以出现 Edit Item 选项
  - (增) 用户能够通过点击工具栏上的创建新 Job 创建新的 order_item
    - 点击按钮后, 弹出输入对话框, 输入对话框包括
      - OE: 检索 purchase_order.oe_number 中此项的最大值, 然后+1显示 (只读项)
      - Job Number: 检索 job.job_number 最大的值, 然后+1显示
        - 紧贴文本框添加一个按钮 Use Previous
        - 点击后使用 job.job_number 的最大值
      - Customer: 下拉菜单, 列出所有 customer.customer_name, 同时该文本框应该可以被编辑, 以添加新的 customer
      - Qty: order_item.quantity 高概率为数字
      - Parts: part.drawing_number
      - Rev: part.revision
      - Contact: 下拉菜单, customer 中所有 contact
      - Ln: order_item.line_number, 必须填, 大概率为数字, 从1开始, 初始为1
      - Description: part.description 可空
      - Price: part.unit_price, 大概率为 real
      - P.O.: purchase_order.po_number 为空时储存为 NPO-{job_number}
      - Del. Req'd: order_item.delivery_required_date
      - 返回按钮: 放弃输入, 返回 All PO
      - 创建新Record按钮: 点击后, 
        - 弹出对话框请用户确认输入信息
        - 确认后返回 All PO
      - 批量创建按钮: 点击后, 
        - 弹出对话框请用户确认输入信息
        - 确认后留在此对话框
        - Job No/Ln 增加 1, Customer/Contact/Del.Req'd 不变, 其他清零
    - 级联保存数据到数据库, OE 页面刷新, 
    - 如果未能正确保存, 弹出对话框并输出错误日志
    - 如录入的信息并非正确格式, 则发出warning日志
  - (删) 用户能够通过点击三点图标下拉菜单中的 Mark As Shipped 移除当前 PO
    - 点击后出现确认对话框
    - 数据库操作仅需将 purchase_order.active 设置为 false
    - 数据更新后刷新页面
  - (改) 用户能够通过点击 order 行最右边的三点图标的下拉菜单中的 Edit Order 对每个 order 进行修改
    - 点击按钮后, 弹出修改 order 对话框, 对话框与输入对话框相同
    - 确认输入后, 对数据条目进行更新操作

---

修改 New Job 对话框 --- done
- Contact 下拉菜单
  - 在 Customer 未选择时应该无法编辑
  - 每当 Customer 改变时, 获取数据库该客户下的所有联系人并填充下拉菜单
- 调整三个按钮的顺序: Create Record > Batch Create > Cancel (修改名称)

Edit Item 对话框 --- done
- 修改标题为 {PO} / {Job}
- 修改两个按钮的顺序: Save > Cancel

All PO 界面 --- done
- New Job 按钮同样右对齐
- 简洁视图不要使用中文
- 修改 History 按钮的名字为 History Orders
- All Purchase Orders 标题的右侧显示当前共有多少个活跃的 PO
- 移除 OE/Simple 视图每行最后的 view 按钮
- 简洁视图的每行内容, 只有其存在子节点才在最前方显示展开按钮, 若没有则显示空白
- 每个PO区域, 应该包含一个Header row
- 展开后的子节点, 其图纸号与描述应该与父节点对其
- 展开后的子节点, 其内容应该遵循与edit parts界面相同的显示逻辑, 
  - 确保调用revision最新的那条 & 在sql中使用降序排列, 并limit最上面的一个
- 在每个po的三点菜单中添加一个 Edit Parts 选项, 直接进入 Edit Part 的选择 json 文件对话框

零件加载讨论 --- done
All PO 页面中, 对于每个可展开的 order item, 目前的逻辑是: 点击按钮 > 数据库检索 > 展开
展开会会有明显卡顿, 我希望优化这个过程
我的想法是分线程专门加载所有子节点, 在主页面渲染后后台进行
这个方法是否可行, 是否过于复杂, 你有什么好的建议

---

开发数据库中还是存在小写版本, 举例, part.id为4796和5481应该只有一条数据 --- DONE
- 发现是 rev 不一致导致, rev="-"的数据只是占位版本
- 在单 PO 界面, 全部引用的是小写的版本
- 在 edit part 界面每行的保存按钮中添加逻辑, 查看当前 PO 下的 order_item 是否引用了与本行图纸号相同, 但rev不同的另外的一个图纸, 如果是, 则更新指向当前的新图纸

当前这种情况属于设计上的问题, 同一个图纸号, 即使大小写不同, 其也应该代表相同的记录
探讨: 如何规避这种情况, 有哪些设计上技巧可以避免这个问题
给出方案: 
1. 如何对数据库或者业务进行修改, 使后续避免这个问题
2. 如何修改以处理当前数据库中的重复问题

1. 更新record_db技能, 涵盖这个问题
2. 更新 claude.md, 标记这个问题

Edit part 选择 RT79-87630-PN-R005_import, 但显示的条目仍然是占位数据

---

修改 Search 页面, 添加对于 PO 和 Job 的搜索支持
当前, 点击搜索结果的view按钮, 必然会重定向到drawing. 我希望
- 当匹配到的结果是po或job时, 定向到单po页
- 当匹配到drawing时, 定向到图纸查看页面
MP Schedule 的 loading 没有动画, 是否有圆环形的动画

---

All PO 页面修改

将主界面的条目进行排序
- 先按照PO进行分组, 相同PO分为一组, 组与组之间有半行的间隔
- 组内按照line number进行排序
- 组间按照每组的第一条内容的oe进行递增排序

---

- 将按钮名称改为 MP Schedule 而不是mfg schedule
- 随着搜索框输入数据, 下方条目确实被过滤了, 但是右边的甘特图与左侧的项目不在一行上了, 出现了断裂

---

修改 schedule 页面
- 将按钮名称改为 MP Schedule
每次加载该页面都有较大延迟, 添加loading样式, 效果与本项目其他页面loading样式保持一致
- 在 PO 号的右边, 添加一个搜索框, 样式参考搜索页面
  - 功能包括随用户输入信息过滤下方 order_item, 参考搜索页面
  - 支持 job number, drawing number, description 三列的匹配
- 为 job, customer, due date 三列的标题添加点击排序功能
- 页面打开的默认效果是按job number升序
- 在修改时间单位的单选框左侧添加一个filter按钮
  - 其中, 为 PO, description, customer, quantity, due date, memo, status 分别添加一个复选框
  - 下方的界面只显示复选框check的列


- Memo 应该设计成一种只显示固定宽度内容的格子
  - 当选中时, 弹出一个对话框, 显示所有的笔记, 并且可以添加新笔记, 参考drawing页面的notes逻辑
  - 每条笔记其值应该对应一个 part_note.content
  - 鼠标悬浮在上面时, 显示具体信息

- Assign step 对话框拉长一些, 下面的按钮显示不出来
- 甘特图中的时间跨度上目前只显示 shop code, 应该使用格式 {shop code}: {description}, 超过的宽度部分以省略号结束, 鼠标移上去时弹出提示, 显示全部 description
- status 列的文本修改为 一个分数, {完成的步骤} / {所有步骤}

---

设计并添加一个 Manufacturing Schedule
- 这个页面通过同名按钮触发
- 其设计的初衷是追踪每一个零件的生产进度, 应该是一个以时间为单位的甘特图
- 工具栏右侧包含一个零件图标, 用于规定设置, 例如时间跨度, 支持天/周/月显示
- 整个界面类似于 All PO 界面, 左侧为零件基本信息, 右侧为以天为单位的时间条, 每一行代表一个零件
  - 左侧界面的列为
    - PO
    - Job
    - Customer
    - Drawing Number
    - Description
    - Qty
    - Due Date
    - Memo
    - Status
  - Status 记录了本行零件当前的状态, 
    - 是一个下拉选择的组件
    - 下拉列表数据来源是 process template, 
    - 而对其的编辑可以批量修改写入step_tracker.status
      - 举例, 当前选择第三步, 那前两个步骤直接修改其结束时间为当前时间戳
      - 这样, 只需要检查每个步骤是否有截至时间即可追溯当前进度
      - 或者说应该对数据库结构进行修改, 你有什么意见
  - Memo 应该设计成一种只显示固定宽度内容的格子
    - 当选中时, 弹出一个悬浮组件, 显示所有的笔记
    - 每条笔记其值应该对应一个 part_note.content
  - 右侧的界面, 每列为一个时间单位, 该单位应该可以通过设置进行改变, 显示对应的时间跨度
    - 时间跨度应该为以当前时间前后一年扩展
    - 右侧的每一个格子应该都都是一个下拉列表点击可以选择该行零件的 process_template.
      - 每个 process 所占据的连续时间的开头和结束作为 step_tracker 的起止时间
    - 帮我考虑这样是否合理, 是否会占据大量的系统资源
  - 帮我考虑如何解决左右侧的数据同步问题
    - 举例, 右边设置了步骤三, 但是左边的status只是步骤二, 那就出现了矛盾
      - 是否应该将左边的status设置为只读, 跟随右边的修改进行修改, 你有什么其他的建议
- 帮我想一想, 对于上面的方案, 你还有什么比较酷的想法, 或者是参考一下通常的实现方法是什么
- grill me
  

---

添加 Part 页面
- 在单PO点击按钮进入, 上方返回按钮返回单PO页面
- 注意, 此页面应该针对来源的order_item按订单进行显示
  - 其包含一般part信息和针对订单的特质信息
  - 不同order_item对应的零件应该具有一部分不同的内容, 目前仅step track不同
- 工具栏放 Drawing Number: {图纸号}
- 与单个PO页面类似, 先放notes区域
- 下方为基本信息区, 包含
  - revision
  - Description
  - Is Assembly (转化为Yes/No/Unknown)
  - 按钮 Tree View
- 紧接着是 Drawing PDF 区域
  - 罗列所有改图纸的PDF文件, Is Active 的那个在最上, 其余按created_at降序排列
- DIR区域, 后续完成
- Process template区域
  - 结合step_tracker的步骤进行显示, 每行为一个步骤, 内容为:
    - operator
    - machine
    - shop_code
    - row_number
    - description
    - remark
    - status
    - start_time
    - end_time
- 结合 grill me 技能

---

修改单PO页面
- 在图纸号列取消树形结构, 仅展示列表
- 将 Release Date/Due Date放到Job No下面, 脱离表格显示
- 将消息区域置于 Job 区域上方

添加单个 PO 页面
- 应该包含的内容包括
  - P.O.  purchase_order.po_number
  - O.E.  purchase_order.oe_number
  - 以 Job # 进行划分的区域
    - 每个区域包含
      - Job No 标题  job.job_number
      - 下方的每个 order_item, 以 Line # 表示, 每行一条记录
      - 该行可以被展开为树形结构, 样式参考图纸显示界面的树形结构
        - 该树应该占据最左列, 右边的每一列为节点的信息列, 具体包括
          - Drawing Number  part.drawing_number
          - Rev.  part.revision
          - Description
          - Rls Date  order_item.drawing_release_date
          - Due Date  order_item.delivery_required_date
          - 一个打开pdf按钮, 点击使用默认软件打开pdf文件
          - 一个打开零件按钮, 后续实现
      - 在order_item行的末尾应该包含一个查看树按钮, 点击进入view drawings界面, 点击该界面返回按钮, 返回当前页面
  - 还应该包含一个Notes区域, 其中可添加对于该PO的备注, 后续实现

使用 grill me 对我进行提问

---

添加PO页面
- 该页面应该显示所有数据库中的PO
- 在搜索页面添加一个按钮 All POs 进行访问
- 其样式应该与搜索页面相似, 每行一条PO
- 列包括:
  - P.O.  purchase_order.po_number
  - O.E.  purchase_order.oe_number
  - Job No  job.job_number
  - Line No order_item.line_number
  - Customer  customer.customer_name
  - Contact customer_contact.contact_name
  - Qty order_item.quantity
  - Drawing Number  part.drawing_number
  - Rev.  part.revision
  - Rls Date  order_item.drawing_release_date
  - Due Date  order_item.delivery_required_date
- 级联查询所有需要的信息, 考虑是否应该建立对应的 index
- 行可以选中, view 按钮先返回一个 log 信息, 后续会导航到单PO页面
- 单PO页面用于显示具体一个PO的信息, 后续开发

---

修改搜索页面
- PO 列宽度再增加 1/4
- 搜索框居中显示
- 每列的内容应该在左边添加1个字符的padding
- 每行的内容应该被当作总体对待, 目前点选时在整行变蓝的同时, 具体的格子也会被黑框选中, 去掉黑框部分

---

本session主要任务: 添加搜索功能
用例:
1. 用户点击 search 按钮后, 主界面进入搜索界面, 包含顶部的工具栏和一个搜索结果显示区域
	- 工具栏包含一个搜索输入框和一个返回按钮, 搜索输入框用于输入图纸号/Job号/PO号等信息, 返回按钮用于返回主界面
2. 用户在搜索输入框中输入图纸号/Job号/PO号等信息后, 搜索结果显示区域应该显示所有匹配的图纸信息, 包括PO, Job, 图纸号, 版本号
3. 搜索结果应该随着用户的数据进行动态更新, 即用户每输入一个字符, 搜索结果就应该进行一次更新, 显示所有匹配当前输入的图纸信息
	- 如果此方法过于频繁, 可以设置一个输入延迟, 例如用户停止输入500毫秒后再进行搜索更新
4. 用户可以点击搜索结果中的任意一行, 应该导航到对应图纸的查看界面, 为了应对此功能, 图纸查看界面应该进行升级
5. 搜索结果中的每一行应该包含一个打开图纸文件的按钮, 点击后可以直接打开对应的图纸PDF文件

图纸查看界面升级:
1. 该界面现在应该接受一个新的参数, 即图纸的唯一标识符, 例如图纸ID或图纸号
2. 开发阶段, 使用硬编码 part.id=3490 的图纸信息
3. 界面的逻辑产生变化, 要求根据传入的图纸信息进行显示
	- 递归查询到该图纸对应的所有上级图纸, 找到最顶层的图纸
	- 根据最顶层图纸的信息, 找到其包含的所有子图纸, 包括当前图纸
	- 将这些图纸构筑成一个树形结构, 在左侧显示该树形结构
	- 剩余部分与之前的图纸查看界面相同, 只是左侧的树形结构的图纸的来源发生了变化
	- 最后应该注意的是, 最初接受的图纸应该在属性结构中被初始高亮显示

---

修改项目:
1. *_import.json 文件存放在单独的文件夹, 而不是直接放在程序的目录

2. 为构造区和图纸列表区的元素添加右键菜单
	- 从左侧列表中复制: 在列表中创建一个当前元素的副本
	- 从左侧列表中移除: 将当前元素从列表中移除

3. 为程序添加类似 MUI 的 snackbar 组件, 为用户提供操作结果的提示

---

添加一个编辑零件界面
- 在 Home 工具栏添加一个 Edit Part 按钮, 位于 View Drawing 前
	- 点击该按钮, 与构造界面相同, 弹出 json 文件选择对话框
	- 选择一个 *_import.json 文件后
		- 使用 SQL 脚本, 用图纸号获取每个零件的信息
		- 将该文件的每个 part 的信息显示在主界面
- 界面上方工具栏与 Build Drawing Tree 界面相同

每行信息包括
- 序号
- 图纸号
- Revision
- Description
- Quantity in Assembly
- is Assembly
- File Path
- Browse 按钮
- PDF 按钮
- Save 按钮
- 占位符, 用于显示绿色的 check-circle

点击每行的 Save 按钮
- 判断当前修改的内容与数据库中是否相同, 如果相同则认为成功
- 如果不同, 则分别列出两组值, 并询问是否覆盖, 点击覆盖则修改数据库该零件内容
- 如果修改成功, 则在占位符显示绿色的 check-circle
- 如果出现错误, 则在该位置显示红色的 cancel

点击工具栏上的保存按钮
- 遍历所有的未成功的行, 逐个触发每行 Save 按钮逻辑

