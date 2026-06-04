# CLAUDE.md — Drawing Tree 项目指南

## 第一优先级规则

- **所有回复消息必须使用简体中文**，代码部分使用英文
- 项目运行于 **Windows 环境**，CLI 命令使用 **PowerShell** 语法，不使用 Linux 命令
- 永远不要假设任何事情，始终检查代码库以确认细节
- 永远不要编造代码或细节，不确定时查阅代码库
- **绝不偷懒**，遇到 bug 必须找到根本原因并修复，不做临时补丁

## 工作流程

1. 思考问题，阅读代码库中的相关文件，在 `tasks/todo.md` 中制定计划
2. 计划包含可逐一勾选的待办项
3. 开始工作前与用户确认计划
4. 逐步完成待办项，完成时标记为已完成
5. 每一步只给出高层次的变更说明
6. **保持简单**：每次变更影响的代码量尽可能少，避免大规模或复杂的改动
7. 完成后在 `tasks/todo.md` 末尾添加 review 章节，总结变更内容

## 项目概述

Drawing Tree 是一个 Windows 桌面应用（WPF / .NET），主要功能：

- **图纸树构建**：从 PDF 文件夹提取图纸号，通过拖拽构建图纸间关系
- **图纸查看**：搜索图纸号/Job号/PO号，左侧树形结构 + 右侧 PDF 预览

### 代码位置

- 所有源码在 `src/` 目录
- 历史 session 记录在 `sessions/*.md`

## 代码风格

使用 C# XML 文档注释，注释语言为**英文**。

**文件头模板：**

```csharp
/// <summary>
/// FileName.cs
/// Brief description of the file's purpose.
/// </summary>
/// <remarks>
/// Usage:
/// - Key usage note 1
/// </remarks>
```

**方法注释模板：**

```csharp
/// <summary>
/// Cleans filename by removing special characters.
/// Converts "RT-88000-70097-045-1-DD-C.pdf" to "RT-88000-70097-045-1-DD-C".
/// </summary>
/// <param name="rawName">Original filename</param>
/// <returns>Cleaned filename without extension and special characters</returns>
public string CleanFilename(string rawName)
{
}
```

- 文件名与类名使用 PascalCase
- `<param>` 的 name 属性必须与参数名完全一致

详见 [Code & Comment Patterns](.github/references/patterns.md)

## 日志规范

使用 `src/DrawingTree/Logging/` 中的单例 Logger：

```csharp
Logger.Instance.Info("Application started successfully");
Logger.Instance.Debug("Processing file: example.pdf");
Logger.Instance.Warning("Disk space running low");
Logger.Instance.Error("Failed to load configuration", ex);
```

- 日志存放在应用目录 `Logs/log_yyyy-MM-dd.txt`，自动按天轮转
- 配置文件 `config.txt`，格式为 `key=value`

详见 [Logging Style](.github/references/logging_style.md)

## 数据库

- **开发环境**：`data/record.db`（SQLite3）
- **生产环境**：`\\rtdnas2\OE\record.db`（SQLite3，网络挂载）
- 时间戳格式：`datetime('now', 'localtime')`，例如 `2026-05-28 21:43:15`
- 多条相关 SQL 语句包装在事务中
- 重大迁移前必须备份数据库

```powershell
# 备份命令
Copy-Item data/record.db "data/record.db.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
```

详见 [数据库表结构](.github/skills/database/reference/schema-reference.md) 和 [表总结](.github/skills/database/reference/table-summary.md)

## 可用 Skills

### session
开始新会话时使用，管理任务理解、TODO、session 文件创建和总结。
详见 [.claude/skills/session/SKILL.md](.claude/skills/session/SKILL.md)

### database
数据库查询、迁移、结构管理。
详见 [.claude/skills/database/SKILL.md](.claude/skills/database/SKILL.md)

### grill-me
逐一深入追问计划或设计，直到达成共同理解。
详见 [.claude/skills/grill-me/SKILL.md](.claude/skills/grill-me/SKILL.md)

### creating-vba-macro
创建 Excel VBA 宏，默认导入全局变量模块和 Logger 模块。
详见 [.claude/skills/creating-vba-macro/SKILL.md](.claude/skills/creating-vba-macro/SKILL.md)

## Session 文件规范

- Session 文件存放在 `sessions/session{number}.md`
- 格式参考 [summary_template.md](.claude/skills/session/templates/summary_template.md)
- 开始前阅读历史 `sessions/*.md` 作为背景记忆
