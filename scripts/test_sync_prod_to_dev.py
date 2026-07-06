"""
<file>
  <name>test_sync_prod_to_dev.py</name>
  <description>
    Unit tests for sync_prod_to_dev.py.
    Uses only stdlib unittest and in-memory SQLite — no real network
    database required.
  </description>
</file>
"""

import sqlite3
import sys
import unittest
from pathlib import Path

# Allow importing the script from the same directory
sys.path.insert(0, str(Path(__file__).parent))
import sync_prod_to_dev as script


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------

SCHEMA = """
    CREATE TABLE customer (
        id            INTEGER PRIMARY KEY AUTOINCREMENT,
        customer_name TEXT NOT NULL,
        usage_count   INTEGER DEFAULT 0,
        last_used     TEXT,
        created_at    TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
        updated_at    TEXT NOT NULL DEFAULT (datetime('now', 'localtime'))
    );
    CREATE TABLE customer_contact (
        id            INTEGER PRIMARY KEY AUTOINCREMENT,
        customer_id   INTEGER NOT NULL,
        contact_name  TEXT NOT NULL,
        contact_email TEXT,
        usage_count   INTEGER DEFAULT 0,
        last_used     TEXT,
        created_at    TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
        updated_at    TEXT NOT NULL DEFAULT (datetime('now', 'localtime'))
    );
    CREATE TABLE purchase_order (
        id          INTEGER PRIMARY KEY AUTOINCREMENT,
        po_number   TEXT NOT NULL,
        oe_number   TEXT,
        contact_id  INTEGER,
        is_active   INTEGER DEFAULT 1,
        closed_at   TEXT,
        created_at  TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
        updated_at  TEXT NOT NULL DEFAULT (datetime('now', 'localtime'))
    );
    CREATE TABLE job (
        id          INTEGER PRIMARY KEY AUTOINCREMENT,
        job_number  TEXT UNIQUE NOT NULL,
        po_id       INTEGER NOT NULL,
        priority    TEXT DEFAULT 'Normal',
        created_at  TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
        updated_at  TEXT NOT NULL DEFAULT (datetime('now', 'localtime'))
    );
    CREATE TABLE part (
        id                        INTEGER PRIMARY KEY AUTOINCREMENT,
        drawing_number            TEXT NOT NULL,
        revision                  TEXT NOT NULL DEFAULT '-',
        description               TEXT,
        is_assembly               INTEGER DEFAULT 0,
        has_parent                INTEGER,
        production_count          INTEGER DEFAULT 0,
        total_production_hour     REAL DEFAULT 0,
        total_administrative_hour REAL DEFAULT 0,
        unit_price                REAL DEFAULT 0,
        created_at                TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
        updated_at                TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
        UNIQUE(drawing_number, revision)
    );
    CREATE TABLE order_item (
        id                      INTEGER PRIMARY KEY AUTOINCREMENT,
        job_id                  INTEGER NOT NULL,
        part_id                 INTEGER,
        line_number             INTEGER NOT NULL,
        quantity                INTEGER NOT NULL DEFAULT 0,
        actual_price            REAL,
        production_hour         REAL DEFAULT 0,
        administrative_hour     REAL DEFAULT 0,
        status                  TEXT DEFAULT 'PENDING',
        drawing_release_date    TEXT,
        delivery_required_date  TEXT,
        created_at              TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
        updated_at              TEXT NOT NULL DEFAULT (datetime('now', 'localtime'))
    );
"""


def _make_db(readonly: bool = False) -> sqlite3.Connection:
    """Create an in-memory SQLite DB with the 6-table schema used by the sync script."""
    conn = sqlite3.connect(":memory:", isolation_level=None)
    conn.executescript(SCHEMA)
    return conn


# ---------------------------------------------------------------------------
# TestSyncCascade — exercises the full customer -> ... -> order_item cascade
# ---------------------------------------------------------------------------

class TestSyncCascade(unittest.TestCase):

    def setUp(self):
        self.prod = _make_db()
        self.dev = _make_db()

        # Prod: one customer, one contact, one PO, one job, one part, two order_items
        self.prod.executescript("""
            INSERT INTO customer (id, customer_name, usage_count) VALUES (1, 'Acme Corp', 5);
            INSERT INTO customer_contact (id, customer_id, contact_name, contact_email) VALUES
                (1, 1, 'Jane Doe', 'jane@acme.com');
            INSERT INTO purchase_order (id, po_number, oe_number, contact_id, is_active) VALUES
                (1, 'PO-100', 'OE-1', 1, 1);
            INSERT INTO job (id, job_number, po_id, priority) VALUES (1, 'J-100', 1, 'High');
            INSERT INTO part (id, drawing_number, revision, description) VALUES
                (1, 'RT-001', 'A', 'Bracket');
            INSERT INTO order_item (id, job_id, part_id, line_number, quantity, actual_price, status) VALUES
                (1, 1, 1, 1, 10, 5.5, 'PENDING'),
                (2, 1, 1, 2, 20, 3.25, 'PENDING');
        """)

    def tearDown(self):
        self.prod.close()
        self.dev.close()

    def test_full_cascade_inserts_all_new_rows(self):
        changes = script.run_sync(self.prod, self.dev)

        self.assertEqual(changes["customer"], 1)
        self.assertEqual(changes["customer_contact"], 1)
        self.assertEqual(changes["purchase_order"], 1)
        self.assertEqual(changes["job"], 1)
        self.assertEqual(changes["part"], 1)
        self.assertEqual(changes["order_item"], 2)

        dev_customer = self.dev.execute("SELECT customer_name FROM customer").fetchone()
        self.assertEqual(dev_customer[0], "Acme Corp")

        dev_job = self.dev.execute(
            "SELECT j.job_number, po.po_number FROM job j JOIN purchase_order po ON po.id = j.po_id"
        ).fetchone()
        self.assertEqual(dev_job, ("J-100", "PO-100"))

        dev_items = self.dev.execute(
            "SELECT line_number, quantity FROM order_item ORDER BY line_number"
        ).fetchall()
        self.assertEqual(dev_items, [(1, 10), (2, 20)])

    def test_rerun_is_idempotent_no_duplicate_inserts(self):
        script.run_sync(self.prod, self.dev)
        # Running again against the same (unchanged) prod state should insert nothing new
        changes = script.run_sync(self.prod, self.dev)
        self.assertEqual(sum(changes.values()), 0)

        self.assertEqual(self.dev.execute("SELECT COUNT(*) FROM customer").fetchone()[0], 1)
        self.assertEqual(self.dev.execute("SELECT COUNT(*) FROM order_item").fetchone()[0], 2)

    def test_existing_dev_row_is_not_overwritten(self):
        # Dev already has this job under a different priority (simulating local test edits)
        self.dev.executescript("""
            INSERT INTO customer (id, customer_name) VALUES (1, 'Acme Corp');
            INSERT INTO customer_contact (id, customer_id, contact_name) VALUES (1, 1, 'Jane Doe');
            INSERT INTO purchase_order (id, po_number, contact_id) VALUES (1, 'PO-100', 1);
            INSERT INTO job (id, job_number, po_id, priority) VALUES (1, 'J-100', 1, 'Low');
        """)

        changes = script.run_sync(self.prod, self.dev)

        # job_number already existed -> zero new job rows, and priority must stay 'Low'
        self.assertEqual(changes["job"], 0)
        priority = self.dev.execute("SELECT priority FROM job WHERE job_number = 'J-100'").fetchone()[0]
        self.assertEqual(priority, "Low")

    def test_prod_connection_is_never_written_to(self):
        script.run_sync(self.prod, self.dev)
        # Sanity: prod row count for every table must be unchanged after a sync run
        for table in ["customer", "customer_contact", "purchase_order", "job", "part", "order_item"]:
            count = self.prod.execute(f"SELECT COUNT(*) FROM {table}").fetchone()[0]
            self.assertEqual(count, 1 if table != "order_item" else 2, f"prod.{table} row count changed")

    def test_new_order_item_on_existing_job_is_added(self):
        # Dev already synced once; prod gains a 3rd order_item on the same job
        script.run_sync(self.prod, self.dev)
        self.prod.execute(
            "INSERT INTO order_item (id, job_id, part_id, line_number, quantity) VALUES (3, 1, 1, 3, 99)"
        )

        changes = script.run_sync(self.prod, self.dev)
        self.assertEqual(changes["order_item"], 1)
        self.assertEqual(self.dev.execute("SELECT COUNT(*) FROM order_item").fetchone()[0], 3)


# ---------------------------------------------------------------------------
# TestOpenProdReadonly
# ---------------------------------------------------------------------------

class TestOpenProdReadonly(unittest.TestCase):

    def test_write_attempt_raises_on_readonly_connection(self):
        import tempfile
        import os

        fd, path = tempfile.mkstemp(suffix=".db")
        os.close(fd)
        try:
            setup_conn = sqlite3.connect(path)
            setup_conn.executescript(SCHEMA)
            setup_conn.close()

            conn = script.open_prod_readonly(path)
            with self.assertRaises(sqlite3.OperationalError):
                conn.execute("INSERT INTO customer (customer_name) VALUES ('x')")
            conn.close()
        finally:
            os.remove(path)

    def test_missing_file_exits(self):
        with self.assertRaises(SystemExit) as ctx:
            script.open_prod_readonly(r"C:\path\does\not\exist\record.db")
        self.assertEqual(ctx.exception.code, 1)

    def test_missing_unc_path_exits_cleanly(self):
        # Regression test: a "file:...?mode=ro" URI raises "invalid uri
        # authority" for any UNC path (see open_prod_readonly docstring),
        # not the expected "unable to open database file". Guard against
        # reintroducing that URI-based approach.
        with self.assertRaises(SystemExit) as ctx:
            script.open_prod_readonly(r"\\fakehost\share\does_not_exist.db")
        self.assertEqual(ctx.exception.code, 1)


if __name__ == "__main__":
    unittest.main()
