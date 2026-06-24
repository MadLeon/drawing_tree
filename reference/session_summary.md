# Project Session Summary

## Last Updated
2026-06-24 | 新增 purchase_order.is_active 全量同步脚本并修复 All POs 界面过滤

## Current Status
- `scripts/update_po_is_active.py` 可重用脚本已完成：从 Excel OE 日志 AA 列读取活跃 order_item ID，全量同步 `purchase_order.is_active`（默认 dry-run，`--apply` 写入）
- 开发库已执行同步：134 个 PO 活跃，142 个 PO 设为 inactive
- `PoRepository.GetAllPoLines()` 已加 `WHERE po.is_active = 1`，All POs 界面只显示活跃记录
- `GetDrawingInfo(string drawingNumber)` 按 `revision DESC` 排序，优先返回最高 revision
- 保存 PartEditor 行时自动将 PO 下引用旧 part 的 order_item 重定向到新 part

## Recent Sessions

2026-06-24 - 新增 purchase_order.is_active 全量同步脚本并修复 All POs 界面过滤
- 新建 `scripts/update_po_is_active.py`：`openpyxl` 只读读取 Excel AA 列，temp table JOIN 解析 PO，事务写入 `is_active`；默认 dry-run，`--apply` 才执行
- 使用 `os.environ["USERPROFILE"]` 构建路径，不硬编码用户名
- 18 个单元测试（`scripts/test_update_po_is_active.py`）全部通过，使用 in-memory SQLite + mock openpyxl
- 修复 `apply_changes` 中 "cannot start a transaction within a transaction" 错误：连接改用 `isolation_level=None`
- `PoRepository.GetAllPoLines()` 加 `WHERE po.is_active = 1`，All POs 界面只显示活跃 PO

2026-06-24 - 修复 PartEditor revision 选取逻辑并实现 PO order_item 自动重定向
- 发现 `GetDrawingInfo(string)` 无 ORDER BY，LIMIT 1 随机返回任一 revision，可能选中占位版本 rev="-"
- 在查询中加 `ORDER BY p.revision DESC`，使真实 revision（ASCII 值高于 "-"）优先
- 新增 `DrawingRepository.RedirectPoOrderItems(poNumber, drawingNumber, targetPartId)`，将 PO 下引用旧 part 的 order_item 批量更新至新 part
- 在 `PartEditorControl.SaveRow()` 的 insert/update 两条成功路径中调用上述方法

2026-06-20 - 修复 PartEditor 保存逻辑并分析 drawing_number 大小写重复问题
- `PartEditorRow.PartId` 从 `init` 改为 `set`（INotifyPropertyChanged），支持创建后赋值
- `PartEditorControl.SaveRow()` 新增自动创建逻辑：若 PartId 为 null，调用 `InsertPart()` + `UpdatePart()` + `UpsertDrawingFile()` 新建记录
- 排查 RT-87000-71200-1004-1-DD-B 数据不更新问题，发现根本原因：SQLite TEXT UNIQUE 约束默认 BINARY 排序，大小写不同视为不同记录，导致 170+ 组重复
- 制定三步修复方案：① SQL 数据合并（保留大写，迁移 FK）→ ② Schema 迁移（加 COLLATE NOCASE）→ ③ InsertPart 代码加 ToUpperInvariant

## Key Decisions
- drawing_number 大小写问题修复顺序：先清理数据再改 Schema，因为 NOCASE 约束会拒绝已有重复数据的导入
- PartEditor 创建新 part 时不弹 ConfirmOverwriteDialog，新建流程直接保存后返回
- InsertPart() 已存在于 DrawingRepository.cs 但原先从未被调用，本次直接接入而非重写
- revision 占位版本（rev="-"）与真实 revision 并存时，加载优先选最高 revision（ORDER BY revision DESC）；保存时重定向 order_item 而非批量删除占位版本
