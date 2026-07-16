-- Cascading query: purchase_order -> job -> order_item -> part
-- For ad-hoc inspection in DBeaver.
SELECT
    po.id                      AS po_id,
    po.po_number,
    po.oe_number,
    po.is_active               AS po_active,
    cust.customer_name,
    cc.contact_name,
    j.id                       AS job_id,
    j.job_number,
    oi.id                      AS order_item_id,
    oi.line_number,
    oi.quantity,
    oi.actual_price,
    oi.status,
    oi.is_active               AS order_item_active,
    oi.description             AS order_item_description,
    oi.drawing_release_date,
    oi.delivery_required_date,
    p.id                       AS part_id,
    p.drawing_number,
    p.revision,
    p.description              AS part_description
FROM purchase_order po
JOIN job j                     ON j.po_id = po.id
JOIN order_item oi             ON oi.job_id = j.id
LEFT JOIN part p                ON p.id = oi.part_id
LEFT JOIN customer_contact cc   ON cc.id = po.contact_id
LEFT JOIN customer cust         ON cust.id = cc.customer_id
WHERE j.job_number="72906" OR j.job_number="72517"
ORDER BY po.po_number, j.job_number, oi.line_number;
