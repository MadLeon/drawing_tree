-- part_tree.sql
-- Recursively query the full part tree rooted at a given top-level drawing number.
-- Uses SQLite CTE (WITH RECURSIVE) to traverse parent → child relationships in part_tree.
--
-- Used in Build Drawing Tree to pre-populate existing relationships on load.
--
-- Usage:
--   Replace the drawing_number and revision in the anchor query
--   to match the top-level drawing from the import JSON.

-- ── 1. Verify part exists and check if it has children ───────────────────────
SELECT
    p.id,
    p.drawing_number,
    p.revision,
    p.is_assembly,
    p.has_parent,
    COUNT(pt.id) AS child_count
FROM part p
LEFT JOIN part_tree pt ON pt.parent_id = p.id
WHERE p.drawing_number = 'RT-87630-71254-1000-1-GA-D'
  AND p.revision       = '2'
GROUP BY p.id;


-- ── 2. Recursive CTE: full tree starting from root drawing ───────────────────
--    Returns every ancestor→descendant edge, with depth and full path.
WITH RECURSIVE tree AS (
    -- Anchor: the root part
    SELECT
        p.id             AS part_id,
        p.drawing_number,
        p.revision,
        p.description,
        p.is_assembly,
        CAST(NULL AS INTEGER) AS parent_part_id,
        CAST(NULL AS TEXT)    AS parent_drawing_number,
        CAST(NULL AS TEXT)    AS parent_revision,
        CAST(NULL AS INTEGER) AS part_tree_id,
        CAST(NULL AS INTEGER) AS quantity,
        0                     AS depth,
        p.drawing_number      AS path
    FROM part p
    WHERE p.drawing_number = 'RT-87630-71254-1000-1-GA-D'
      AND p.revision       = '2'

    UNION ALL

    -- Recursive: walk down through part_tree
    SELECT
        child.id             AS part_id,
        child.drawing_number,
        child.revision,
        child.description,
        child.is_assembly,
        parent.part_id       AS parent_part_id,
        parent.drawing_number AS parent_drawing_number,
        parent.revision      AS parent_revision,
        pt.id                AS part_tree_id,
        pt.quantity,
        parent.depth + 1     AS depth,
        parent.path || ' > ' || child.drawing_number AS path
    FROM tree parent
    JOIN part_tree pt    ON pt.parent_id = parent.part_id
    JOIN part      child ON child.id     = pt.child_id
)
SELECT
    part_id,
    drawing_number,
    revision,
    description,
    is_assembly,
    parent_part_id,
    parent_drawing_number,
    parent_revision,
    part_tree_id,
    quantity,
    depth,
    path
FROM tree
ORDER BY depth, drawing_number;


-- ── 3. Direct children only (one level, no recursion) ────────────────────────
--    Useful for lazy-loading tree nodes one level at a time.
SELECT
    pt.id          AS part_tree_id,
    pt.quantity,
    child.id       AS child_part_id,
    child.drawing_number,
    child.revision,
    child.description,
    child.is_assembly,
    child.has_parent,
    df.file_path   AS drawing_file_path
FROM part_tree pt
JOIN part child ON child.id = pt.child_id
LEFT JOIN drawing_file df ON df.part_id = child.id AND df.is_active = 1
WHERE pt.parent_id = (
    SELECT id FROM part
    WHERE drawing_number = 'RT-87630-71254-1000-1-GA-D'
      AND revision       = '2'
)
ORDER BY child.drawing_number;
