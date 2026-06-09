-- save_tree.sql
-- Save parent-child relationships from the Build Drawing Tree screen to part_tree.
--
-- For each relationship in the current tree:
--   Case A: relationship does not exist in DB → INSERT
--   Case B: relationship exists but quantity differs → UPDATE
--
-- After saving, check for orphaned relationships:
--   Case C: relationship exists in DB but not in current tree → log WARNING (no delete)
--
-- Note: run each section separately in DBeaver.
--       In C#, wrap all INSERT/UPDATE statements in a single BeginTransaction() / Commit().
--
-- Test data: parent = RT-87630-71254-1000-1-GA-D rev 2
--            child  = (replace with actual child drawing from part_tree query)

-- ── 1. Query: check if a specific parent-child relationship already exists ────
SELECT
    pt.id,
    pt.parent_id,
    pt.child_id,
    pt.quantity,
    parent.drawing_number  AS parent_drawing_number,
    parent.revision        AS parent_revision,
    child.drawing_number   AS child_drawing_number,
    child.revision         AS child_revision
FROM part_tree pt
JOIN part parent ON parent.id = pt.parent_id
JOIN part child  ON child.id  = pt.child_id
WHERE pt.parent_id = (
    SELECT id FROM part
    WHERE drawing_number = 'RT-87630-71254-1000-1-GA-D'
      AND revision       = '2'
)
ORDER BY child.drawing_number;


-- ── 2. Query: all current relationships for a given parent (used for diff) ────
--    Compare this result against the in-memory tree to detect Case B and C.
SELECT
    pt.id        AS part_tree_id,
    pt.parent_id,
    pt.child_id,
    pt.quantity,
    child.drawing_number AS child_drawing_number,
    child.revision       AS child_revision
FROM part_tree pt
JOIN part child ON child.id = pt.child_id
WHERE pt.parent_id = (
    SELECT id FROM part
    WHERE drawing_number = 'RT-87630-71254-1000-1-GA-D'
      AND revision       = '2'
);


-- ── 3. INSERT: add a new parent-child relationship (Case A) ──────────────────
--    Uncomment and replace values before running.

INSERT INTO part_tree (parent_id, child_id, quantity)
VALUES (
    (SELECT id FROM part
     WHERE drawing_number = 'RT-87630-71254-1000-1-GA-D'
       AND revision       = '2'),      -- parent_id
    (SELECT id FROM part
     WHERE drawing_number = 'RT-XXXXX-XXXXX-XXXX-X-XX-X'
       AND revision       = '2'),      -- child_id
    1                                  -- quantity
);



-- ── 4. UPDATE: change quantity of an existing relationship (Case B) ───────────
--    Uncomment and replace values before running.
/*
UPDATE part_tree
SET    quantity   = 2,                             -- new quantity
       updated_at = datetime('now', 'localtime')
WHERE  parent_id  = (
    SELECT id FROM part
    WHERE  drawing_number = 'RT-87630-71254-1000-1-GA-D'
      AND  revision       = '2'
)
  AND  child_id = (
    SELECT id FROM part
    WHERE  drawing_number = 'RT-XXXXX-XXXXX-XXXX-X-XX-X'
      AND  revision       = 'A'
);
*/


-- ── 5. Query: detect orphaned relationships (Case C — WARNING) ────────────────
--    Returns relationships that exist in DB but are absent from the current tree.
--    In C#: compare this result against the saved tree nodes and log WARNING for each row.
--
--    Usage: replace the VALUES list with the child part IDs present in the current tree.
--    Example: current tree has child part IDs (101, 202, 303).
SELECT
    pt.id        AS part_tree_id,
    pt.parent_id,
    pt.child_id,
    pt.quantity,
    parent.drawing_number AS parent_drawing_number,
    parent.revision       AS parent_revision,
    child.drawing_number  AS child_drawing_number,
    child.revision        AS child_revision
FROM part_tree pt
JOIN part parent ON parent.id = pt.parent_id
JOIN part child  ON child.id  = pt.child_id
WHERE pt.parent_id = (
    SELECT id FROM part
    WHERE drawing_number = 'RT-87630-71254-1000-1-GA-D'
      AND revision       = '2'
)
  AND pt.child_id NOT IN (
    -- Replace with the actual child part IDs present in the current tree
    SELECT id FROM part
    WHERE drawing_number IN ('RT-XXXXX-XXXXX-XXXX-1', 'RT-XXXXX-XXXXX-XXXX-2')
  );
