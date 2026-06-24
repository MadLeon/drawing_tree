"""
<file>
  <name>update_po_is_active.py</name>
  <description>
    Full-sync script that reads active order_item IDs from the OE Excel log
    (column AA, read-only), resolves the corresponding purchase_order records
    via order_item -> job -> purchase_order, and updates purchase_order.is_active
    (1 = active, 0 = inactive).  Runs in dry-run mode by default; pass --apply
    to commit changes.
  </description>
  <usage>
    python scripts/update_po_is_active.py                   # dry-run (default)
    python scripts/update_po_is_active.py --apply           # write to dev DB
    python scripts/update_po_is_active.py --db prod --apply # write to prod DB
    python scripts/update_po_is_active.py --excel PATH      # override Excel path
  </usage>
  <dependencies>openpyxl (third-party), sqlite3 (stdlib), argparse (stdlib)</dependencies>
</file>
"""

import argparse
import datetime
import inspect
import os
import sqlite3
import sys
from pathlib import Path

import openpyxl

# ---------------------------------------------------------------------------
# Configuration — edit DB paths here; do not hard-code usernames
# ---------------------------------------------------------------------------
EXCEL_PATH = r"\\rtdnas2\OE\Order Entry Log.xlsm"
DEV_DB = Path(os.environ["USERPROFILE"]) / "drawing_tree" / "data" / "record.db"
PROD_DB = r"\\rtdnas2\OE\record.db"

AA_COL_IDX = 27  # column AA is the 27th column (1-based)
HEADER_ROWS = 1  # number of header rows to skip


# ---------------------------------------------------------------------------
# Logging helper
# ---------------------------------------------------------------------------

def log(level: str, func_name: str, message: str) -> None:
    """
    <summary>
    Prints a log line to stdout using the project's standard format.
    </summary>
    <param name="level">Severity: INFO, WARN, or ERROR</param>
    <param name="func_name">Caller identifier, e.g. 'update_po_is_active.main'</param>
    <param name="message">Human-readable message</param>
    """
    ts = datetime.datetime.now().strftime("%Y-%m-%dT%H:%M:%S")
    print(f"{ts} [{level}] [{func_name}] - {message}")


# ---------------------------------------------------------------------------
# Excel reader
# ---------------------------------------------------------------------------

def read_active_order_item_ids(excel_path: str) -> set:
    """
    <summary>
    Opens the OE Excel log in read-only mode and returns the set of integer
    order_item IDs found in column AA.  Skips header rows and non-integer cells.
    Works even when the file is already open in Excel by another user.
    </summary>
    <param name="excel_path">Full path to the .xlsm file</param>
    <returns>set[int] of order_item IDs listed in column AA</returns>
    <exception cref="SystemExit">Exits with code 1 if the file cannot be opened</exception>
    """
    func = "update_po_is_active.read_active_order_item_ids"
    log("INFO", func, f"Opening Excel file (read-only): {excel_path}")

    try:
        # read_only=True reads the zip stream without requesting an exclusive lock.
        # data_only=True returns cached cell values instead of formula strings.
        # keep_vba=False avoids loading the VBA binary (irrelevant here).
        wb = openpyxl.load_workbook(excel_path, read_only=True, data_only=True, keep_vba=False)
    except FileNotFoundError:
        log("ERROR", func, f"File not found: {excel_path}")
        log("ERROR", func, "Ensure the network path is accessible and the filename is correct.")
        sys.exit(1)
    except Exception as exc:
        log("ERROR", func, f"Failed to open Excel file: {exc}")
        sys.exit(1)

    ws = wb.worksheets[0]
    ids: set = set()
    skipped = 0

    for row_idx, row in enumerate(ws.iter_rows(min_col=AA_COL_IDX, max_col=AA_COL_IDX), start=1):
        if row_idx <= HEADER_ROWS:
            continue
        cell_value = row[0].value
        if cell_value is None:
            continue
        try:
            ids.add(int(cell_value))
        except (ValueError, TypeError):
            skipped += 1

    wb.close()

    log("INFO", func, f"Found {len(ids)} order_item IDs in column AA (skipped {skipped} non-integer cells)")
    return ids


# ---------------------------------------------------------------------------
# Database helpers
# ---------------------------------------------------------------------------

def resolve_po_ids_from_order_items(conn: sqlite3.Connection, order_item_ids: set) -> set:
    """
    <summary>
    Given a set of order_item.id values, returns the set of purchase_order.id
    values that own at least one of those order items.
    Uses a temporary table to avoid SQLite's IN-clause variable limit.
    </summary>
    <param name="conn">Open SQLite connection</param>
    <param name="order_item_ids">set[int] of active order_item IDs</param>
    <returns>set[int] of purchase_order IDs that should be active</returns>
    """
    func = "update_po_is_active.resolve_po_ids_from_order_items"

    if not order_item_ids:
        log("WARN", func, "No order_item IDs provided — all POs will be set inactive")
        return set()

    conn.execute("CREATE TEMP TABLE IF NOT EXISTS _active_oi (id INTEGER PRIMARY KEY)")
    conn.execute("DELETE FROM _active_oi")
    conn.executemany("INSERT INTO _active_oi VALUES (?)", [(i,) for i in order_item_ids])

    rows = conn.execute("""
        SELECT DISTINCT po.id
        FROM purchase_order po
        JOIN job j         ON j.po_id   = po.id
        JOIN order_item oi ON oi.job_id = j.id
        JOIN _active_oi ai ON ai.id     = oi.id
    """).fetchall()

    conn.execute("DROP TABLE IF EXISTS _active_oi")

    po_ids = {row[0] for row in rows}
    log("INFO", func, f"Resolved {len(po_ids)} active POs from {len(order_item_ids)} order_item IDs")
    return po_ids


def compute_changes(conn: sqlite3.Connection, active_po_ids: set) -> dict:
    """
    <summary>
    Compares the desired active PO set against current DB state and returns
    a dict describing which POs need to change.
    </summary>
    <param name="conn">Open SQLite connection</param>
    <param name="active_po_ids">set[int] of PO IDs that should be active</param>
    <returns>
      dict with keys: to_activate (set), to_deactivate (set),
      already_active (int), already_inactive (int)
    </returns>
    """
    rows = conn.execute("SELECT id, is_active FROM purchase_order").fetchall()

    to_activate: set = set()
    to_deactivate: set = set()
    already_active = 0
    already_inactive = 0

    for po_id, is_active in rows:
        should_be_active = po_id in active_po_ids
        currently_active = bool(is_active)

        if should_be_active and not currently_active:
            to_activate.add(po_id)
        elif not should_be_active and currently_active:
            to_deactivate.add(po_id)
        elif should_be_active and currently_active:
            already_active += 1
        else:
            already_inactive += 1

    return {
        "to_activate": to_activate,
        "to_deactivate": to_deactivate,
        "already_active": already_active,
        "already_inactive": already_inactive,
    }


def print_summary(changes: dict) -> None:
    """
    <summary>Prints a human-readable preview of the pending changes.</summary>
    <param name="changes">dict returned by compute_changes()</param>
    """
    func = "update_po_is_active.print_summary"
    log("INFO", func, "Change summary:")
    print(f"  POs to set ACTIVE    : {len(changes['to_activate'])}")
    print(f"  POs to set INACTIVE  : {len(changes['to_deactivate'])}")
    print(f"  Already active (ok)  : {changes['already_active']}")
    print(f"  Already inactive (ok): {changes['already_inactive']}")


def apply_changes(conn: sqlite3.Connection, changes: dict) -> None:
    """
    <summary>
    Executes the UPDATE statements inside a single transaction.
    Rolls back and re-raises on any error.
    </summary>
    <param name="conn">Open SQLite connection</param>
    <param name="changes">dict returned by compute_changes()</param>
    """
    func = "update_po_is_active.apply_changes"

    to_activate = [(1, po_id) for po_id in changes["to_activate"]]
    to_deactivate = [(0, po_id) for po_id in changes["to_deactivate"]]
    all_updates = to_activate + to_deactivate

    if not all_updates:
        log("INFO", func, "No changes needed — database already in sync")
        return

    sql = "UPDATE purchase_order SET is_active = ?, updated_at = datetime('now', 'localtime') WHERE id = ?"

    try:
        conn.execute("BEGIN")
        conn.executemany(sql, all_updates)
        conn.execute("COMMIT")
        log("INFO", func, f"Applied {len(to_activate)} activations and {len(to_deactivate)} deactivations")
    except Exception as exc:
        conn.execute("ROLLBACK")
        log("ERROR", func, f"Transaction rolled back due to error: {exc}")
        raise


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

def main() -> None:
    """
    <summary>CLI entry point. Parses arguments and orchestrates the sync pipeline.</summary>
    """
    func = "update_po_is_active.main"

    parser = argparse.ArgumentParser(
        description="Sync purchase_order.is_active from the OE Excel log."
    )
    parser.add_argument(
        "--apply",
        action="store_true",
        help="Commit changes to the database (default: dry-run only)",
    )
    parser.add_argument(
        "--db",
        choices=["dev", "prod"],
        default="dev",
        help="Target database: 'dev' (default) or 'prod'",
    )
    parser.add_argument(
        "--excel",
        default=EXCEL_PATH,
        help="Override the Excel file path",
    )
    args = parser.parse_args()

    db_path = str(DEV_DB) if args.db == "dev" else PROD_DB
    log("INFO", func, f"Target database: {db_path}")
    log("INFO", func, f"Mode: {'APPLY' if args.apply else 'DRY-RUN'}")

    # Step 1: read active IDs from Excel
    active_oi_ids = read_active_order_item_ids(args.excel)

    # Step 2: open DB and resolve POs
    # isolation_level=None = autocommit mode; all transactions are managed explicitly
    # via BEGIN/COMMIT/ROLLBACK, avoiding conflicts with implicit sqlite3 transactions.
    conn = sqlite3.connect(db_path, isolation_level=None)
    try:
        active_po_ids = resolve_po_ids_from_order_items(conn, active_oi_ids)

        # Step 3: compute what needs to change
        changes = compute_changes(conn, active_po_ids)
        print_summary(changes)

        # Step 4: apply or report
        if args.apply:
            apply_changes(conn, changes)
            log("INFO", func, "Done.")
        else:
            log("INFO", func, "Dry-run complete — pass --apply to commit changes")
    finally:
        conn.close()


if __name__ == "__main__":
    main()
