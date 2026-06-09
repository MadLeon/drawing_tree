-- add_drawing.sql
-- Query and insert logic for the "Add Drawing" dialog in Build Drawing Tree.
--
-- Flow:
--   User inputs drawing_number + revision →
--     Case A: part exists → fill file_path from active drawing_file (editable)
--     Case B: part does not exist → INSERT part + drawing_file, warn user
--
-- Usage:
--   Replace @drawingNumber and @revision with user input values.

-- ── 1. Query: check if part exists by drawing_number + revision ───────────────
SELECT
    p.id,
    p.drawing_number,
    p.revision,
    p.description,
    p.is_assembly,
    p.has_parent
FROM part p
WHERE p.drawing_number = 'RT-87630-71254-1000-1-GA-D'
  AND p.revision       = '2';


-- ── 2. Query: get active drawing file for the matched part ────────────────────
--    Run after query 1 confirms the part exists.
--    Use the part.id returned from query 1 as @partId.
SELECT
    df.id,
    df.file_name,
    df.file_path,
    df.revision,
    df.is_active,
    df.last_modified_at
FROM drawing_file df
WHERE df.part_id = (
    SELECT id FROM part
    WHERE drawing_number = 'RT-87630-71254-1000-1-GA-D'
      AND revision       = '2'
)
  AND df.is_active = 1;


-- ── 3. Query: fuzzy search — drawing_number only (revision unknown) ───────────
--    Used when user leaves revision blank; returns all revisions for selection.
SELECT
    p.id,
    p.drawing_number,
    p.revision,
    p.description,
    df.file_path
FROM part p
LEFT JOIN drawing_file df ON df.part_id = p.id AND df.is_active = 1
WHERE p.drawing_number LIKE '%87630-71254-1000%'
ORDER BY p.drawing_number, p.revision;


-- ── 4. INSERT: new part (Case B — part not found) ────────────────────────────
--    Uncomment and replace values before running.
--    Log a WARNING after insert: "Part not found, created new: <drawing_number> rev <revision>"
/*
INSERT INTO part (drawing_number, revision, description, is_assembly, has_parent)
VALUES (
    'RT-XXXXX-XXXXX-XXXX-X-XX-X',   -- drawing_number  (from user input)
    'A',                              -- revision        (from user input; use '-' if blank)
    NULL,                             -- description     (unknown at this stage)
    NULL,                             -- is_assembly     (unknown at this stage)
    NULL                              -- has_parent      (unknown at this stage)
);
*/


-- ── 5. UPSERT: set active drawing_file for a known part ──────────────────────
--    Uses ON CONFLICT(file_path) DO UPDATE to handle the case where the file
--    path already exists in the table (imported from G drive scan).
--    Deactivates all other active files for the same part first.
--    Uncomment and replace values before running.
-- Note: run Step A and Step B separately in DBeaver (auto-commit mode).
--       In C#, wrap both in a single BeginTransaction() / Commit().
--       Uncomment and replace values before running.

-- Step A: deactivate other active files for this part

UPDATE drawing_file
SET    is_active  = 0,
       updated_at = datetime('now', 'localtime')
WHERE  part_id = (
    SELECT id FROM part
    WHERE  drawing_number = 'RT-87630-71254-1000-1-GA-D'
      AND  revision       = '2'
);


-- Step B: upsert the target file (insert or update if file_path already exists)

INSERT INTO drawing_file (part_id, file_name, file_path, is_active, revision)
VALUES (
    (SELECT id FROM part
     WHERE  drawing_number = 'RT-87630-71254-1000-1-GA-D'
       AND  revision       = '2'),
    'RT-87630-71254-1000-1-GA-D.pdf',             -- file_name  (from user input)
    'G:\Drawings\RT-87630-71254-1000-1-GA-D.pdf', -- file_path  (from user input or browse)
    1,                                              -- is_active
    '2'                                             -- revision   (from user input)
)
ON CONFLICT(file_path) DO UPDATE SET
    part_id    = excluded.part_id,
    file_name  = excluded.file_name,
    is_active  = 1,
    revision   = excluded.revision,
    updated_at = datetime('now', 'localtime');

