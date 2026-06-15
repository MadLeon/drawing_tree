# TreeBuilderControl 功能分析

## 活动图

```mermaid
flowchart TD
    START([用户打开 import.json])
    START --> LFJ

    LFJ["LoadFromJsonFile()\n解析 JSON, 填充左栏 _leftDrawings\n记录 _poName"]
    LFJ --> OVERLAY["显示 Loading 遮罩"]

    OVERLAY --> DB1[("① GetGroupsForPo\nSELECT purchase_order, job,\norder_item, part\nWHERE po_number = @po")]

    DB1 --> CHK1{有根节点组?}
    CHK1 -- 否 --> DONE_LOAD(["关闭遮罩, 加载结束"])
    CHK1 -- 是 --> DB2

    DB2[("② GetDrawingInfo x N\n为每条左栏图纸查 part + drawing_file\n补全 PartId, Revision, Description, PdfPath")]
    DB2 --> LOOP["foreach group in groups"]

    LOOP --> DB3[("③ GetPartTree\nCTE 递归查 part_tree\n返回该根节点的已保存子树")]

    DB3 --> SETUP["SetupRootNodeFromGroup()\n在左栏匹配 DrawingNumber\n从左栏移除, 创建根 DrawingNode"]

    SETUP --> CHK_CHILD{有 DB 子节点?}
    CHK_CHILD -- 否 --> ADD_ROOT
    CHK_CHILD -- 是 --> ATTACH

    ATTACH["AttachDbChildren() 递归\n有匹配: 从左栏移除, 挂载到树\n无匹配: WARNING 日志, 跳过"]
    ATTACH --> ADD_ROOT

    ADD_ROOT["_rootNodes.Add(rootNode)\n树视图刷新"]
    ADD_ROOT --> CHK_LOOP{还有 group?}
    CHK_LOOP -- 是 --> LOOP
    CHK_LOOP -- 否 --> CLOSE["关闭 Loading 遮罩"]
    CLOSE --> IDLE

    IDLE["用户操作"]

    IDLE --> SEL["点击左栏 / 树节点\nInfo 面板填充数据"]
    SEL --> IDLE

    IDLE --> DRAG_L["左栏拖拽到树节点\n新建 DrawingNode 挂载\n_hasUnsavedChanges=true"]
    DRAG_L --> IDLE

    IDLE --> DRAG_T["树内拖拽换父节点\n从原父移除, 加入新父\n_hasUnsavedChanges=true"]
    DRAG_T --> IDLE

    IDLE --> REMOVE["Remove 按钮\n从树移除, 归还左栏并排序\n_hasUnsavedChanges=true"]
    REMOVE --> IDLE

    IDLE --> EDIT["修改 Info 面板字段\n_hasUnsavedChanges=true"]
    EDIT --> IDLE

    IDLE --> BROWSE["Browse 按钮\n文件对话框, 更新 InfoFilePath"]
    BROWSE --> IDLE

    IDLE --> ISAVE["点击 Info Save 按钮"]
    ISAVE --> CHK_PID{PartId 已绑定?}
    CHK_PID -- 否 --> ERR1(["显示错误: 未链接数据库"])

    CHK_PID -- 是 --> DB4[("④ UpdatePart\nUPDATE part\nSET revision, description, is_assembly")]

    DB4 --> CHK_PART{写入成功?}
    CHK_PART -- 否 --> ERR2(["显示错误提示"])
    CHK_PART -- 是 --> CHK_FP{InfoFilePath 非空?}
    CHK_FP -- 否 --> UPD_MEM

    CHK_FP -- 是 --> DB5[("⑤ UpsertDrawingFile 事务\nA: UPDATE drawing_file SET is_active=0\nB: INSERT INTO drawing_file\nON CONFLICT DO UPDATE")]

    DB5 --> CHK_FILE{写入成功?}
    CHK_FILE -- 否 --> ERR3(["显示错误提示"])
    CHK_FILE -- 是 --> UPD_MEM["更新内存 _selectedDrawing"]
    UPD_MEM --> IDLE

    IDLE --> TSAVE["点击 Toolbar Save 按钮"]
    TSAVE --> DB6_LOOP["SaveTree() 递归遍历 _rootNodes"]

    DB6_LOOP --> DB_ORPHAN[("⑨ CheckOrphanedEdges\nSELECT part_tree WHERE parent_id\n对比当前树 child_id 集合")]
    DB_ORPHAN --> ORPHAN{发现孤立边?}
    ORPHAN -- 是 --> WARN(["WARNING 日志, 不删除"])
    ORPHAN -- 否 --> CHK_PT
    WARN --> CHK_PT

    CHK_PT{子节点 PartTreeId 存在?}
    CHK_PT -- 是 --> DB7[("⑥ UPDATE part_tree\nSET quantity WHERE quantity 变化")]
    CHK_PT -- 否 --> DB8[("⑦ INSERT INTO part_tree\n回写 PartTreeId 到节点")]
    DB8 --> DB9[("⑧ UPDATE part\nSET has_parent=1")]
    DB9 --> RECURSE
    DB7 --> RECURSE

    RECURSE{还有子节点?}
    RECURSE -- 是 --> DB6_LOOP
    RECURSE -- 否 --> SDONE["_hasUnsavedChanges = false"]
    SDONE --> IDLE

    IDLE --> RET["点击 Return 按钮"]
    RET --> CHK_US{_hasUnsavedChanges?}
    CHK_US -- 否 --> EVT(["触发 ReturnRequested 事件"])
    CHK_US -- 是 --> CONFIRM{用户确认放弃?}
    CONFIRM -- 否 --> IDLE
    CONFIRM -- 是 --> EVT

    classDef db fill:#dbeafe,stroke:#2563eb,color:#1e40af
    classDef decision fill:#fef9c3,stroke:#ca8a04
    class DB1,DB2,DB3,DB4,DB5,DB6_LOOP,DB7,DB8,DB9,DB_ORPHAN db
    class CHK1,CHK_LOOP,CHK_CHILD,CHK_PID,CHK_PART,CHK_FP,CHK_FILE,CHK_PT,RECURSE,ORPHAN,CHK_US,CONFIRM decision
```

---

## 时序图

```mermaid
sequenceDiagram
    actor User
    participant TBC as TreeBuilderControl
    participant PR as PoRepository
    participant DR as DrawingRepository
    participant DB as SQLite DB

    User->>TBC: 打开 import.json
    TBC->>TBC: LoadFromJsonFile() 解析 JSON 填充左栏
    TBC->>TBC: 显示 Loading 遮罩

    TBC->>+PR: GetGroupsForPo(poName)
    PR->>+DB: SELECT purchase_order join job join order_item join part WHERE po_number=@po
    DB-->>-PR: rows (job, line, drawing_number, part_id)
    PR-->>-TBC: List of PoTreeGroup

    alt 无根节点组
        TBC->>TBC: 关闭遮罩, 加载结束
    else 有根节点组
        loop 左栏每条图纸
            TBC->>+DR: GetDrawingInfo(drawingNumber)
            DR->>+DB: SELECT part + drawing_file WHERE drawing_number=@dn LIMIT 1
            DB-->>-DR: part_id, revision, description, file_path
            DR-->>-TBC: DrawingInfo (含 PartId)
            TBC->>TBC: 补全 _leftDrawings 元数据
        end

        loop 每个 PoTreeGroup
            TBC->>+PR: GetPartTree(group.PartId)
            PR->>+DB: WITH RECURSIVE tree CTE 递归查 part_tree + part + drawing_file
            DB-->>-PR: 子树行集 (part_tree_id, part_id, drawing_number, parent_part_id)
            PR-->>-TBC: List of DrawingNode (内存中已组装层级)
            TBC->>TBC: SetupRootNodeFromGroup() 从左栏移除根节点 创建根 DrawingNode
            TBC->>TBC: AttachDbChildren() 递归 匹配左栏则移除并挂载 无匹配则 WARNING
        end

        TBC->>TBC: 关闭 Loading 遮罩
    end

    Note over User,TBC: 用户拖拽 / 移除 / 编辑 Info 面板 (纯内存操作 不访问 DB)

    User->>TBC: 点击 Info Save 按钮
    TBC->>+DR: UpdatePart(partId, revision, description, isAssembly)
    DR->>+DB: UPDATE part SET revision/description/is_assembly/updated_at WHERE id=@partId
    DB-->>-DR: OK
    DR-->>-TBC: true

    opt InfoFilePath 非空
        TBC->>+DR: UpsertDrawingFile(partId, fileName, filePath, revision)
        DR->>+DB: BEGIN TRANSACTION
        DR->>DB: UPDATE drawing_file SET is_active=0 WHERE part_id=@partId
        DR->>DB: INSERT INTO drawing_file ON CONFLICT file_path DO UPDATE
        DR->>DB: COMMIT
        DB-->>-DR: OK
        DR-->>-TBC: true
        TBC->>TBC: 更新内存 _selectedDrawing
    end

    User->>TBC: 点击 Toolbar Save 按钮
    TBC->>+DR: SaveTree(_rootNodes)
    DR->>+DB: BEGIN TRANSACTION

    loop 递归遍历每个父节点的子边
        DR->>DB: SELECT part_tree WHERE parent_id=@pid (CheckOrphanedEdges)
        DB-->>DR: DB 已有边列表
        DR->>DR: 对比当前树 childIds 孤立边则写 WARNING 日志

        alt 子节点 PartTreeId 已存在 (已有边)
            DR->>DB: UPDATE part_tree SET quantity/updated_at WHERE id=@partTreeId AND quantity!=@qty
        else 新边
            DR->>DB: INSERT INTO part_tree (parent_id, child_id, quantity)
            DB-->>DR: last_insert_rowid 回写 PartTreeId
            DR->>DB: UPDATE part SET has_parent=1 WHERE id=@childId
        end
    end

    DR->>DB: COMMIT
    DB-->>-DR: OK
    DR-->>-TBC: 完成
    TBC->>TBC: _hasUnsavedChanges = false
```

---

## 数据库交互汇总

| # | 触发时机 | 方法 | 表操作 |
|---|---------|------|--------|
| ① | 打开 import.json | `GetGroupsForPo` | SELECT purchase_order / job / order_item / part |
| ② | 同上，为左栏每张图纸逐条查询 | `GetDrawingInfo × N` | SELECT part + drawing_file |
| ③ | 每个根节点各查一次 | `GetPartTree` | SELECT part_tree（CTE 递归）+ part + drawing_file |
| ④ | 点击 Info Save | `UpdatePart` | UPDATE part |
| ⑤ | 点击 Info Save 且有文件路径 | `UpsertDrawingFile` | UPDATE drawing_file（is_active=0）+ INSERT/UPDATE drawing_file（事务）|
| ⑥ | 点击 Toolbar Save，已有边 | `SaveTree` 内部 | UPDATE part_tree（quantity 变化时）|
| ⑦ | 点击 Toolbar Save，新边 | `SaveTree` 内部 | INSERT part_tree |
| ⑧ | 同⑦，随新边一起写 | `SaveTree` 内部 | UPDATE part SET has_parent=1 |
| ⑨ | Toolbar Save 处理每个父节点时 | `CheckOrphanedEdges` | SELECT part_tree（孤立边检测，只读）|
