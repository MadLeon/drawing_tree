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

## 零件数据调用
- 通过图纸名调用part时, 可能有多个同名part, 确保调用revision最新的那条
- 在sql中使用降序排列, 并limit最上面的一个
- 应用: Edit Parts 界面; All PO 界面的展开部分