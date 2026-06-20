# Project Session Summary

## Last Updated
2026-06-20 | 修复 PartEditor 保存逻辑并分析 drawing_number 大小写重复问题

## Current Status
- PartEditor 现可在保存时自动新建不存在的 part 记录及对应 drawing_file
- 数据库中存在 170+ 组大小写重复的 part 记录，需执行 data cleanup + schema 迁移修复
- `PartEditorRow.PartId` 已改为可变属性，支持创建后同行二次保存

## Recent Sessions

2026-06-20 - 修复 PartEditor 保存逻辑并分析 drawing_number 大小写重复问题
- `PartEditorRow.PartId` 从 `init` 改为 `set`（INotifyPropertyChanged），支持创建后赋值
- `PartEditorControl.SaveRow()` 新增自动创建逻辑：若 PartId 为 null，调用 `InsertPart()` + `UpdatePart()` + `UpsertDrawingFile()` 新建记录
- 排查 RT-87000-71200-1004-1-DD-B 数据不更新问题，发现根本原因：SQLite TEXT UNIQUE 约束默认 BINARY 排序，大小写不同视为不同记录，导致 170+ 组重复
- 制定三步修复方案：① SQL 数据合并（保留大写，迁移 FK）→ ② Schema 迁移（加 COLLATE NOCASE）→ ③ InsertPart 代码加 ToUpperInvariant

## Key Decisions
- drawing_number 大小写问题修复顺序：先清理数据再改 Schema，因为 NOCASE 约束会拒绝已有重复数据的导入
- PartEditor 创建新 part 时不弹 ConfirmOverwriteDialog，新建流程直接保存后返回
- InsertPart() 已存在于 DrawingRepository.cs 但原先从未被调用，本次直接接入而非重写
