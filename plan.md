# drawing_number 大小写重复问题：根因分析与修复方案

---

## 一、问题现象

用户在树构筑界面看到 `RT-87000-71200-1004-1-DD-B`，输入信息后数据没有更新到界面。

---

## 二、根因分析

### 数据库中存在两条重复记录

```
id=3452  RT-87000-71200-1004-1-DD-B (大写)  rev=0   has_parent=NULL  → 有 drawing_file
id=4791  rt-87000-71200-1004-1-dd-b (小写)  rev=-   has_parent=1     → 在 part_tree 中
```

- `drawing_file` 表的记录绑定在 **id=3452**（有效文件）
- `part_tree` 树结构绑定的是 **id=4791** 作为 `id=3451`（RT-87000-71200-1000-1-GA-E）的子件，quantity=2

### 两者数据互不相通

- 树构筑界面通过 `part_tree` → `GetPartTree()` 加载 **id=4791**，显示 `revision=-`、`description=AXIAL WEAR RING`
- PartEditor 通过 `GetDrawingInfo("RT-...")` 大小写敏感匹配找到 **id=3452**，显示 `revision=0`、`description=Bellows Replacement Tool...`
- 用户在任一界面保存，只会更新各自绑定的记录，另一条不受影响

### 设计层面的根本原因

`part` 表的约束：

```sql
UNIQUE(drawing_number, revision)
```

SQLite 的 TEXT 字段默认使用 **BINARY 排序规则**，`RT-xxx` 与 `rt-xxx` 被视为不同值，UNIQUE 约束不拦截，导致大小写不同的同一图纸号可以共存。

### 问题规模

查询发现数据库中存在 **170+ 组**大小写重复对，均为大写与小写版本各一条，例如：

```
sample: 4 条 (3598=SAMPLE | 3599=Sample | 5333=Sample | 3605=sample)
rt-87640-72150-*: 约 40 组
rt-87000-71200-*: 约 10 组
rt-87000-71225-*: 约 37 组
59rt-79112-*:     约 35 组
...
```

这是系统性问题，单次导入时图纸号大小写不一致（PDF 文件名小写 vs 工程图纸编号大写）导致批量重复插入。

---

## 三、修复方案

三层修复，**必须按顺序执行**。

---

### Step 1：数据清理（先于 Schema 变更执行）

**策略**：每对重复中，保留大写版本（`drawing_number = UPPER(drawing_number)`），将小写版本的所有外键引用合并过来，再删除小写记录。

```sql
-- 先备份！
-- Copy-Item data/record.db "data/record.db.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"

BEGIN;

-- 1. 重定向 part_tree.child_id（小写 → 大写）
UPDATE part_tree
SET child_id = canonical.id
FROM (
    SELECT p_upper.id AS id, p_lower.id AS dup_id
    FROM part p_upper
    JOIN part p_lower
      ON LOWER(p_upper.drawing_number) = LOWER(p_lower.drawing_number)
     AND p_upper.revision = p_lower.revision
     AND p_upper.drawing_number = UPPER(p_upper.drawing_number)
     AND p_lower.drawing_number != UPPER(p_lower.drawing_number)
) AS canonical
WHERE part_tree.child_id = canonical.dup_id;

-- 2. 重定向 part_tree.parent_id（小写 → 大写）
UPDATE part_tree
SET parent_id = canonical.id
FROM (
    SELECT p_upper.id AS id, p_lower.id AS dup_id
    FROM part p_upper
    JOIN part p_lower
      ON LOWER(p_upper.drawing_number) = LOWER(p_lower.drawing_number)
     AND p_upper.revision = p_lower.revision
     AND p_upper.drawing_number = UPPER(p_upper.drawing_number)
     AND p_lower.drawing_number != UPPER(p_lower.drawing_number)
) AS canonical
WHERE part_tree.parent_id = canonical.dup_id;

-- 3. 重定向 drawing_file.part_id（仅当大写版本没有 active file 时）
UPDATE drawing_file
SET part_id = canonical.id
FROM (
    SELECT p_upper.id AS id, p_lower.id AS dup_id
    FROM part p_upper
    JOIN part p_lower
      ON LOWER(p_upper.drawing_number) = LOWER(p_lower.drawing_number)
     AND p_upper.revision = p_lower.revision
     AND p_upper.drawing_number = UPPER(p_upper.drawing_number)
     AND p_lower.drawing_number != UPPER(p_lower.drawing_number)
    WHERE NOT EXISTS (
        SELECT 1 FROM drawing_file df2
        WHERE df2.part_id = p_upper.id AND df2.is_active = 1
    )
) AS canonical
WHERE drawing_file.part_id = canonical.dup_id;

-- 4. 同步大写版本的 has_parent 标记
UPDATE part
SET has_parent = 1
WHERE drawing_number = UPPER(drawing_number)
  AND EXISTS (
      SELECT 1 FROM part p_lower
      WHERE LOWER(p_lower.drawing_number) = LOWER(part.drawing_number)
        AND p_lower.revision = part.revision
        AND p_lower.drawing_number != UPPER(p_lower.drawing_number)
        AND p_lower.has_parent = 1
  );

-- 5. 删除小写重复记录
DELETE FROM part
WHERE drawing_number != UPPER(drawing_number)
  AND EXISTS (
      SELECT 1 FROM part p2
      WHERE LOWER(p2.drawing_number) = LOWER(part.drawing_number)
        AND p2.revision = part.revision
        AND p2.drawing_number = UPPER(p2.drawing_number)
  );

COMMIT;

-- 验证：应返回 0 行
SELECT LOWER(drawing_number), revision, COUNT(*)
FROM part
GROUP BY LOWER(drawing_number), revision
HAVING COUNT(*) > 1;
```

> **注意**：`sample`/`sketch`/`NPN` 等无大写对应版本的特殊词不会被删除，可单独处理或保留。

---

### Step 2：Schema 迁移（数据清理完成后执行）

在 `drawing_number` 列加入 `COLLATE NOCASE`，使 UNIQUE 约束自动变为大小写不敏感。

SQLite 不支持直接修改列定义，需重建表：

```sql
BEGIN;

CREATE TABLE part_new (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    previous_id INTEGER,
    next_id INTEGER,
    drawing_number TEXT NOT NULL COLLATE NOCASE,
    revision TEXT NOT NULL DEFAULT '-',
    description TEXT,
    is_assembly INTEGER DEFAULT 0,
    production_count INTEGER DEFAULT 0,
    total_production_hour REAL DEFAULT 0,
    total_administrative_hour REAL DEFAULT 0,
    unit_price REAL DEFAULT 0,
    created_at TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
    updated_at TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
    has_parent INTEGER,
    UNIQUE(drawing_number, revision),
    FOREIGN KEY (previous_id) REFERENCES part_new(id) ON DELETE SET NULL,
    FOREIGN KEY (next_id) REFERENCES part_new(id) ON DELETE SET NULL
);

INSERT INTO part_new SELECT * FROM part;

DROP TABLE part;
ALTER TABLE part_new RENAME TO part;

CREATE INDEX IF NOT EXISTS idx_part_drawing_number ON part(drawing_number);

COMMIT;
```

加入 `COLLATE NOCASE` 后，`INSERT INTO part (drawing_number='rt-xxx', revision='0')` 会因 UNIQUE 约束冲突而失败（若已存在 `RT-xxx` rev=0），从根本上杜绝重复。

---

### Step 3：代码修复（防御纵深）

`DrawingRepository.cs` 的 `InsertPart()` 方法中对 `drawingNumber` 强制大写，防止大小写不规范的输入写入数据库：

```csharp
// DrawingRepository.cs:131
cmd.Parameters.AddWithValue("@dn", drawingNumber.ToUpperInvariant());
```

与 Schema 的 `COLLATE NOCASE` 形成双重保护：COLLATE 防止重复存在，代码规范化确保存储格式一致。

---

## 四、执行顺序

1. **备份数据库**（PowerShell）
2. **DBeaver 执行 Step 1**，确认验证查询返回 0 行
3. **DBeaver 执行 Step 2**（Schema 迁移）
4. **将 Step 2 SQL 追加到 `data/db_changes.sql`**
5. **修改代码 Step 3**（InsertPart 加 ToUpperInvariant）

---

## 五、验证

```sql
-- 无重复
SELECT LOWER(drawing_number), revision, COUNT(*)
FROM part GROUP BY LOWER(drawing_number), revision HAVING COUNT(*) > 1;

-- part_tree FK 完整性（不应有孤立引用）
SELECT pt.id FROM part_tree pt
LEFT JOIN part p ON p.id = pt.child_id WHERE p.id IS NULL;

-- drawing_file FK 完整性
SELECT df.id FROM drawing_file df
LEFT JOIN part p ON p.id = df.part_id WHERE p.id IS NULL;
```

---

## 六、当前具体问题的临时处理

在执行全量清理前，如需立刻恢复 `RT-87000-71200-1004-1-DD-B` 的树构筑界面功能，可单独执行：

```sql
UPDATE part_tree SET child_id = 3452 WHERE child_id = 4791;
UPDATE part SET has_parent = 1 WHERE id = 3452;
DELETE FROM part WHERE id = 4791;
```
