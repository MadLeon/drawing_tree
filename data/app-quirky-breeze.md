# 数据库开发策略与实施计划

## Context

前端功能已基本完成，使用硬编码 mock 数据（`PoTreeService.cs`）。现阶段目标是将应用连接到真实 SQLite 数据库（`data/record.db`，55MB，141,548 条记录），实现生产级数据交互。当前 C# 项目中**尚无任何数据库访问代码**（.csproj 无 SQLite 依赖）。

---

## 推荐开发策略

### 1. 用哪个数据库进行开发？

**结论：直接使用现有的 `data/record.db`（生产库的副本），不需要另建小型测试库。**

理由：
- 数据库高度关系型（job → order_item → part → part_tree → drawing_file），截取子集会破坏 FK 完整性，制作复杂且容易出错。
- 55MB / 141K 条记录对 SQLite 查询来说完全在毫秒级，不会有性能问题。
- 数据真实性直接暴露边界条件（空值、多版本 part 链、孤立记录等）。

**防护措施（每次开发前备份）：**
```powershell
Copy-Item data/record.db "data/record.db.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
```

---

### 2. SQL 开发工作流：DBeaver + 脚本文件结合

**推荐两阶段工作流：**

#### 阶段 A：DBeaver 交互式调试（写 SQL 的阶段）
- 在 DBeaver 中连接 `data/record.db`
- 用交互方式快速迭代 SQL：测试 JOIN、检验数据形状、确认边界条件
- 利用 DBeaver 的自动补全、结果预览、执行计划分析
- **不在 DBeaver 里做最终版本管理**

#### 阶段 B：脚本文件固化（版本控制的阶段）
- 每条经过验证的查询保存到 `db/queries/*.sql` 文件
- SQL 文件作为"参考规格"，是 C# Repository 实现的依据
- 好处：查询有版本历史，CR 时可单独审查 SQL 逻辑

---

### 3. C# 集成路径

#### 步骤 1：添加 NuGet 包
```
Microsoft.Data.Sqlite（或 sqlite-net-pcl）
```
推荐 `Microsoft.Data.Sqlite`——微软官方维护，ADO.NET 标准接口，无 ORM 魔法。

#### 步骤 2：新建数据访问层
```
src/DrawingTree/
└── Data/
    ├── DatabaseConnectionFactory.cs   ← 管理连接字符串（dev/prod 切换）
    ├── PoRepository.cs                ← 替换 PoTreeService 中的 mock
    └── DrawingRepository.cs           ← 图纸查询
```

#### 步骤 3：按功能模块逐一替换 mock
优先级：
1. `PoTreeService.GetGroupsForPo()` → 查询 purchase_order + job + order_item + part
2. DrawingViewerControl 的图纸搜索 → 查询 drawing_file + part

---

### 4. 关键查询参考（DBeaver 阶段的起点）

**PO → 树节点的核心查询：**
```sql
SELECT
    j.job_number,
    oi.line_number,
    p.drawing_number,
    p.revision,
    p.is_assembly,
    df.file_path
FROM purchase_order po
JOIN job j ON j.po_id = po.id
JOIN order_item oi ON oi.job_id = j.id
JOIN part p ON p.id = oi.part_id
LEFT JOIN drawing_file df ON df.part_id = p.id AND df.is_active = 1
WHERE po.po_number = @poNumber
ORDER BY j.job_number, oi.line_number;
```

**图纸文件搜索：**
```sql
SELECT p.drawing_number, p.revision, df.file_path, df.file_name
FROM drawing_file df
JOIN part p ON p.id = df.part_id
WHERE df.is_active = 1
  AND (p.drawing_number LIKE @keyword OR df.file_name LIKE @keyword)
LIMIT 100;
```

---

## 验证方法

1. DBeaver 中执行上述 SQL，确认结果集与 mock 数据结构一致
2. 添加 NuGet 包后项目能正常编译
3. 运行应用，输入 PO 号 `RT79-87630-PN-R005`（原 mock 中的 PO），树能正确渲染
4. 与 mock 结果对比，确认返回的 job/line/drawing_number 一致

---

## 文件变更范围

| 文件 | 操作 |
|------|------|
| `DrawingTree.csproj` | 添加 Microsoft.Data.Sqlite 包引用 |
| `src/DrawingTree/Data/DatabaseConnectionFactory.cs` | 新建 |
| `src/DrawingTree/Data/PoRepository.cs` | 新建，替换 mock 逻辑 |
| `src/DrawingTree/Services/PoTreeService.cs` | 修改，调用 PoRepository |
| `db/queries/po_tree.sql` | 新建，保存验证过的 SQL |
