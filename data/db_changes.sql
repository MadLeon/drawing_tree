-- db_changes.sql
-- Tracks all DDL changes made against the dev database during development.
-- Apply this file to the production database (\\rtdnas2\OE\record.db) before deployment.

-- 2026-06-15: Add index for drawing number search performance
CREATE INDEX IF NOT EXISTS idx_part_drawing_number ON part(drawing_number);
