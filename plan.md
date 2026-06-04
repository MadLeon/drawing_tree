使用 .github/skills/session/SKILL.md 这个 skill 来管理当前工作流, 下面是 input 内容:

本session主要任务: 添加图纸关系构建功能

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

- 修复打开图纸按钮
- 移除构造区展开收起功能, 以及相关的"+"和"-"按钮, 因为在实际生产环境中, 图纸之间的层级关系是由数据库中的 order_item 来确定的, 用户不应该通过拖拽来调整图纸之间的层级关系
- 使用mermaid的样式, 当前垂直连线直通底部
- 左侧图纸列表的元素被选中时应该也高亮显示
- is assembly 复选框应该与标题在同一行, 复选框位于标题的右侧
- 略微拓宽 drawing info 区的宽度

修改逻辑并添加功能:
- 在生产环境, 可以通过数据库根据 po_number 获取:
	1. 当前订单中的所有 job_number (一个或多个)
	2. 每个 job_number 对应的 order_item (一个或多个)
	3. 每个 order_item 对应的 part, 即顶层装配图 (一个)
- 我们目前的顶层装配图是根据第一个拖拽的图纸来确定的, 但在实际生产环境中, 顶层装配图应该是根据数据库中的 order_item 来确定的
- 所以正确的逻辑应该是在用户点击 build drawing tree 后
	1. 应该根据 po_number 从数据库中级联查询出所有的 job_number, order_item, part (顶层装配图)
	2. 然后直接将该装配图作为树结构的根节点, 用户在构建树结构时只能将其他图纸拖拽到该装配图下
	- 当前处于开发阶段, 使用下列的模拟数据来实现上述逻辑:
		- purchase_order.po_number: "RT79-87630-PN-R005"
			- job.job_number: "72395"
				- order_item.part_id (FK)
					- part.drawing_number: "RT-87630-71254-1000-1-GA-D"
			- job.job_number: "72396"
				- order_item.part_id (FK)
					- part.drawing_number: "RT-87630-71254-1000-1-GA-D"
			- job.job_number: "72397"
				- order_item.part_id (FK)
					- part.drawing_number: "RT-87630-71254-1010-1-DD-D"
	- 注意, 如果存在多个 job_number 对应同一个 order_item, 则它们对应的 part 也是同一个, 这种情况下应该只存在一个该 part 对应的图纸元素, 而不是重复多个相同的图纸元素, 但是 job number 应该注明
	- 构造区应该进行重构, 以适应上述逻辑的变化, 具体来说:
		- 使用 job number 来区分不同的 job 对应的图纸元素
			- 先显示 "Job Number: <job_number>", 字体大一号, 加粗
			- 在 job number 下方显示顶层总装图
			- 如果多个 job_number 对应同一个顶层总装图, 则显示 "Job Number: <job_number1> & <job_number2>"

如需了解数据库结构可以使用 /database 技能

---

	
本session主要任务: 添加搜索功能
用例:
1. 用户点击 search 按钮后, 主界面进入搜索界面, 包含一个搜索输入框和一个搜索结果显示区域
2. 用户在搜索输入框中输入图纸号/Job号/PO号等信息后, 搜索结果显示区域应该显示所有匹配的图纸信息, 包括图纸号、图纸名称、版本号等基本信息



在导入界面添加 Revision 列, 位于浏览按钮列前
confirm 按钮点击后, 将 Revision 信息存储在 .json 文件中, 该字段非必填, 用户可以选择是否填写
在导入界面的每行添加打开图纸文件按钮, 位于浏览按钮后, 点击后可以直接打开对应的图纸PDF文件

## 数据库脚本

### drawing_file 表

根据图纸提取结果将图纸文件信息存储在 drawing_file 表中, 并与 part 表建立关联
触发条件: 图纸提取完成, 用户点击 Confirm 并成功后

问题:

- job_number 字段的值应该与数据库中的 order_item 相关联
- 
