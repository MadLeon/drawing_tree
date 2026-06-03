# VBA Modules Usage Guide

## Global Variables 模块详解

### 主要配置

| 变量             | 类型    | 默认值 | 说明                       |
| ---------------- | ------- | ------ | -------------------------- |
| `DEBUG_MODE`     | Boolean | True   | 控制Logger是否输出详细日志 |
| `DISABLE_LOGGER` | Boolean | False  | 完全禁用所有Logger功能     |

### DISABLE_LOGGER vs DEBUG_MODE 的区别

- **DEBUG_MODE**：控制日志的详细程度
  - True：输出所有日志（包括 LogDebug）
  - False：仅输出关键日志（LogError, LogInfo），过滤掉 LogDebug

- **DISABLE_LOGGER**：控制是否完全禁用Logger
  - True：所有日志函数直接返回，不执行任何操作（包括不写入文件）
  - False：Logger正常运行

---

## Logger 模块详解

### 日志文件位置

- 默认路径：`工作簿所在目录\mp_log.txt`
- 获取路径：`GetLogFileFullPath()`

### 日志格式示例

```
[2026-05-05 14:30:25] [INFO] 操作开始：获取图纸信息
[2026-05-05 14:30:26] [DEBUG] 处理 PO-001，图纸 DWG-123
[2026-05-05 14:30:27] [ERROR] 警告：某个文件缺少part_id

[2026-05-05 14:25:10] [INFO] 上一个操作已完成
```

### 特性说明

- **块级日志**：同一操作的日志被分组为一个块
- **时间顺序**：块内日志按时间正序排列（最早先）
- **最新优先**：新的日志块在文件顶部
- **缓冲机制**：日志在调用 `FlushLogBlock()` 时才写入文件

### API参考

| 函数                   | 描述             | 例子                                 |
| ---------------------- | ---------------- | ------------------------------------ |
| `StartLogBlock()`      | 开始新日志块     | `StartLogBlock`                      |
| `LogDebug(msg)`        | 记录调试信息     | `LogDebug "变量值：x=" & x`          |
| `LogError(msg)`        | 记录错误信息     | `LogError Err.Description`           |
| `LogInfo(msg, cat)`    | 记录信息         | `LogInfo "完成", "SUCCESS"`          |
| `FlushLogBlock()`      | 写入所有日志     | `FlushLogBlock`                      |
| `ClearLog()`           | 清空日志文件     | `If ClearLog() Then MsgBox "已清空"` |
| `GetLogFileFullPath()` | 获取文件完整路径 | `path = GetLogFileFullPath()`        |
| `IsLogBlockActive()`   | 检查是否有活跃块 | `If IsLogBlockActive() Then ...`     |

使用 call 函数时, 应该使用 "()" 将参数括起来, 若直接使用参数会发生错误

```vba
Call LogInfo "操作开始" ' - 发生传参错误
```

### 提示

**使用 Call 函数的正确方式**：

应该使用 "()" 将参数括起来，若直接使用参数会发生错误

```vba
Call LogInfo("操作开始") ' - 正确
Call LogInfo "操作开始"  ' - 发生传参错误
```

**何时检查DISABLE_LOGGER**：

- 在所有需要调用Logger函数的地方（StartLogBlock, LogDebug 等）
- 在 If 条件中判断：`If Not DISABLE_LOGGER Then`

### 使用示例

```vba
' ===== 主操作流程 =====
Sub MainOperation()
    On Error GoTo ErrorHandler

    ' 判断Logger是否被禁用
    If Not DISABLE_LOGGER Then
        StartLogBlock
        LogInfo "主操作开始"
    End If

    ' 如果处于调试模式，输出更详细的日志
    If DEBUG_MODE And Not DISABLE_LOGGER Then
        LogDebug "初始化完成，开始主流程"
    End If

    ' 第一步
    If Not Step1() Then GoTo StepFailed

    ' 第二步
    If Not Step2() Then GoTo StepFailed

    If Not DISABLE_LOGGER Then
        LogInfo "主操作完成"
        FlushLogBlock
    End If

    Exit Sub

StepFailed:
    If Not DISABLE_LOGGER Then
        LogError "操作失败"
        FlushLogBlock
    End If

ErrorHandler:
    If Not DISABLE_LOGGER Then
        LogError "异常：" & Err.Description
        FlushLogBlock
    End If
End Sub
```

---

## SQLite 模块详解（可选）

### 快速开始

```vba
' 初始化
If Not InitializeSQLite("C:\data\record.db") Then
    LogError "数据库初始化失败"
    Exit Sub
End If

' 执行查询
Dim results As Variant
results = ExecuteQuery("SELECT * FROM jobs WHERE status='active'")

' 获取数据
If Not IsNull(results) Then
    Dim row As Long, col As Long
    For row = LBound(results) To UBound(results)
        For col = LBound(results, 2) To UBound(results, 2)
            ' 使用 results(row, col)
        Next col
    Next row
End If

' 关闭连接
CloseSQLite
```

### API参考

| 函数                     | 返回值           | 说明                       |
| ------------------------ | ---------------- | -------------------------- |
| `InitializeSQLite(path)` | Boolean          | 初始化数据库连接           |
| `ExecuteQuery(sql)`      | Variant (2D数组) | 执行SELECT查询             |
| `ExecuteNonQuery(sql)`   | Boolean          | 执行INSERT/UPDATE/DELETE   |
| `ExecuteUpdate(sql)`     | Long             | 执行UPDATE，返回受影响行数 |
| `CloseSQLite()`          | -                | 关闭连接                   |

### 数据类型转换

SQLite3的数据类型会自动转换为VBA类型：

- SQLITE_INTEGER → Long
- SQLITE_FLOAT → Double
- SQLITE_TEXT → String
- SQLITE_NULL → Null
