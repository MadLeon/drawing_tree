未来实现
- customer 按照使用情况进行排序的功能
- 写脚本, 使用DIR Log中的数据批量更新图纸状态
- 在图纸界面, 用户能够点击创建按钮使用软件 UI 编写 MP (未来实现)

---

急需修复

Order Entry Log 无法拾取所有输入的问题

- 在drawing editor中指定的文件夹中包含该PO所有的零件列表, 而数据库中并没有一个结构能够储存这个文件列表, 所以使用外部的json文件进行储存
  - 也就是说, 我如果只传PO给part editor, part editor无法得知具体的零件列表
  - 但在此处, 可以将文件名和路径直接存到数据库
- 使用json文件做中介还有一个用处就是可以起到类似保存的效果, 即用户可以直接从edit part开始
- 我能想到的是是否需要在数据库中添加一个中间表, 用来链接part和po, 但似乎没有必要, 因为在edit part结束后, part列表可以关联到 order_item上, 这样, 我们就可以从part - order_item - job - purchase_order 一路级联上去
- 我想到的, 唯一能解耦的地方在part editor和treebuilder, 进入树构建界面后, 直接级联查到po下的part列表, 直接使用这里的数据
你有什么想法?

---

写可以复用的脚本, 用于手动更新数据库中 purchase_order.is_active 字段的值.

- 数据来自文件, 位置为 "\\rtdnas2\OE\Order Entry Log.xlsm"
- 使用开发数据库, 但应该很容易修改目标数据库的地址
- 该文件极度重要, 每次操作必须使用只读方式, 避免破坏该文件造成公司损失
- 该文件通常被其他用户打开, 即使用 UI 也只能选择只读方式
- 数据库中的 purchase_order.is_active 已经有值, 只需要对状态进行更新
- 该文件 AA 列记录了每个 order_item.id
- 提取该列信息并使用这些 id 更新数据库中 PO 的状态
  - 只有id对应的PO应该被认为是活跃的, 否则应标记为不活跃