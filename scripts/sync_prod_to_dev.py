"""
<file>
  <name>sync_prod_to_dev.py</name>
  <description>
    Copies rows that exist in the production database but not yet in the
    development database, across the six tables the OE Excel macro
    (reference/mod_AddNewJobToDB.bas) writes to: customer -> customer_contact
    -> purchase_order -> job -> part -> order_item. Each table is matched by
    its natural key (e.g. job.job_number, part.(drawing_number, revision));
    rows that already exist in dev are left completely untouched -- this is
    an insert-only sync, it never overwrites dev data. The production
    database is opened with PRAGMA query_only = ON, so any write statement
    raises an error and it can never be modified by this script (a plain,
    non-URI connection is used because prod lives on a UNC network path,
    and SQLite's file: URI parser rejects non-empty/non-localhost
    authorities -- see open_prod_readonly). Runs in dry-run mode by
    default; pass --apply to commit changes, which backs up the dev
    database first.
  </description>
  <usage>
    python scripts/sync_prod_to_dev.py                     # dry-run (default)
    python scripts/sync_prod_to_dev.py --apply              # write to dev DB (auto-backs up first)
    python scripts/sync_prod_to_dev.py --db prod --apply    # (not supported; prod is always the source)
    python scripts/sync_prod_to_dev.py --dev-db PATH --prod-db PATH
  </usage>
  <dependencies>sqlite3 (stdlib), argparse (stdlib), shutil (stdlib)</dependencies>
</file>
"""

import argparse
import datetime
import os
import shutil
import sqlite3
import sys
from pathlib import Path

# ---------------------------------------------------------------------------
# Configuration — edit DB paths here; do not hard-code usernames
# ---------------------------------------------------------------------------
DEV_DB = Path(os.environ["USERPROFILE"]) / "drawing_tree" / "data" / "record.db"
PROD_DB = r"\\rtdnas2\OE\record.db"


# ---------------------------------------------------------------------------
# Logging helper
# ---------------------------------------------------------------------------

def log(level: str, func_name: str, message: str) -> None:
    """
    <summary>
    Prints a log line to stdout using the project's standard format.
    </summary>
    <param name="level">Severity: INFO, WARN, or ERROR</param>
    <param name="func_name">Caller identifier, e.g. 'sync_prod_to_dev.main'</param>
    <param name="message">Human-readable message</param>
    """
    ts = datetime.datetime.now().strftime("%Y-%m-%dT%H:%M:%S")
    print(f"{ts} [{level}] [{func_name}] - {message}")


# ---------------------------------------------------------------------------
# Connection helpers
# ---------------------------------------------------------------------------

def open_prod_readonly(prod_path: str) -> sqlite3.Connection:
    """
    <summary>
    Opens the production database and enforces read-only access with
    PRAGMA query_only, so no write statement can ever reach the production
    file even by accident. A plain (non-URI) connection is used rather than
    a "file:...?mode=ro" URI: prod lives on a UNC network path
    (\\\\rtdnas2\\OE\\record.db), and SQLite's URI parser rejects any
    non-empty, non-"localhost" authority (the UNC hostname) unless the
    library was built with SQLITE_ENABLE_URI_AUTHORITY, which the stock
    Windows Python sqlite3.dll is not -- so mode=ro URIs fail with
    "invalid uri authority" for UNC paths regardless of filesystem
    permissions. PRAGMA query_only gives the same guarantee without going
    through URI parsing.
    </summary>
    <param name="prod_path">Filesystem path to the production record.db</param>
    <returns>Read-only sqlite3.Connection</returns>
    <exception cref="SystemExit">Exits with code 1 if the file cannot be opened</exception>
    """
    func = "sync_prod_to_dev.open_prod_readonly"
    try:
        conn = sqlite3.connect(prod_path)
        conn.execute("PRAGMA query_only = ON")
        conn.execute("SELECT 1")
        return conn
    except sqlite3.OperationalError as exc:
        log("ERROR", func, f"Cannot open production database read-only: {prod_path} ({exc})")
        sys.exit(1)


def backup_dev_db(dev_path: Path) -> Path:
    """
    <summary>
    Copies the dev database to a timestamped backup file next to it, before
    any write is committed.
    </summary>
    <param name="dev_path">Filesystem path to the dev record.db</param>
    <returns>Path to the created backup file</returns>
    """
    func = "sync_prod_to_dev.backup_dev_db"
    ts = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    backup_path = dev_path.with_name(dev_path.name + f".backup-{ts}")
    shutil.copy2(dev_path, backup_path)
    log("INFO", func, f"Backed up dev database to {backup_path}")
    return backup_path


# ---------------------------------------------------------------------------
# Per-table sync (insert-only, mirrors the find-or-create cascade in
# reference/mod_AddNewJobToDB.bas). Each function inserts brand-new rows
# directly into dev_conn's active transaction and returns (prod_id -> dev_id
# map, inserted count) so downstream tables can resolve foreign keys.
# ---------------------------------------------------------------------------

def sync_customers(prod_conn: sqlite3.Connection, dev_conn: sqlite3.Connection) -> tuple:
    """<summary>Inserts customers present in prod but missing in dev, matched by customer_name.</summary>"""
    func = "sync_prod_to_dev.sync_customers"
    id_map = {}
    inserted = 0
    rows = prod_conn.execute(
        "SELECT id, customer_name, usage_count, last_used FROM customer"
    ).fetchall()
    for prod_id, name, usage_count, last_used in rows:
        existing = dev_conn.execute(
            "SELECT id FROM customer WHERE customer_name = ?", (name,)
        ).fetchone()
        if existing:
            id_map[prod_id] = existing[0]
            continue
        cur = dev_conn.execute(
            "INSERT INTO customer (customer_name, usage_count, last_used, created_at, updated_at) "
            "VALUES (?, ?, ?, datetime('now','localtime'), datetime('now','localtime'))",
            (name, usage_count, last_used),
        )
        id_map[prod_id] = cur.lastrowid
        inserted += 1
    log("INFO", func, f"customer: {inserted} new row(s)")
    return id_map, inserted


def sync_customer_contacts(prod_conn: sqlite3.Connection, dev_conn: sqlite3.Connection, customer_map: dict) -> tuple:
    """<summary>Inserts customer_contact rows missing in dev, matched by (dev_customer_id, contact_name).</summary>"""
    func = "sync_prod_to_dev.sync_customer_contacts"
    id_map = {}
    inserted = 0
    rows = prod_conn.execute(
        "SELECT id, customer_id, contact_name, contact_email, usage_count, last_used FROM customer_contact"
    ).fetchall()
    for prod_id, prod_customer_id, name, email, usage_count, last_used in rows:
        dev_customer_id = customer_map.get(prod_customer_id)
        if dev_customer_id is None:
            log("WARN", func, f"Skipping contact '{name}' — prod customer_id {prod_customer_id} not resolved")
            continue
        existing = dev_conn.execute(
            "SELECT id FROM customer_contact WHERE customer_id = ? AND contact_name = ?",
            (dev_customer_id, name),
        ).fetchone()
        if existing:
            id_map[prod_id] = existing[0]
            continue
        cur = dev_conn.execute(
            "INSERT INTO customer_contact (customer_id, contact_name, contact_email, usage_count, last_used, created_at, updated_at) "
            "VALUES (?, ?, ?, ?, ?, datetime('now','localtime'), datetime('now','localtime'))",
            (dev_customer_id, name, email, usage_count, last_used),
        )
        id_map[prod_id] = cur.lastrowid
        inserted += 1
    log("INFO", func, f"customer_contact: {inserted} new row(s)")
    return id_map, inserted


def sync_purchase_orders(prod_conn: sqlite3.Connection, dev_conn: sqlite3.Connection, contact_map: dict) -> tuple:
    """<summary>Inserts purchase_order rows missing in dev, matched by po_number.</summary>"""
    func = "sync_prod_to_dev.sync_purchase_orders"
    id_map = {}
    inserted = 0
    rows = prod_conn.execute(
        "SELECT id, po_number, oe_number, contact_id, is_active, closed_at FROM purchase_order"
    ).fetchall()
    for prod_id, po_number, oe_number, prod_contact_id, is_active, closed_at in rows:
        existing = dev_conn.execute(
            "SELECT id FROM purchase_order WHERE po_number = ?", (po_number,)
        ).fetchone()
        if existing:
            id_map[prod_id] = existing[0]
            continue
        dev_contact_id = contact_map.get(prod_contact_id) if prod_contact_id is not None else None
        if prod_contact_id is not None and dev_contact_id is None:
            log("WARN", func, f"PO '{po_number}' — prod contact_id {prod_contact_id} not resolved, inserting with NULL contact_id")
        cur = dev_conn.execute(
            "INSERT INTO purchase_order (po_number, oe_number, contact_id, is_active, closed_at, created_at, updated_at) "
            "VALUES (?, ?, ?, ?, ?, datetime('now','localtime'), datetime('now','localtime'))",
            (po_number, oe_number, dev_contact_id, is_active, closed_at),
        )
        id_map[prod_id] = cur.lastrowid
        inserted += 1
    log("INFO", func, f"purchase_order: {inserted} new row(s)")
    return id_map, inserted


def sync_jobs(prod_conn: sqlite3.Connection, dev_conn: sqlite3.Connection, po_map: dict) -> tuple:
    """<summary>Inserts job rows missing in dev, matched by job_number.</summary>"""
    func = "sync_prod_to_dev.sync_jobs"
    id_map = {}
    inserted = 0
    rows = prod_conn.execute("SELECT id, job_number, po_id, priority FROM job").fetchall()
    for prod_id, job_number, prod_po_id, priority in rows:
        existing = dev_conn.execute(
            "SELECT id FROM job WHERE job_number = ?", (job_number,)
        ).fetchone()
        if existing:
            id_map[prod_id] = existing[0]
            continue
        dev_po_id = po_map.get(prod_po_id)
        if dev_po_id is None:
            log("ERROR", func, f"Skipping job '{job_number}' — prod po_id {prod_po_id} not resolved (job.po_id is required)")
            continue
        cur = dev_conn.execute(
            "INSERT INTO job (job_number, po_id, priority, created_at, updated_at) "
            "VALUES (?, ?, ?, datetime('now','localtime'), datetime('now','localtime'))",
            (job_number, dev_po_id, priority),
        )
        id_map[prod_id] = cur.lastrowid
        inserted += 1
    log("INFO", func, f"job: {inserted} new row(s)")
    return id_map, inserted


def sync_parts(prod_conn: sqlite3.Connection, dev_conn: sqlite3.Connection) -> tuple:
    """<summary>Inserts part rows missing in dev, matched by (drawing_number, revision).</summary>"""
    func = "sync_prod_to_dev.sync_parts"
    id_map = {}
    inserted = 0
    rows = prod_conn.execute(
        "SELECT id, drawing_number, revision, description, is_assembly, has_parent, "
        "production_count, total_production_hour, total_administrative_hour, unit_price FROM part"
    ).fetchall()
    for (prod_id, drawing_number, revision, description, is_assembly, has_parent,
         production_count, total_production_hour, total_administrative_hour, unit_price) in rows:
        existing = dev_conn.execute(
            "SELECT id FROM part WHERE drawing_number = ? AND revision = ?",
            (drawing_number, revision),
        ).fetchone()
        if existing:
            id_map[prod_id] = existing[0]
            continue
        cur = dev_conn.execute(
            "INSERT INTO part (drawing_number, revision, description, is_assembly, has_parent, "
            "production_count, total_production_hour, total_administrative_hour, unit_price, created_at, updated_at) "
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, datetime('now','localtime'), datetime('now','localtime'))",
            (drawing_number, revision, description, is_assembly, has_parent,
             production_count, total_production_hour, total_administrative_hour, unit_price),
        )
        id_map[prod_id] = cur.lastrowid
        inserted += 1
    log("INFO", func, f"part: {inserted} new row(s)")
    return id_map, inserted


def sync_order_items(prod_conn: sqlite3.Connection, dev_conn: sqlite3.Connection, job_map: dict, part_map: dict) -> int:
    """<summary>Inserts order_item rows missing in dev, matched by (dev_job_id, line_number).</summary>"""
    func = "sync_prod_to_dev.sync_order_items"
    inserted = 0
    rows = prod_conn.execute(
        "SELECT id, job_id, part_id, line_number, quantity, actual_price, production_hour, "
        "administrative_hour, status, drawing_release_date, delivery_required_date FROM order_item"
    ).fetchall()
    for (prod_id, prod_job_id, prod_part_id, line_number, quantity, actual_price, production_hour,
         administrative_hour, status, drawing_release_date, delivery_required_date) in rows:
        dev_job_id = job_map.get(prod_job_id)
        if dev_job_id is None:
            log("ERROR", func, f"Skipping order_item {prod_id} — prod job_id {prod_job_id} not resolved (order_item.job_id is required)")
            continue
        existing = dev_conn.execute(
            "SELECT id FROM order_item WHERE job_id = ? AND line_number = ?",
            (dev_job_id, line_number),
        ).fetchone()
        if existing:
            continue
        dev_part_id = part_map.get(prod_part_id) if prod_part_id is not None else None
        if prod_part_id is not None and dev_part_id is None:
            log("WARN", func, f"order_item {prod_id} — prod part_id {prod_part_id} not resolved, inserting with NULL part_id")
        dev_conn.execute(
            "INSERT INTO order_item (job_id, part_id, line_number, quantity, actual_price, production_hour, "
            "administrative_hour, status, drawing_release_date, delivery_required_date, created_at, updated_at) "
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, datetime('now','localtime'), datetime('now','localtime'))",
            (dev_job_id, dev_part_id, line_number, quantity, actual_price, production_hour,
             administrative_hour, status, drawing_release_date, delivery_required_date),
        )
        inserted += 1
    log("INFO", func, f"order_item: {inserted} new row(s)")
    return inserted


def run_sync(prod_conn: sqlite3.Connection, dev_conn: sqlite3.Connection) -> dict:
    """
    <summary>
    Runs the full 6-table sync cascade inside the caller's dev_conn
    transaction and returns a summary of rows inserted per table.
    </summary>
    <param name="prod_conn">Read-only connection to the production database</param>
    <param name="dev_conn">Read-write connection to the dev database, transaction already open</param>
    <returns>dict mapping table name to inserted row count</returns>
    """
    customer_map, customer_count = sync_customers(prod_conn, dev_conn)
    contact_map, contact_count = sync_customer_contacts(prod_conn, dev_conn, customer_map)
    po_map, po_count = sync_purchase_orders(prod_conn, dev_conn, contact_map)
    job_map, job_count = sync_jobs(prod_conn, dev_conn, po_map)
    part_map, part_count = sync_parts(prod_conn, dev_conn)
    order_item_count = sync_order_items(prod_conn, dev_conn, job_map, part_map)

    return {
        "customer": customer_count,
        "customer_contact": contact_count,
        "purchase_order": po_count,
        "job": job_count,
        "part": part_count,
        "order_item": order_item_count,
    }


def print_summary(changes: dict) -> None:
    """<summary>Prints a human-readable preview of rows inserted per table.</summary>"""
    func = "sync_prod_to_dev.print_summary"
    log("INFO", func, "Change summary:")
    total = 0
    for table, count in changes.items():
        print(f"  {table:<18}: {count} new row(s)")
        total += count
    print(f"  {'TOTAL':<18}: {total} new row(s)")


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

def main() -> None:
    """<summary>CLI entry point. Parses arguments and orchestrates the sync pipeline.</summary>"""
    func = "sync_prod_to_dev.main"

    parser = argparse.ArgumentParser(
        description="Copy new rows from the production database into the dev database (insert-only)."
    )
    parser.add_argument(
        "--apply",
        action="store_true",
        help="Commit changes to the dev database (default: dry-run only)",
    )
    parser.add_argument("--dev-db", default=str(DEV_DB), help="Override the dev database path")
    parser.add_argument("--prod-db", default=PROD_DB, help="Override the production database path")
    args = parser.parse_args()

    dev_path = Path(args.dev_db)
    log("INFO", func, f"Dev database : {dev_path}")
    log("INFO", func, f"Prod database: {args.prod_db}")
    log("INFO", func, f"Mode: {'APPLY' if args.apply else 'DRY-RUN'}")

    if not dev_path.exists():
        log("ERROR", func, f"Dev database not found: {dev_path}")
        sys.exit(1)

    prod_conn = open_prod_readonly(args.prod_db)
    dev_conn = sqlite3.connect(str(dev_path), isolation_level=None)
    try:
        if args.apply:
            backup_dev_db(dev_path)

        dev_conn.execute("BEGIN")
        try:
            changes = run_sync(prod_conn, dev_conn)
        except Exception:
            dev_conn.execute("ROLLBACK")
            raise

        print_summary(changes)

        if args.apply:
            dev_conn.execute("COMMIT")
            log("INFO", func, "Done.")
        else:
            dev_conn.execute("ROLLBACK")
            log("INFO", func, "Dry-run complete — pass --apply to commit changes")
    finally:
        prod_conn.close()
        dev_conn.close()


if __name__ == "__main__":
    main()
