# Data Flow: Input Data → Edit Part → Tree Builder

## 概览

三个界面以 `imports/{PO}_import.json` 文件路径作为唯一媒介串联，界面间不传对象。

---

## JSON 文件的两个职责

1. **文件系统扫描结果持久化**：DrawingEditor 扫描到的 PDF 路径和文件名在数据库中没有对应结构，JSON 是唯一存储
2. **断点续传**：用户可跳过 DrawingEditor，直接从 PartEditor 加载 JSON 继续工作

JSON 结构（字段：`DrawingNumber`、`PdfPath`、`FileName`）由 `DrawingEditorControl.ExportToJson()`（~304 行）生成。

---

## 界面间数据流

```
DrawingEditor  ---(JSON路径)--->  MainWindow.OnDrawingEditorImportCompleted
                                  → 创建 PartEditorControl，载入 JSON

PartEditor 保存完成 ---(同一JSON路径)--->  MainWindow.OnPartEditorSaveAllCompleted
                                           → 创建 TreeBuilderControl，载入 JSON
```

- **PartEditor**（`LoadFromJsonFile()` ~47行）：读 JSON 拿图纸编号，再查数据库补全 `Revision`、`Description`、`PartId` 后显示
- **TreeBuilder**（`LoadFromJsonFile()` ~88行）：同样读 JSON 建骨架，`LoadFromDatabaseAsync()`（~136行）补全元数据，`AttachDbChildren()` 将已入树图纸移至右侧面板

---

## 数据模型

- **`DrawingInfo`**：`DrawingNumber`、`PdfPath`、`Revision`、`Description`、`PartId`、`IsAssembly`
- **`DrawingNode`**：包装 `DrawingInfo`，支持父子关系和拖拽

---

## 潜在优化方向

**TreeBuilder 可以脱离 JSON 文件**，改为只接收 `poId`，通过数据库级联查询重建左侧图纸列表：

```
part → order_item → job → purchase_order
```

PartEditor 保存后，所有 part 已关联到 order_item，TreeBuilder 可直接从数据库取到完整列表。

**前提**：PartEditor 保存时须将 PDF 路径也写入数据库（如 `drawing_file` 表），否则 TreeBuilder 查到的数据会缺少路径，点击打开 PDF 会失效。

> JSON 文件的范围应收窄为 DrawingEditor ↔ PartEditor，TreeBuilder 改为纯 DB 查询。
