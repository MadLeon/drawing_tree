###### **customer**

id (PK)
customer_name
usage_count
last_used
created_at
updated_at

```sql

CREATE TABLE customer (
	id							INTEGER		PRIMARY KEY AUTOINCREMENT,
	customer_name		TEXT			NOT NULL,				-- 客户名称
	usage_count			INTEGER		DEFAULT 0,			-- 使用频率
	last_used				TEXT,											-- 最后使用时间
	created_at			TEXT			NOT NULL DEFAULT (datetime('now', 'localtime')),
	updated_at			TEXT			NOT NULL DEFAULT (datetime('now', 'localtime'))
);

```

note:

1. update usage_count with updated_at

###### **customer_contact**

id (PK)
customer_id (FK -> customer.id)
contact_name
contact_email
usage_count
last_used
created_at
updated_at

```sql

CREATE TABLE customer_contact (
	id							INTEGER		PRIMARY KEY AUTOINCREMENT,
	customer_id			INTEGER		NOT NULL,				-- 关联 customer
	contact_name		TEXT			NOT NULL,				-- 联系人名称
	contact_email		TEXT,											-- 邮箱地址
	usage_count			INTEGER		DEFAULT 0,			-- 使用频率
	last_used				TEXT,											-- 最后使用时间
	created_at			TEXT			NOT NULL DEFAULT (datetime('now', 'localtime')),
	updated_at			TEXT			NOT NULL DEFAULT (datetime('now', 'localtime')),

	-- 外键约束
	FOREIGN KEY (customer_id)		REFERENCES customer(id)		ON DELETE CASCADE
);

```

###### **purchase_order**

id (PK)
po_number
oe_number
contact_id (FK -> customer_contact.id)
is_active
closed_at
created_at
updated_at

```sql

CREATE TABLE purchase_order (
	id							INTEGER		PRIMARY KEY AUTOINCREMENT,
	po_number				TEXT			NOT NULL,				-- 采购订单号
	oe_number				TEXT,											-- 订单号
	contact_id			INTEGER,									-- 关联 customer_contact
	is_active				INTEGER		DEFAULT 1,			-- 1:激活 0:已归档
	closed_at				TEXT,											-- 关闭日期
	created_at			TEXT			NOT NULL DEFAULT (datetime('now', 'localtime')),
	updated_at			TEXT			NOT NULL DEFAULT (datetime('now', 'localtime')),

	-- 外键约束
	FOREIGN KEY (contact_id)		REFERENCES customer_contact(id)
);

```

###### **job**

id (PK)
job_number (UK)
po_id (FK -> purchase_order.id)
priorit
created_at
updated_at

```sql

CREATE TABLE job (
	id							INTEGER		PRIMARY KEY AUTOINCREMENT,
	job_number			TEXT			UNIQUE NOT NULL,		-- 作业号
	po_id						INTEGER		NOT NULL,						-- 关联 purchase_order
	priority				TEXT			DEFAULT 'Normal',		-- 优先级
	created_at			TEXT			NOT NULL DEFAULT (datetime('now', 'localtime')),
	updated_at			TEXT			NOT NULL DEFAULT (datetime('now', 'localtime')),

	-- 外键约束
	FOREIGN KEY (po_id)				REFERENCES purchase_order(id)		ON DELETE CASCADE
);

```

###### **order_item**

id (PK)
job_id (FK -> production_job.id)
part_id (FK ->part.id)
line_number
quantity
actual_price

- _production_hour_
- _administrative_hour_
- _status_
  drawing_release_date
  delivery_required_date
  created_at
  updated_at

```sql

CREATE TABLE order_item (
	id											INTEGER		PRIMARY KEY AUTOINCREMENT,
	job_id									INTEGER		NOT NULL,							-- 关联 job
	part_id									INTEGER,												-- 关联 part (可为空)
	line_number							INTEGER		NOT NULL,							-- 行号
	quantity								INTEGER		NOT NULL DEFAULT 0,		-- 数量
	actual_price						REAL,														-- 实际价格
	production_hour					REAL			DEFAULT 0,						-- 生产小时数
	administrative_hour			REAL			DEFAULT 0,						-- 行政小时数
	status									TEXT			DEFAULT 'PENDING',		-- 状态
	drawing_release_date		TEXT,														-- 图纸发放日期
	delivery_required_date	TEXT,														-- 交付所需日期
	created_at							TEXT			NOT NULL DEFAULT (datetime('now', 'localtime')),
	updated_at							TEXT			NOT NULL DEFAULT (datetime('now', 'localtime')),

	-- 外键约束
	FOREIGN KEY (job_id)			REFERENCES job(id)				ON DELETE CASCADE,
	FOREIGN KEY (part_id)			REFERENCES part(id)
);

```

###### **shipment**

id (PK)
packing_slip_number (unique)
invoice_number
delivery_shipped_date
created_at
updated_at

```sql

CREATE TABLE shipment (
	id										INTEGER		PRIMARY KEY AUTOINCREMENT,
	packing_slip_number		TEXT			UNIQUE NOT NULL,	-- 装箱单号
	invoice_number				TEXT,												-- 发票号
	delivery_shipped_date	TEXT,												-- 发货日期
	created_at						TEXT			NOT NULL DEFAULT (datetime('now', 'localtime')),
	updated_at						TEXT			NOT NULL DEFAULT (datetime('now', 'localtime'))
);

```

###### **shipment_item**

发货明细表
id (PK)
order_item_id (FK -> order_item.id)
shipment_id (FK -> shipment.id)
shipped_quantity
created_at
updated_at

```sql

CREATE TABLE shipment_item (
	id							INTEGER		PRIMARY KEY AUTOINCREMENT,
	order_item_id		INTEGER		NOT NULL,						-- 关联 order_item
	shipment_id			INTEGER		NOT NULL,						-- 关联 shipment
	quantity				INTEGER		NOT NULL DEFAULT 0,	-- 发货数量
	created_at			TEXT			NOT NULL DEFAULT (datetime('now', 'localtime')),
	updated_at			TEXT			NOT NULL DEFAULT (datetime('now', 'localtime')),

	-- 外键约束
	FOREIGN KEY (order_item_id)		REFERENCES order_item(id),
	FOREIGN KEY (shipment_id)			REFERENCES shipment(id)		ON DELETE CASCADE
);

```

###### **part**

id (PK)
previous_id (FK -> part.id)
drawing_number
revision
description
is_assembly

- _production_count_
- _total_production_hour_
- _total_administrative_hour_
  unit_price
  created_at
  updated_at

```sql

CREATE TABLE part (
	id												INTEGER		PRIMARY KEY AUTOINCREMENT,
	previous_id								INTEGER,												-- 前一版本零件ID (版本链)
	next_id										INTEGER,												-- 下一版本零件ID (版本链)
	drawing_number						TEXT			NOT NULL,							-- 图纸号
	revision									TEXT			NOT NULL DEFAULT '-',	-- 版本号
	description								TEXT,														-- 描述
	is_assembly								INTEGER		DEFAULT NULL,					-- 是否为装配体
	has_parent								INTEGER,												-- 是否有父亲 (1:有 NULL:无)
	production_count					INTEGER		DEFAULT 0,						-- 生产数量
	total_production_hour			REAL			DEFAULT 0,						-- 总生产小时数
	total_administrative_hour	REAL			DEFAULT 0,						-- 总行政小时数
	unit_price								REAL			DEFAULT 0,						-- 单价
	created_at								TEXT			NOT NULL DEFAULT (datetime('now', 'localtime')),
	updated_at								TEXT			NOT NULL DEFAULT (datetime('now', 'localtime')),

	-- 约束
	FOREIGN KEY (previous_id)		REFERENCES part(id),
	UNIQUE(drawing_number, revision)
);

```

###### **part_tree**

零件组成关系表 (BOM)
id (PK)
parent_id (FK -> part.id)
child_id (FK -> part.id)
quantity
created_at
updated_at

```sql

CREATE TABLE part_tree (
	id							INTEGER		PRIMARY KEY AUTOINCREMENT,
	parent_id				INTEGER		NOT NULL,				-- 关联 part (父零件)
	child_id				INTEGER		NOT NULL,				-- 关联 part (子零件)
	quantity				INTEGER		DEFAULT 1,			-- 用量
	created_at			TEXT			NOT NULL DEFAULT (datetime('now', 'localtime')),
	updated_at			TEXT			NOT NULL DEFAULT (datetime('now', 'localtime')),

	-- 外键约束
	FOREIGN KEY (parent_id)		REFERENCES part(id),
	FOREIGN KEY (child_id)		REFERENCES part(id)
);

```

###### **part_attachment**

零件附件表 (图纸PDF, 质检报告等)
id (PK)
part_id (FK -> part.id, 可为空)
order_item_id (FK -> order_item.id, 可为空)
file_type
file_name
file_path
is_active
last_modified_at
created_at
updated_at

```sql

CREATE TABLE part_attachment (
	id								INTEGER			PRIMARY KEY AUTOINCREMENT,
	part_id						INTEGER,											-- 关联 part (可为空)
	order_item_id			INTEGER,											-- 关联 order_item (可为空)
	file_type					TEXT				NOT NULL,					-- 文件类型 (INSPECTION/MTR/DEVIATION)
	file_name					TEXT				NOT NULL,					-- 文件名
	file_path					TEXT				NOT NULL UNIQUE,	-- 文件路径
	is_active					INTEGER			DEFAULT 1,				-- 是否活跃
	last_modified_at	TEXT,													-- 最后修改时间
	created_at				TEXT				NOT NULL DEFAULT (datetime('now', 'localtime')),
	updated_at				TEXT				NOT NULL DEFAULT (datetime('now', 'localtime')),

	-- 外键约束
	FOREIGN KEY (part_id)					REFERENCES part(id),
	FOREIGN KEY (order_item_id)		REFERENCES order_item(id)
);

```

note:

1. part_id / order_item_id 都可为空 - 可关联零件或订单
2. 为来自 drawing_file 的特定类型文件 (MTR, 检验报告等)

###### **drawing_file**

图纸文件路径表 (G 盘扫描结果)
id (PK)
part_id (FK -> part.id, 可为空)
file_name
file_path
is_active
last_modified_at
revision
created_at
updated_at

```sql

CREATE TABLE drawing_file (
	id								INTEGER		PRIMARY KEY AUTOINCREMENT,
	part_id						INTEGER,										-- 关联 part (可为空，后期手工匹配)
	file_name					TEXT			NOT NULL,					-- 文件名
	file_path					TEXT			NOT NULL UNIQUE,	-- 文件路径 (G盘路径)
	is_active					INTEGER		DEFAULT 1,				-- 是否活跃
	last_modified_at	TEXT,												-- 最后修改时间
	revision					TEXT			NOT NULL DEFAULT '-',	-- 版本号
	created_at				TEXT			NOT NULL DEFAULT (datetime('now', 'localtime')),
	updated_at				TEXT			NOT NULL DEFAULT (datetime('now', 'localtime')),

	-- 外键约束
	FOREIGN KEY (part_id)				REFERENCES part(id)
);

```

note:

1. part_id 可为空 - G 盘文件扫描导入，后期手工匹配关联
2. 已有 137,399 条扫描记录

###### **folder_mapping**

客户文件夹映射表
id (PK)
customer_id (FK -> customer.id)
folder_name
is_verified
created_at
updated_at

```sql

CREATE TABLE folder_mapping (
	id							INTEGER			PRIMARY KEY AUTOINCREMENT,
	customer_id			INTEGER			NOT NULL,				-- 关联 customer
	folder_name			TEXT				NOT NULL,				-- G盘文件夹名
	is_verified			INTEGER			DEFAULT 0,			-- 是否已验证
	created_at			TEXT				NOT NULL DEFAULT (datetime('now', 'localtime')),
	updated_at			TEXT				NOT NULL DEFAULT (datetime('now', 'localtime')),

	-- 外键约束
	FOREIGN KEY (customer_id)		REFERENCES customer(id)		ON DELETE CASCADE
);

```

###### **process_template**

生产工艺模板表 (从 Excel 抓取)
id (PK)
part_id (FK -> part.id)
row_number
shop_code
description
remark
created_at
updated_at

```sql

CREATE TABLE process_template (
	id							INTEGER		PRIMARY KEY AUTOINCREMENT,
	part_id					INTEGER		NOT NULL,				-- 关联 part
	row_number			INTEGER		NOT NULL,				-- 行号
	shop_code				TEXT			NOT NULL,				-- 工序代码
	description			TEXT,											-- 工序描述
	remark					TEXT,											-- 备注
	created_at			TEXT			NOT NULL DEFAULT (datetime('now', 'localtime')),
	updated_at			TEXT			NOT NULL DEFAULT (datetime('now', 'localtime')),

	-- 外键约束
	FOREIGN KEY (part_id)			REFERENCES part(id)
);

```

###### **step_tracker**

步骤追踪执行表 (条码扫描记录)
id (PK)
order_item_id (FK -> order_item.id)
process_template_id (FK -> process_template.id)
operator_id
machine_id
status
start_time
end_time

```sql

CREATE TABLE step_tracker (
	id										INTEGER			PRIMARY KEY AUTOINCREMENT,
	order_item_id					INTEGER			NOT NULL,						-- 关联 order_item
	process_template_id		INTEGER			NOT NULL,						-- 关联 process_template
	operator_id						TEXT,														-- 操作员ID
	machine_id						TEXT,														-- 机器ID
	status								TEXT				DEFAULT 'PENDING',	-- 状态
	start_time						TEXT,														-- 开始时间
	end_time							TEXT,														-- 结束时间
	created_at						TEXT				NOT NULL DEFAULT (datetime('now', 'localtime')),
	updated_at						TEXT				NOT NULL DEFAULT (datetime('now', 'localtime')),

	-- 外键约束
	FOREIGN KEY (order_item_id)				REFERENCES order_item(id)			ON DELETE CASCADE,
	FOREIGN KEY (process_template_id)	REFERENCES process_template(id)
);

```

###### **po_note**

采购订单备注表
id (PK)
po_id (FK -> purchase_order.id)
content
author
created_at
updated_at

```sql

CREATE TABLE po_note (
	id							INTEGER		PRIMARY KEY AUTOINCREMENT,
	po_id						NTEGER		NOT NULL,				-- 关联 purchase_order
	content					TEXT			NOT NULL,				-- 备注内容
	author					TEXT,											-- 作者
	created_at			TEXT			NOT NULL DEFAULT (datetime('now', 'localtime')),
	updated_at			TEXT			NOT NULL DEFAULT (datetime('now', 'localtime')),

	-- 外键约束
	FOREIGN KEY (po_id)				REFERENCES purchase_order(id)		ON DELETE CASCADE
);

```

###### **job_note**

作业备注表
id (PK)
job_id (FK -> job.id)
content
author
created_at
updated_at

```sql

CREATE TABLE job_note (
	id							INTEGER		PRIMARY KEY AUTOINCREMENT,
	job_id					INTEGER		NOT NULL,				-- 关联 job
	content					TEXT			NOT NULL,				-- 备注内容
	author					TEXT,											-- 作者
	created_at			TEXT			NOT NULL DEFAULT (datetime('now', 'localtime')),
	updated_at			TEXT			NOT NULL DEFAULT (datetime('now', 'localtime')),

	-- 外键约束
	FOREIGN KEY (job_id)			REFERENCES job(id)				ON DELETE CASCADE
);

```

###### **order_item_note**

订单明细备注表
id (PK)
order_item_id (FK -> order_item.id)
content
author
created_at
updated_at

```sql

CREATE TABLE order_item_note (
	id							INTEGER				PRIMARY KEY AUTOINCREMENT,
	order_item_id		INTEGER				NOT NULL,				-- 关联 order_item
	content					TEXT					NOT NULL,				-- 备注内容
	author					TEXT,													-- 作者
	created_at			TEXT					NOT NULL DEFAULT (datetime('now', 'localtime')),
	updated_at			TEXT					NOT NULL DEFAULT (datetime('now', 'localtime')),

	-- 外键约束
	FOREIGN KEY (order_item_id)		REFERENCES order_item(id)		ON DELETE CASCADE
);

```

###### **part_note**

零件备注表
id (PK)
part_id (FK -> part.id)
content
author
created_at
updated_at

```sql

CREATE TABLE part_note (
	id							INTEGER		PRIMARY KEY AUTOINCREMENT,
	part_id					INTEGER		NOT NULL,				-- 关联 part
	content					TEXT			NOT NULL,				-- 备注内容
	author					TEXT,											-- 作者
	created_at			TEXT			NOT NULL DEFAULT (datetime('now', 'localtime')),
	updated_at			TEXT			NOT NULL DEFAULT (datetime('now', 'localtime')),

	-- 外键约束
	FOREIGN KEY (part_id)			REFERENCES part(id)				ON DELETE CASCADE
);

```

###### **shipment_note**

发货单备注表
id (PK)
shipment_id (FK -> shipment.id)
content
author
created_at
updated_at

```sql

CREATE TABLE shipment_note (
	id							INTEGER			PRIMARY KEY AUTOINCREMENT,
	shipment_id			INTEGER			NOT NULL,				-- 关联 shipment
	content					TEXT				NOT NULL,				-- 备注内容
	author					TEXT,												-- 作者
	created_at			TEXT				NOT NULL DEFAULT (datetime('now', 'localtime')),
	updated_at			TEXT				NOT NULL DEFAULT (datetime('now', 'localtime')),

	-- 外键约束
	FOREIGN KEY (shipment_id)		REFERENCES shipment(id)			ON DELETE CASCADE
);

```

###### **attachment_note**

附件备注表
id (PK)
attachment_id (FK -> part_attachment.id)
content
author
created_at
updated_at

```sql

CREATE TABLE attachment_note (
	id				      INTEGER				PRIMARY KEY AUTOINCREMENT,
	attachment_id		INTEGER				NOT NULL,				-- 关联 part_attachment
	content				  TEXT		 		 	NOT NULL,				-- 备注内容
	author				  TEXT,						          		-- 作者
	created_at			TEXT		 		 	NOT NULL DEFAULT (datetime('now', 'localtime')),
	updated_at			TEXT		  		NOT NULL DEFAULT (datetime('now', 'localtime')),

	-- 外键约束
	FOREIGN KEY (attachment_id)		REFERENCES part_attachment(id)		ON DELETE CASCADE
);

```
