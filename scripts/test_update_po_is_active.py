"""
<file>
  <name>test_update_po_is_active.py</name>
  <description>
    Unit tests for update_po_is_active.py.
    Uses only stdlib unittest and in-memory SQLite — no real Excel file or
    network database required.
  </description>
</file>
"""

import sqlite3
import sys
import unittest
from pathlib import Path
from unittest.mock import MagicMock, patch

# Allow importing the script from the same directory
sys.path.insert(0, str(Path(__file__).parent))
import update_po_is_active as script


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------

def _make_db() -> sqlite3.Connection:
    """Create an in-memory SQLite DB with the minimal schema for testing."""
    conn = sqlite3.connect(":memory:", isolation_level=None)
    conn.executescript("""
        CREATE TABLE purchase_order (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            po_number  TEXT NOT NULL UNIQUE,
            is_active  INTEGER DEFAULT 1,
            updated_at TEXT NOT NULL DEFAULT (datetime('now', 'localtime'))
        );
        CREATE TABLE job (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            job_number TEXT UNIQUE NOT NULL,
            po_id      INTEGER NOT NULL,
            FOREIGN KEY (po_id) REFERENCES purchase_order(id)
        );
        CREATE TABLE order_item (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            job_id     INTEGER NOT NULL,
            line_number INTEGER NOT NULL,
            FOREIGN KEY (job_id) REFERENCES job(id)
        );
    """)
    return conn


def _insert_fixtures(conn: sqlite3.Connection):
    """
    Inserts three POs, each with one job and two order_items.

    PO 1 (id=1): order_items 1, 2
    PO 2 (id=2): order_items 3, 4
    PO 3 (id=3): order_items 5, 6  (initially inactive)
    """
    conn.executescript("""
        INSERT INTO purchase_order (id, po_number, is_active) VALUES
            (1, 'PO-001', 1),
            (2, 'PO-002', 1),
            (3, 'PO-003', 0);

        INSERT INTO job (id, job_number, po_id) VALUES
            (1, 'J-001', 1),
            (2, 'J-002', 2),
            (3, 'J-003', 3);

        INSERT INTO order_item (id, job_id, line_number) VALUES
            (1, 1, 1), (2, 1, 2),
            (3, 2, 1), (4, 2, 2),
            (5, 3, 1), (6, 3, 2);
    """)


# ---------------------------------------------------------------------------
# TestReadActiveOrderItemIds
# ---------------------------------------------------------------------------

class TestReadActiveOrderItemIds(unittest.TestCase):

    def _make_mock_workbook(self, column_values: list):
        """Build a mock openpyxl workbook whose first worksheet yields column_values."""
        wb = MagicMock()
        ws = MagicMock()
        wb.worksheets = [ws]

        # iter_rows yields one-element tuples per row
        rows = [
            (MagicMock(value=v),) for v in column_values
        ]
        ws.iter_rows.return_value = iter(rows)
        return wb

    @patch("update_po_is_active.openpyxl.load_workbook")
    def test_reads_valid_integer_ids(self, mock_load):
        # HEADER_ROWS=1 so first entry is skipped; remaining 3 are valid IDs
        mock_load.return_value = self._make_mock_workbook(["ID", 10, 20, 30])
        result = script.read_active_order_item_ids("dummy.xlsm")
        self.assertEqual(result, {10, 20, 30})

    @patch("update_po_is_active.openpyxl.load_workbook")
    def test_skips_none_values(self, mock_load):
        mock_load.return_value = self._make_mock_workbook(["ID", 10, None, 20])
        result = script.read_active_order_item_ids("dummy.xlsm")
        self.assertEqual(result, {10, 20})

    @patch("update_po_is_active.openpyxl.load_workbook")
    def test_skips_non_integer_values(self, mock_load):
        mock_load.return_value = self._make_mock_workbook(["ID", "N/A", 42, "x"])
        result = script.read_active_order_item_ids("dummy.xlsm")
        self.assertEqual(result, {42})

    @patch("update_po_is_active.openpyxl.load_workbook")
    def test_skips_header_rows(self, mock_load):
        # Only rows after the first HEADER_ROWS rows should be read
        mock_load.return_value = self._make_mock_workbook(["Header", 99])
        result = script.read_active_order_item_ids("dummy.xlsm")
        self.assertIn(99, result)
        self.assertNotIn("Header", result)

    @patch("update_po_is_active.openpyxl.load_workbook", side_effect=FileNotFoundError)
    def test_file_not_found_exits(self, _mock):
        with self.assertRaises(SystemExit) as ctx:
            script.read_active_order_item_ids("missing.xlsm")
        self.assertEqual(ctx.exception.code, 1)


# ---------------------------------------------------------------------------
# TestResolvePoIdsFromOrderItems
# ---------------------------------------------------------------------------

class TestResolvePoIdsFromOrderItems(unittest.TestCase):

    def setUp(self):
        self.conn = _make_db()
        _insert_fixtures(self.conn)

    def tearDown(self):
        self.conn.close()

    def test_resolves_single_item(self):
        result = script.resolve_po_ids_from_order_items(self.conn, {1})
        self.assertEqual(result, {1})

    def test_resolves_multiple_items_same_po(self):
        # order_items 1 and 2 both belong to PO 1
        result = script.resolve_po_ids_from_order_items(self.conn, {1, 2})
        self.assertEqual(result, {1})

    def test_resolves_items_across_different_pos(self):
        result = script.resolve_po_ids_from_order_items(self.conn, {1, 3})
        self.assertEqual(result, {1, 2})

    def test_empty_input_returns_empty_set(self):
        result = script.resolve_po_ids_from_order_items(self.conn, set())
        self.assertEqual(result, set())

    def test_unknown_item_id_ignored(self):
        result = script.resolve_po_ids_from_order_items(self.conn, {9999})
        self.assertEqual(result, set())


# ---------------------------------------------------------------------------
# TestComputeChanges
# ---------------------------------------------------------------------------

class TestComputeChanges(unittest.TestCase):

    def setUp(self):
        self.conn = _make_db()
        _insert_fixtures(self.conn)
        # Initial state: PO 1=active, PO 2=active, PO 3=inactive

    def tearDown(self):
        self.conn.close()

    def test_all_to_activate(self):
        # All 3 POs should be active, but PO 3 is currently inactive
        changes = script.compute_changes(self.conn, {1, 2, 3})
        self.assertEqual(changes["to_activate"], {3})
        self.assertEqual(changes["to_deactivate"], set())
        self.assertEqual(changes["already_active"], 2)
        self.assertEqual(changes["already_inactive"], 0)

    def test_all_to_deactivate(self):
        # No POs should be active
        changes = script.compute_changes(self.conn, set())
        self.assertEqual(changes["to_activate"], set())
        self.assertEqual(changes["to_deactivate"], {1, 2})
        self.assertEqual(changes["already_active"], 0)
        self.assertEqual(changes["already_inactive"], 1)

    def test_mixed_scenario(self):
        # Only PO 2 should be active
        changes = script.compute_changes(self.conn, {2})
        self.assertEqual(changes["to_activate"], set())
        self.assertEqual(changes["to_deactivate"], {1})
        self.assertEqual(changes["already_active"], 1)
        self.assertEqual(changes["already_inactive"], 1)

    def test_already_correct_counted(self):
        # PO 1 and 2 already active — should not appear in to_activate
        changes = script.compute_changes(self.conn, {1, 2})
        self.assertNotIn(1, changes["to_activate"])
        self.assertNotIn(2, changes["to_activate"])
        self.assertEqual(changes["already_active"], 2)


# ---------------------------------------------------------------------------
# TestApplyChanges
# ---------------------------------------------------------------------------

class TestApplyChanges(unittest.TestCase):

    def setUp(self):
        self.conn = _make_db()
        _insert_fixtures(self.conn)

    def tearDown(self):
        self.conn.close()

    def _get_is_active(self, po_id: int) -> int:
        row = self.conn.execute(
            "SELECT is_active FROM purchase_order WHERE id = ?", (po_id,)
        ).fetchone()
        return row[0]

    def test_activate_updates_is_active(self):
        # PO 3 is initially inactive
        changes = {"to_activate": {3}, "to_deactivate": set()}
        script.apply_changes(self.conn, changes)
        self.assertEqual(self._get_is_active(3), 1)

    def test_deactivate_sets_is_active_zero(self):
        # PO 1 is initially active
        changes = {"to_activate": set(), "to_deactivate": {1}}
        script.apply_changes(self.conn, changes)
        self.assertEqual(self._get_is_active(1), 0)

    def test_no_changes_leaves_db_unchanged(self):
        changes = {"to_activate": set(), "to_deactivate": set()}
        script.apply_changes(self.conn, changes)
        self.assertEqual(self._get_is_active(1), 1)
        self.assertEqual(self._get_is_active(3), 0)

    def test_transaction_rolls_back_on_error(self):
        # Use a MagicMock connection so executemany can be made to raise.
        # sqlite3.Connection.executemany is read-only and cannot be monkey-patched.
        mock_conn = MagicMock()
        mock_conn.executemany.side_effect = sqlite3.OperationalError("simulated failure")

        changes = {"to_activate": {3}, "to_deactivate": {1}}
        with self.assertRaises(sqlite3.OperationalError):
            script.apply_changes(mock_conn, changes)

        # Verify that ROLLBACK was issued via conn.execute(...)
        rollback_issued = any(
            "ROLLBACK" in str(call)
            for call in mock_conn.execute.call_args_list
        )
        self.assertTrue(rollback_issued, "ROLLBACK should have been called on error")


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    unittest.main()
