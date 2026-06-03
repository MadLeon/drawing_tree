# Code & Comment Pattern

## 代码格式

- 使用 C# XML 文档注释风格进行注释, 注释语言使用英语
- 在文件顶部添加文件说明注释, 格式如下:
  - 文件名应该使用 PascalCase 命名规范
  - 类名应与文件名一致
- 每个新文件的开头如下:

```c#
/// <summary>
/// Global Variables Declaration Module (GlobalVariable.cs)
/// Central storage for application-wide global variables.
/// Contains development mode flags and configuration settings.
/// Should be imported first before other modules that depend on globals.
/// </summary>
/// <remarks>
/// Usage:
/// - Modify DEBUG_MODE to enable/disable development logging
/// - Add more global variables as needed for application state
/// </remarks>
```

## 注释格式

- 使用 C# XML 文档注释风格进行注释, 注释语言使用英语
- 对于每个方法/函数, 使用以下格式进行注释:

```c#
/// <summary>
/// Cleans filename by removing special characters.
/// Converts "RT-88000-70097-045-1-DD-C.pdf" to "RT-88000-70097-045-1-DD-C".
/// </summary>
/// <param name="rawName">Original filename</param>
/// <returns>Cleaned filename without extension and special characters</returns>
public string CleanFilename(string rawName)
{
    // Implementation
}
```

- `<summary>` 标签表明方法用途和功能描述
- `<param>` 标签注明每个参数, name 属性必须与参数名称完全一致
- `<returns>` 标签注明返回值说明
- 如有异常抛出, 使用 `<exception>` 标签说明
- 示例代码使用 `<example>` 和 `<code>` 标签
- 注释结束后, 另起一行编写方法代码