-- po_tree.sql
-- Query all jobs, order items, and top-level parts for a given PO number.
-- Replaces the hardcoded mock in PoTreeService.GetMockGroups().
--
-- Chain: purchase_order → job → order_item → part
--
-- Usage:
--   Replace @poNumber with the actual PO number (e.g. 'RT79-87630-PN-R005')
--
-- Expected result for 'RT79-87630-PN-R005':
--   job_number=72395, line_number=1, drawing_number=RT-87630-71254-1000-1-GA-D

-- ── 1. Verify PO exists ───────────────────────────────────────────────────────
SELECT
    po.id        AS po_id,
    po.po_number,
    po.oe_number,
    po.is_active
FROM purchase_order po
WHERE po.po_number = 'RT79-87630-PN-R005';


-- ── 2. Main query: all order items under the PO ───────────────────────────────
SELECT
    j.job_number,
    oi.line_number,
    p.id             AS part_id,
    p.drawing_number,
    p.revision,
    p.description,
    p.is_assembly,
    p.has_parent
FROM purchase_order po
JOIN job        j  ON j.po_id   = po.id
JOIN order_item oi ON oi.job_id = j.id
JOIN part       p  ON p.id      = oi.part_id
WHERE po.po_number = 'RT79-87630-PN-R005'
ORDER BY j.job_number, oi.line_number;


-- ── 3. With active drawing file path (LEFT JOIN — part may have no file) ──────
SELECT
    j.job_number,
    oi.line_number,
    p.id             AS part_id,
    p.drawing_number,
    p.revision,
    p.description,
    p.is_assembly,
    p.has_parent,
    df.file_path     AS drawing_file_path,
    df.file_name     AS drawing_file_name
FROM purchase_order po
JOIN job        j  ON j.po_id   = po.id
JOIN order_item oi ON oi.job_id = j.id
JOIN part       p  ON p.id      = oi.part_id
LEFT JOIN drawing_file df ON df.part_id = p.id AND df.is_active = 1
WHERE po.po_number = 'RT79-87630-PN-R005'
ORDER BY j.job_number, oi.line_number;
