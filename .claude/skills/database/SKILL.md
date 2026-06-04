---
name: database
description: 'Database interaction and schema management. Use this skill when: querying or manipulating the job management database (\\rtdnas2\OE\record.db); creating or running database migrations; checking database structure and integrity; understanding table relationships and schemas; writing SQL queries against the job management database. Trigger on requests involving: database operations, schema changes, data persistence, migration tasks, database debugging, or SQL queries. Reference the schema documentation for table structure before writing queries.'
license: Proprietary
---

# 数据库交互与架构管理

## 概述

- 本项目使用 SQLite3 数据库存储工作管理、订单、零件、生产等数据。正式数据库位于 `\\rtdnas2\OE\record.db`，开发环境使用 `data/record.db`。
- 数据库使用 dbeaver 进行管理和查询, 通过 SQL 命令在软件中进行数据操作和迁移。
- 本系统运行在 Windows 环境, 如需编写相关脚本，请使用 PowerShell。

## 参考资源

- [数据库表结构详细文档](reference/schema-reference.md)
- [数据库表总结（记录统计）](reference/table-summary.md)

## 快速开始

### 检查数据库结构和内容

- 参考./reference文件夹中的文件来了解数据库表结构、记录数、样本数据
- 如需获取最新统计信息，运行以下命令：

```powershell
node ./scripts/check-db.js
```

## 数据库备份

### 备份（开发环境）

- 使用以下命令, 并确保文件名严格按照 `record.db.backup-YYYYMMDD-HHMMSS` 格式命名：

```powershell
Copy-Item data/record.db "data/record.db.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
```

## 最佳实践

- **使用外键约束**：在设计新表时定义适当的外键关系
- **时间戳**：使用ISO 8601 格式 `datetime('now', 'localtime')` 记录本地时间. 例如: 2026-05-28 21:43:15
- **事务安全**：多条相关 SQL 语句应包装在事务中
- **备份迁移前**：始终在应用重大迁移前备份数据库

## 环境配置

- **开发**：`data/record.db` (SQLite3)
- **生产**：`\\rtdnas2\OE\record.db` (SQLite3, 网络挂载)
