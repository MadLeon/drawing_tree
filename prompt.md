未来实现
- customer 按照使用情况进行排序的功能
- 写脚本, 使用DIR Log中的数据批量更新图纸状态
- 在图纸界面, 用户能够点击创建按钮使用软件 UI 编写 MP (未来实现)
- DISCUS 是否具备输出中间json的可能性
- 是否有图纸标注的开源项目

添加dir的粗糙度问题
bubble drawing 区域点击复制按钮后, 复制的信息为最后一个'-'后面的内容, 
- 举例, Hook Assembly of Extension Tool - Hook Weldment - Hook -> Hook
- 如果没有破折号, 则原样输出

process detail 关联时, 定位到的文件夹应该是 {mp文件夹}\{经过过滤的PO号}这个文件夹, 而不是config中的mp文件夹或者没经过过滤的PO

---

OE 同步 - 逻辑图解
https://claude.ai/code/artifact/290147f1-b050-4494-acc9-4d5b65be2b31


目前项目的MP Schedule实现了手动更新状态, 未来计划加入条码模块, 可以借由扫码自动更新状态
还缺少接到订单以后的项目scheduling部分
结合我现在的项目开发方向和程度, 如果希望在 scheduling 方向继续前进, 应该如何做
是否有现成的开源项目我可以借鉴学习, 你可以访问github为我推荐