# 数据库表总结

## 概览

| 指标     | 数值    |
| -------- | ------- |
| 总表数   | 20      |
| 总记录数 | 141,548 |
| 已填充表 | 10      |
| 空表     | 10      |

---

## 所有表清单

| 表名             | 记录数  | 字段数 | 状态 |
| ---------------- | ------- | ------ | ---- |
| attachment_note  | 0       | 6      | ⭘ 空 |
| customer         | 79      | 6      | ✓    |
| customer_contact | 69      | 8      | ✓    |
| drawing_file     | 137,399 | 9      | ✓    |
| folder_mapping   | 0       | 6      | ⭘ 空 |
| job              | 339     | 6      | ✓    |
| job_note         | 0       | 6      | ⭘ 空 |
| order_item       | 358     | 13     | ✓    |
| order_item_note  | 0       | 6      | ⭘ 空 |
| part             | 1,657   | 13     | ✓    |
| part_attachment  | 0       | 10     | ⭘ 空 |
| part_note        | 0       | 6      | ⭘ 空 |
| part_tree        | 1,460   | 6      | ✓    |
| po_note          | 0       | 6      | ⭘ 空 |
| process_template | 0       | 8      | ⭘ 空 |
| purchase_order   | 172     | 8      | ✓    |
| shipment         | 5       | 6      | ✓    |
| shipment_item    | 10      | 6      | ✓    |
| shipment_note    | 0       | 6      | ⭘ 空 |
| step_tracker     | 0       | 10     | ⭘ 空 |

---

## 已填充表详情

### customer - 79 条记录

```
字段:
  • id(INTEGER)
  • customer_name(TEXT)
  • usage_count(INTEGER)
  • last_used(TEXT)
  • created_at(TEXT)
  ... 及 1 个字段
```

### customer_contact - 69 条记录

```
字段:
  • id(INTEGER)
  • customer_id(INTEGER)
  • contact_name(TEXT)
  • contact_email(TEXT)
  • usage_count(INTEGER)
  ... 及 3 个字段
```

### drawing_file - 137,399 条记录

```
字段:
  • id(INTEGER)
  • part_id(INTEGER)
  • file_name(TEXT)
  • file_path(TEXT)
  • is_active(INTEGER)
  ... 及 4 个字段
```

### job - 339 条记录

```
字段:
  • id(INTEGER)
  • job_number(TEXT)
  • po_id(INTEGER)
  • priority(TEXT)
  • created_at(TEXT)
  ... 及 1 个字段
```

### order_item - 358 条记录

```
字段:
  • id(INTEGER)
  • job_id(INTEGER)
  • part_id(INTEGER)
  • line_number(INTEGER)
  • quantity(INTEGER)
  ... 及 8 个字段
```

### part - 1,657 条记录

```
字段:
  • id(INTEGER)
  • previous_id(INTEGER)
  • next_id(INTEGER)
  • drawing_number(TEXT)
  • revision(TEXT)
  ... 及 8 个字段
```

### part_tree - 1,460 条记录

```
字段:
  • id(INTEGER)
  • parent_id(INTEGER)
  • child_id(INTEGER)
  • quantity(INTEGER)
  • created_at(TEXT)
  ... 及 1 个字段
```

### purchase_order - 172 条记录

```
字段:
  • id(INTEGER)
  • po_number(TEXT)
  • oe_number(TEXT)
  • contact_id(INTEGER)
  • is_active(INTEGER)
  ... 及 3 个字段
```

### shipment - 5 条记录

```
字段:
  • id(INTEGER)
  • packing_slip_number(TEXT)
  • invoice_number(TEXT)
  • delivery_shipped_date(TEXT)
  • created_at(TEXT)
  ... 及 1 个字段
```

### shipment_item - 10 条记录

```
字段:
  • id(INTEGER)
  • order_item_id(INTEGER)
  • shipment_id(INTEGER)
  • quantity(INTEGER)
  • created_at(TEXT)
  ... 及 1 个字段
```

---

## 数据分布

### 按记录数排序（前 10）

1. **drawing_file** - 137,399 条 (97.2% 的总记录)
2. **part** - 1,657 条
3. **part_tree** - 1,460 条
4. **job** - 339 条
5. **order_item** - 358 条
6. **purchase_order** - 172 条
7. **customer** - 79 条
8. **customer_contact** - 69 条
9. **shipment** - 5 条
10. **shipment_item** - 10 条

---

## 备注

- **空表** (10 个): 这些表通常用于特定功能（备注、附件、模板），在当前阶段尚未使用
- **drawing_file** 是最大的表，包含 137,399 条图纸文件记录
- 完整的字段定义见 [schema-reference.md](schema-reference.md)
- 使用 `npm run db:check` 可获取最新数据库统计信息
