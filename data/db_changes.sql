-- db_changes.sql
-- Tracks all DDL changes made against the dev database during development.
-- Apply this file to the production database (\\rtdnas2\OE\record.db) before deployment.

-- 2026-06-15: Add index for drawing number search performance
CREATE INDEX IF NOT EXISTS idx_part_drawing_number ON part(drawing_number);

-- 2026-06-20: Fix case-duplicate records and enforce COLLATE NOCASE on drawing_number
-- Root cause: SQLite TEXT columns use BINARY collation by default, so 'RT-xxx' and 'rt-xxx'
-- were treated as distinct values, allowing case-variant duplicates to accumulate (170+ pairs).

-- Step 1: Merge lowercase duplicates into their uppercase canonical records.
-- Safe to re-run: UPDATE/DELETE match 0 rows when no duplicates remain.
-- Uses UPDATE OR IGNORE + cleanup DELETE to handle cases where the canonical
-- is already present under the same parent (avoids UNIQUE(parent_id,child_id) conflicts).
BEGIN;

-- Redirect part_tree.child_id: dup → canonical (skip if canonical already present)
UPDATE OR IGNORE part_tree
SET child_id = canonical.id
FROM (
    SELECT p_upper.id AS id, p_lower.id AS dup_id
    FROM part p_upper
    JOIN part p_lower
      ON LOWER(p_upper.drawing_number) = LOWER(p_lower.drawing_number)
     AND p_upper.revision              = p_lower.revision
     AND p_upper.drawing_number        = UPPER(p_upper.drawing_number)
     AND p_lower.drawing_number       != UPPER(p_lower.drawing_number)
) AS canonical
WHERE part_tree.child_id = canonical.dup_id;

-- Remove rows that still reference dup_ids as child (UPDATE OR IGNORE skipped them
-- because the canonical child was already present under the same parent)
DELETE FROM part_tree
WHERE child_id IN (
    SELECT p_lower.id FROM part p_upper
    JOIN part p_lower
      ON LOWER(p_upper.drawing_number) = LOWER(p_lower.drawing_number)
     AND p_upper.revision              = p_lower.revision
     AND p_upper.drawing_number        = UPPER(p_upper.drawing_number)
     AND p_lower.drawing_number       != UPPER(p_lower.drawing_number)
);

-- Redirect part_tree.parent_id: dup → canonical (skip if canonical already present)
UPDATE OR IGNORE part_tree
SET parent_id = canonical.id
FROM (
    SELECT p_upper.id AS id, p_lower.id AS dup_id
    FROM part p_upper
    JOIN part p_lower
      ON LOWER(p_upper.drawing_number) = LOWER(p_lower.drawing_number)
     AND p_upper.revision              = p_lower.revision
     AND p_upper.drawing_number        = UPPER(p_upper.drawing_number)
     AND p_lower.drawing_number       != UPPER(p_lower.drawing_number)
) AS canonical
WHERE part_tree.parent_id = canonical.dup_id;

-- Remove rows that still reference dup_ids as parent_id
DELETE FROM part_tree
WHERE parent_id IN (
    SELECT p_lower.id FROM part p_upper
    JOIN part p_lower
      ON LOWER(p_upper.drawing_number) = LOWER(p_lower.drawing_number)
     AND p_upper.revision              = p_lower.revision
     AND p_upper.drawing_number        = UPPER(p_upper.drawing_number)
     AND p_lower.drawing_number       != UPPER(p_lower.drawing_number)
);

-- Redirect drawing_file.part_id only when the uppercase canonical has no active file
UPDATE drawing_file
SET part_id = canonical.id
FROM (
    SELECT p_upper.id AS id, p_lower.id AS dup_id
    FROM part p_upper
    JOIN part p_lower
      ON LOWER(p_upper.drawing_number) = LOWER(p_lower.drawing_number)
     AND p_upper.revision              = p_lower.revision
     AND p_upper.drawing_number        = UPPER(p_upper.drawing_number)
     AND p_lower.drawing_number       != UPPER(p_lower.drawing_number)
    WHERE NOT EXISTS (
        SELECT 1 FROM drawing_file df2
        WHERE df2.part_id = p_upper.id AND df2.is_active = 1
    )
) AS canonical
WHERE drawing_file.part_id = canonical.dup_id;

-- Carry has_parent=1 from lowercase dup up to the uppercase canonical
UPDATE part
SET has_parent = 1
WHERE drawing_number = UPPER(drawing_number)
  AND EXISTS (
      SELECT 1 FROM part p_lower
      WHERE LOWER(p_lower.drawing_number) = LOWER(part.drawing_number)
        AND p_lower.revision              = part.revision
        AND p_lower.drawing_number       != UPPER(p_lower.drawing_number)
        AND p_lower.has_parent            = 1
  );

-- Delete lowercase records that have an uppercase counterpart with the same revision
DELETE FROM part
WHERE drawing_number != UPPER(drawing_number)
  AND EXISTS (
      SELECT 1 FROM part p2
      WHERE LOWER(p2.drawing_number) = LOWER(part.drawing_number)
        AND p2.revision              = part.revision
        AND p2.drawing_number        = UPPER(p2.drawing_number)
  );

COMMIT;

-- Step 2: Rebuild part table with COLLATE NOCASE so the UNIQUE constraint
-- becomes case-insensitive, preventing future case-duplicate inserts.
-- Idempotent: DROP IF EXISTS handles partial-run recovery; full re-run is safe.
BEGIN;

DROP TABLE IF EXISTS part_new;

CREATE TABLE part_new (
    id                        INTEGER PRIMARY KEY AUTOINCREMENT,
    previous_id               INTEGER,
    next_id                   INTEGER,
    drawing_number            TEXT NOT NULL COLLATE NOCASE,
    revision                  TEXT NOT NULL DEFAULT '-',
    description               TEXT,
    is_assembly               INTEGER DEFAULT 0,
    production_count          INTEGER DEFAULT 0,
    total_production_hour     REAL DEFAULT 0,
    total_administrative_hour REAL DEFAULT 0,
    unit_price                REAL DEFAULT 0,
    created_at                TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
    updated_at                TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
    has_parent                INTEGER,
    UNIQUE(drawing_number, revision),
    FOREIGN KEY (previous_id) REFERENCES part_new(id) ON DELETE SET NULL,
    FOREIGN KEY (next_id)     REFERENCES part_new(id) ON DELETE SET NULL
);

INSERT INTO part_new SELECT * FROM part;

DROP TABLE part;
ALTER TABLE part_new RENAME TO part;

CREATE INDEX IF NOT EXISTS idx_part_drawing_number ON part(drawing_number);

COMMIT;

-- 2026-07-03: Add status column to part_attachment (Issue #23)
ALTER TABLE part_attachment ADD COLUMN status TEXT NOT NULL DEFAULT 'in progress';

-- Backfill pre-existing rows (created before status tracking existed) as completed
UPDATE part_attachment SET status = 'completed';

-- 2026-07-08: Add description/revision to purchase_order; enforce uniqueness on customer_contact (Issue #37)
ALTER TABLE purchase_order ADD COLUMN description TEXT;
ALTER TABLE purchase_order ADD COLUMN revision TEXT;

-- Rebuild customer_contact with UNIQUE(customer_id, contact_name) — no existing duplicates today,
-- but UpsertContact's INSERT OR IGNORE silently relies on this constraint to dedupe.
BEGIN;
DROP TABLE IF EXISTS customer_contact_new;
CREATE TABLE customer_contact_new (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    customer_id   INTEGER NOT NULL,
    contact_name  TEXT NOT NULL,
    contact_email TEXT,
    usage_count   INTEGER DEFAULT 0,
    last_used     TEXT,
    created_at    TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
    updated_at    TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
    UNIQUE(customer_id, contact_name),
    FOREIGN KEY (customer_id) REFERENCES customer(id) ON DELETE CASCADE
);
INSERT INTO customer_contact_new SELECT * FROM customer_contact;
DROP TABLE customer_contact;
ALTER TABLE customer_contact_new RENAME TO customer_contact;
CREATE INDEX IF NOT EXISTS idx_customer_contact_customer_id ON customer_contact(customer_id);
CREATE INDEX IF NOT EXISTS idx_customer_contact_name ON customer_contact(contact_name);
COMMIT;

-- 2026-07-13: Add order_item.is_active for item-level shipped tracking (Issue #44)
-- purchase_order.is_active is too coarse: a PO can be partially shipped
-- (some order_items already delivered, others still open).
ALTER TABLE order_item ADD COLUMN is_active INTEGER NOT NULL DEFAULT 1;

-- Backfill from the owning PO: items of an already-inactive PO start inactive;
-- items of active POs start active (corrected later by OE sync runs).
UPDATE order_item
SET is_active = COALESCE((
    SELECT po.is_active
    FROM job j
    JOIN purchase_order po ON po.id = j.po_id
    WHERE j.id = order_item.job_id
), 1);

-- 2026-07-13: OE sync anomaly "handled" memory (Issue #44 follow-up)
-- Records that a human already reviewed a given (job_number, line_number) Modify/Anomaly change
-- (via Ignore, or a corrected Update) so a future OE sync run can recognize the exact same
-- proposed change and suppress it instead of re-nagging from scratch. Matching is by
-- `fingerprint` (a hash of the specific field values being proposed — see
-- OeSyncService.ComputeFingerprint), not by job/line alone, so a genuinely new/different change
-- on the same (job_number, line_number) still surfaces for review. Deleted automatically once
-- the order_item ships (is_active -> 0), since it can never resurface in a diff again at that
-- point.
-- Deactivate ("Shipped") rows are a PO-level group of order_items, not a single (job, line) — so
-- a Deactivate Ignore writes one row PER item in the group, all sharing the same
-- ComputeDeactivateFingerprint (a hash of the whole group's membership, not a value pair). If the
-- group's membership changes next sync (an item joins or leaves), at least one item's row stops
-- matching and the whole group resurfaces for review.
CREATE TABLE IF NOT EXISTS oe_anomaly_handled (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    job_number     TEXT NOT NULL,
    line_number    TEXT NOT NULL,
    anomaly_reason TEXT NOT NULL,   -- human-readable summary, for debugging only (not matched on)
    fingerprint    TEXT NOT NULL,   -- hash of the proposed field value(s); the actual match key
    action         TEXT NOT NULL,   -- 'ignored' | 'corrected'
    handled_at     TEXT NOT NULL DEFAULT (datetime('now','localtime')),
    UNIQUE(job_number, line_number)
);

-- 2026-07-14: order_item.description (Issue #44 follow-up)
-- The OE file's "Descriptions:" column is per-order text, not a physical-part attribute — two
-- order_items can legitimately share the same part (drawing_number + revision), even the same
-- purchase_order, and still carry different Description text (e.g. job #73158/#73159, same part,
-- same PO 19741, different work-order/destination text baked into each line's description).
-- Storing it on the shared `part` row caused OE sync to overwrite one job's description with
-- another's on every run. order_item.description is per-line, so no row is ever shared and no
-- overwrite conflict is possible. part.description remains as the part's own default/canonical
-- text (set once on first creation, never overwritten by OE sync afterward).
ALTER TABLE order_item ADD COLUMN description TEXT;

-- Backfill from the currently-linked part's description as a reasonable starting point, so
-- existing rows don't show blank Description until their next OE sync Modify.
UPDATE order_item
SET description = (SELECT p.description FROM part p WHERE p.id = order_item.part_id)
WHERE description IS NULL;
