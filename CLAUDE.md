# CLAUDE.md

## 参考文件

- 项目描述: README.md
- 项目目标: ./reference/project_description.md
- 样本数据: ./data/
- 数据库表结构: ~/.claude/skills/database/reference/schema-reference.md
- 表总结: ~/.claude/skills/database/reference/table-summary.md

## 开发历史

- 会话记录 `sessions/*.md` 作为背景记忆
- 开发历史: ./reference/dev_history.md

## 数据库变更追踪

- 所有开发期间的数据库结构变更（DDL）记录在 `./data/db_changes.sql`
- 上线前需将此文件中的语句应用到生产数据库 (`\\rtdnas2\OE\record.db`)

## UI 功能测试

- 所有涉及 UI 的测试, 不要使用自行打开并截图的方法
- 用户负责测试, 并把不合理的地方提供给你

## UI 样式

- 本应用所有的实际 UI 文本全部严格使用英文

## 零件数据调用

- 通过图纸名调用part时, 可能有多个同名part, 确保调用revision最新的那条
- 在sql中使用降序排列, 并limit最上面的一个
- 应用: Edit Parts 界面; All PO 界面的展开部分

## 加载动画

- 使用 LoadingOverlayControl（Controls/LoadingOverlayControl.xaml）；在父 Grid 中放置 <local:LoadingOverlayControl x:Name="LoadingOverlay" Visibility="Collapsed" Panel.ZIndex="10"/>，通过切换 LoadingOverlay.Visibility 显示或隐藏旋转遮罩

## 工具栏返回按钮样式

- Style Key: `IconBtn`（定义在 `App.xaml`，全局可用）
- Icon Geometry Key: `ChevronLeftGeo`（定义在 `App.xaml`）
- 用法：在工具栏最左侧的返回按钮上使用，Path Fill 为 `#333333`，按钮尺寸通常为 28×28，图标 14×14

## 内联图标链接按钮样式

- Style Key: `IconLinkBtn`（定义在 `App.xaml`，全局可用）
- 用法：用于列表行内的图标操作按钮（打开 PDF、打开文件等），区别于 `IconBtn`（仅用于工具栏返回按钮）
- 特点：无固定尺寸，内边距通过 `{TemplateBinding Padding}` 绑定，悬停背景 `#20000000`
- 在 code-behind 中使用：必须用 `FindResource` 而非 `Resources[]`（后者只查本地字典，不遍历 App 级资源）