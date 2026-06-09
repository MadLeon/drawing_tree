-- drawing_info.sql
-- Query part info and active drawing file for a given drawing number.
-- Used in the drawing info panel of the Build Drawing Tree screen.
--
-- If part does not exist → INSERT into part, then INSERT into drawing_file.
-- If part exists but drawing_file does not → INSERT into drawing_file only.
--
-- Usage:
--   Replace @drawingNumber with the actual drawing number
--   Replace @revision      with the revision (use '-' if unknown)
--   Replace @filePath      with the full file path of the PDF

-- ── 1. Query: look up part by drawing number ──────────────────────────────────
--    Returns all revisions; caller picks the one matching the import JSON.
SELECT
    p.id,
    p.drawing_number,
    p.revision,
    p.description,
    p.is_assembly,
    p.has_parent,
    p.created_at
FROM part p
WHERE p.drawing_number = 'RT-87630-71254-1000-1-GA-D'
ORDER BY p.revision;


-- ── 2. Query: active drawing file for a specific part ────────────────────────
SELECT
    df.id,
    df.file_name,
    df.file_path,
    df.revision,
    df.is_active,
    df.last_modified_at
FROM drawing_file df
WHERE df.part_id = (
    SELECT p.id FROM part p
    WHERE p.drawing_number = 'RT-87630-71254-1000-1-GA-D'
      AND p.revision       = '2'
)
  AND df.is_active = 1;


-- ── 3. Combined single-row result (drawing number + revision → part + file) ───
SELECT
    p.id             AS part_id,
    p.drawing_number,
    p.revision,
    p.description,
    p.is_assembly,
    p.has_parent,
    df.id            AS drawing_file_id,
    df.file_name,
    df.file_path,
    df.is_active
FROM part p
LEFT JOIN drawing_file df ON df.part_id = p.id AND df.is_active = 1
WHERE p.drawing_number = 'RT-87630-71254-1000-1-GA-D'
  AND p.revision       = '2';


-- ── 4. INSERT: create new part (when drawing number not found) ────────────────
--    Uncomment and replace values before running.
/*
INSERT INTO part (drawing_number, revision, description, is_assembly, has_parent)
VALUES (
    'RT-XXXXX-XXXXX-XXXX-X-XX-X',   -- drawing_number
    'A',                              -- revision (use '-' if unknown)
    NULL,                             -- description
    NULL,                             -- is_assembly (NULL = unknown)
    NULL                              -- has_parent  (NULL = unknown)
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
/*
UPDATE drawing_file
SET    is_active  = 0,
       updated_at = datetime('now', 'localtime')
WHERE  part_id = (
    SELECT id FROM part
    WHERE  drawing_number = 'RT-XXXXX-XXXXX-XXXX-X-XX-X'
      AND  revision       = 'A'
);
*/

-- Step B: upsert the target file (insert or update if file_path already exists)
/*
INSERT INTO drawing_file (part_id, file_name, file_path, is_active, revision)
VALUES (
    (SELECT id FROM part
     WHERE  drawing_number = 'RT-XXXXX-XXXXX-XXXX-X-XX-X'
       AND  revision       = 'A'),
    'RT-XXXXX-XXXXX-XXXX-X-XX-X.pdf',             -- file_name
    'G:\Drawings\RT-XXXXX-XXXXX-XXXX-X-XX-X.pdf', -- file_path
    1,                                              -- is_active
    'A'                                             -- revision
)
ON CONFLICT(file_path) DO UPDATE SET
    part_id    = excluded.part_id,
    file_name  = excluded.file_name,
    is_active  = 1,
    revision   = excluded.revision,
    updated_at = datetime('now', 'localtime');
*/

