使用 session skill 来管理当前工作流, 下面是 input 内容:

本session主要任务: 添加图纸关系构建功能

- 


## 数据库脚本

### drawing_file 表

根据图纸提取结果将图纸文件信息存储在 drawing_file 表中, 并与 part 表建立关联
触发条件: 图纸提取完成, 用户点击 Confirm 并成功后

