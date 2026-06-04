---
name: creating-vba-macro
description: "Create and develop VBA macros for Excel automation. Use for: writing VBA code, generating macro functions, automating Excel workflows, debugging VBA scripts, building spreadsheet extensions."
argument-hint: "Describe the macro purpose and automation workflow"
user-invocable: true
---

# Creating VBA Macros

## When to Use

- Writing or generating VBA macro code for Excel
- Creating custom Excel functions and utilities
- Building macro-based tools for data manipulation
- Troubleshooting VBA script issues

## References

- 处理涉及到 excel 文件的问题, 使用 .github/skills/xlsx 这个技能
- "一句话中文" 意思为: 该文本内容是中文，并且用一句话进行总结
- "要点格式" 意思为: 使用中文并采用 markdown 的 bullet points 格式进行输出, 并且每个要点都应该尽量简短且为一句话
- vba代码中不能使用中文, 只能使用英文
- 默认情况下, 全局变量模块和日志模块应该始终被导入, 以确保全局变量和日志功能的可用性, 除非用户明确表示不需要其中一个或两个模块
- 在编写vba代码时, 请注意结合日志模块进行日志记录, 以便于调试和维护
- 你编写的vba代码应该尽可能包含一个测试函数入口, 方便用户进行快速测试和验证, 但前提是用户没有明确表示不需要测试函数
- 测试函数应是一个独立的文件, 格式为 `tst_{功能描述}.bas`, 例如 `tst_data_processing.bas`, 并且应该包含一个公共子程序 `Test()` 作为测试入口

## General Workflow

1. Generate a clear understanding of the session task, using the following format:

- 简短任务总结（一句话中文）
- 理解与推断（要点格式）

2. Output the understanding (from step 1) and pause for user confirmation

3. Plan TODO steps based on the understanding, output the TODO steps, then pause for user confirmation

4. 当用户确认TODO步骤后可以开始, for each TODO step:

- Execute the step
- Output the result
- Pause for user confirmation before proceeding to the next step
- 注意, 在此步骤结束以前, 不要进行任何下一步的操作, 包括但不限于: 代码修改, 文件创建, session文件创建等

5. 在本session结束后, 经过用户确认之后, 创建一个新的 session 文件在 `/session_summary`，文件格式为 `{高度概括的任务目标}.md`

6. Generate:

- Session内容总结（要点格式）
- 操作及决策（要点格式）

7. Append the input and generated content to the session file, including:

- 原样复制输入给skill的信息
- Copy instructions and understanding:
  - 简短任务总结（一句话中文）
  - 理解与推断（要点格式）
- Copy 第6步中生成的Session内容总结（要点格式）
- Copy 第6步中生成的操作及决策（要点格式）

## VBA Best Practices

- **Option Explicit**: Always declare variables, 但确保不要重复声明
- **Error Handling**: Use `On Error` statements appropriately
- **Naming**: Use descriptive names for procedures and variables
- **Code Style & Comments**: Follow [代码格式](references/patterns.md) and document complex logic
- **Performance**: Disable screen updates and calculations during heavy operations
- **Logging**: Follow [Logger 模块详解](references/modules-guide.md) for consistent logging practices

## Built-in Modules

### Global Variables Module (dat_global_variable.bas) - ⭐ 首先导入

该模块默认存在, 用于中央全局变量存储，包含开发模式配置。

**主要功能**：

- `DEBUG_MODE` - 布尔标志，控制Logger的详细日志级别
- `DISABLE_LOGGER` - 布尔常量，设置为 True 时完全禁用 Logger 模块功能

**文件位置**：`./assets/modules/dat_global_variable.bas`

**注意事项**：

- 应该 **首先导入** 此模块，然后导入其他依赖的模块
- 开发环境保持 DEBUG_MODE = True，生产环境改为 False
- 设置 `DISABLE_LOGGER = True` 可以完全禁用日志系统，所有 Logger 函数将直接返回而不执行任何操作

### Logger Module (lib_logger.bas) - ⭐ 常驻功能

提供块级别日志记录支持。所有日志缓冲后写入文件，最新日志在顶部。当 `DISABLE_LOGGER` 为 True 时，所有日志函数将被禁用。

**主要功能**：

- `StartLogBlock()` - 开始新的日志块（当 DISABLE_LOGGER 为 True 时将被跳过）
- `LogDebug(message)` - 记录调试信息（当 DISABLE_LOGGER 为 True 时将被跳过）
- `LogError(message)` - 记录错误信息（当 DISABLE_LOGGER 为 True 时将被跳过）
- `LogInfo(message, category)` - 记录信息（当 DISABLE_LOGGER 为 True 时将被跳过）
- `FlushLogBlock()` - 将缓冲的日志写入文件（当 DISABLE_LOGGER 为 True 时将被跳过）
- `ClearLog()` - 清空日志文件（当 DISABLE_LOGGER 为 True 时将被跳过）

**文件位置**：`./assets/modules/lib_logger.bas`

**注意事项**：

- 不要忘记调用 `FlushLogBlock()` 否则日志不会写入文件
- 当 `DISABLE_LOGGER = True` 时，所有日志功能将自动禁用，无需修改调用代码
- Logger 会在每个函数开头检查 DISABLE_LOGGER 标志，如果禁用则立即返回，不执行任何操作

### SQLite Module (sqlite.bas) - 可选模块

提供SQLite数据库交互接口

**主要功能**：

- `InitializeSQLite(dbPath)` - 初始化数据库连接
- `ExecuteQuery(sql)` - 执行查询，返回2D数组
- `ExecuteNonQuery(sql)` - 执行INSERT/UPDATE/DELETE
- `ExecuteUpdate(sql)` - 执行UPDATE并返回受影响的行数
- `CloseSQLite()` - 关闭数据库连接

**文件位置**：`./assets/modules/sqlite.bas`

## Resources

- [Module Usage Guide](references/modules-guide.md)
