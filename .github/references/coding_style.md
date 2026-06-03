---
name: creating-vba-macro
description: "Create and develop VBA macros for Excel automation. Use for: writing VBA code, generating macro functions, automating Excel workflows, debugging VBA scripts, building spreadsheet extensions."
argument-hint: "Describe the macro purpose and automation workflow"
user-invocable: true
---

# Creating VBA Macros

## When to Use

- Generating new code
- Fixing bugs in existing code
- Creating test functions for validation
- Writing debug print and error handling code

## VBA Best Practices

- **Option Explicit**: Always declare variables, 但确保不要重复声明
- **Naming**: Use descriptive names for procedures and variables
- **Logging**: 当编写 debug 和错误处理代码时, 参考 [logging_style.md](./references/logging_style.md)
- **Testing**: 你编写的代码应该尽可能包含一个测试函数, 它应是一个独立的文件, 文件名为 `test_{功能描述}`, 例如 `test_data_processing`

## Resources

- [Code & Comment Patterns](./references/patterns.md)
- [Logging Style](./references/logging_style.md)