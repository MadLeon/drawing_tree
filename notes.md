# Excel 配置表、Importer 与 Schema-Driven 架构总结

## Excel + 数据混合模式的优点

* 业务人员容易理解和编辑
* 快速试错，无需开发介入
* 天然适合二维数据（商品、订单、配置表等）
* 在项目早期开发效率极高

## Excel + 数据混合模式的问题

* 数据结构（Schema）、展示（Layout）、数据内容混在一起
* 代码容易依赖列位置而不是数据含义
* 调整表格布局会影响大量业务逻辑
* 耦合度高，维护成本随项目规模增长而急剧上升

## 问题本质

布局（Layout）被当成了逻辑的一部分。

程序依赖：

```text
第3列是价格
```

而不是：

```text
price字段是价格
```

---

# 从 Excel 驱动转向 Schema 驱动

## 初级模式

```text
Excel
↓
业务逻辑
```

## 成熟模式

```text
Excel
↓
Importer
↓
Schema Validate
↓
Domain Model
↓
业务逻辑
```

---

# Importer（导入器）

## 定义

Importer 的职责是：

```text
将外部格式
转换成
内部格式
```

## 示例

Excel：

| 商品ID | 商品名 | 价格  |
| ---- | --- | --- |
| 1001 | 苹果  | 5.5 |
| 1002 | 香蕉  | 3.2 |

导入后：

```json
{
  "id": 1001,
  "name": "苹果",
  "price": 5.5
}
```

## 导入器负责的工作

### 读取数据

* Excel
* CSV
* Google Sheet
* 后台录入

### 识别字段映射

例如：

```json
{
  "商品ID": "id",
  "商品名": "name",
  "价格": "price"
}
```

### 数据清洗

```text
￥5.5 → 5.5
```

```text
是 → true
```

```text
2025/01/01 → Date
```

### 类型转换

```text
"1001" → 1001
```

```text
"5.5" → 5.5
```

### 输出标准对象

```typescript
Product {
    id: 1001
    name: "苹果"
    price: 5.5
}
```

---

# Schema（数据结构定义）

## 定义

Schema 本质上是在描述：

```text
数据应该长什么样
```

例如：

```yaml
Product:
  id: integer
  name: string
  price: number
```

它相当于：

* 数据合同（Contract）
* 数据规范（Specification）

---

# Schema Validate（结构校验）

## 定义

Schema Validate 的作用：

```text
检查数据是否符合Schema
```

## 合法数据

```json
{
  "id": 1001,
  "name": "苹果",
  "price": 5.5
}
```

结果：

```text
✓ Pass
```

## 非法数据

```json
{
  "id": "abc",
  "name": "苹果",
  "price": 5.5
}
```

结果：

```text
✗ Fail

id 应该是 integer
实际是 string
```

---

# 常见 Schema Validator 工具

## Python

Pydantic

```python
from pydantic import BaseModel

class Product(BaseModel):
    id: int
    name: str
    price: float
```

---

## JavaScript / TypeScript

* Zod
* AJV
* TypeBox

例如：

```typescript
const ProductSchema = z.object({
  id: z.number(),
  name: z.string(),
  price: z.number()
});
```

---

## Java

* Hibernate Validator
* Everit JSON Schema

---

## Go

* go-playground/validator

---

## 跨语言方案

* JSON Schema
* Protocol Buffers（Protobuf）
* Apache Avro

---

# 游戏行业的典型实践

策划编辑：

```text
Excel
↓
怪物配置
```

例如：

| ID   | Name   | HP  | ATK |
| ---- | ------ | --- | --- |
| 1001 | Goblin | 500 | 30  |

构建流程：

```text
Excel
↓
导出工具
↓
JSON / Protobuf
↓
客户端和服务器
```

游戏运行时通常不会直接读取 Excel。

Excel 只是配置编辑器。

---

# 企业系统常见实践

```text
Excel
↓
Importer
↓
JSON
↓
Schema Validate
↓
Domain Object
↓
Database
```

特点：

* Excel可以改
* Schema保持稳定
* 数据格式统一
* 业务逻辑不关心Excel布局

---

# Schema First（Schema优先）

更成熟的团队通常先定义 Schema：

```yaml
Monster:
  id: int
  hp: int
  atk: int
```

然后自动生成：

```text
Excel模板
JSON Schema
TypeScript类型
Java类
数据库表结构
API文档
```

核心思想：

```text
Schema
=
Single Source of Truth
（唯一真实来源）
```

---

# 三层职责划分

## Importer

负责：

```text
格式转换
```

例如：

```text
Excel → JSON
CSV → JSON
Google Sheet → JSON
```

---

## Schema Validator

负责：

```text
数据正确性检查
```

例如：

```text
价格必须是数字
ID必须唯一
日期格式必须正确
```

---

## Domain Model

负责：

```text
业务语义
```

例如：

```text
商品是什么
订单是什么
怪物是什么
用户是什么
```

---

# 核心架构思想

不要让程序依赖 Excel。

错误的方式：

```text
程序
↓
直接读取Excel列位置
```

正确的方式：

```text
程序
↓
依赖Schema
↓
依赖Domain Model
```

Excel 只是其中一个输入界面。

---

# 一句话总结

初级系统：

```text
程序依赖 Excel
```

成熟系统：

```text
Excel只是编辑器
程序依赖Schema
```

最终目标：

```text
Layout（布局）
≠
Schema（结构）
≠
Business Logic（业务逻辑）
```

实现三者解耦。

这也是大型游戏配置系统、企业后台、低代码平台、数据平台最终普遍采用的架构方向。
