/// <summary>
/// OeSyncRepository.cs
/// Database access for the OE sync feature (Issue #44): reads the current "active" snapshot
/// of order_item rows to diff against the OE Excel file, and applies human-approved changes.
/// Each Apply* method runs in its own transaction, independent of every other row — this is
/// what makes retry/cancel semantics in the review dialog safe (see OeSyncDialog).
/// </summary>

using System.Globalization;
using DrawingTree.Logging;
using DrawingTree.Models;
using DrawingTree.Services;
using Microsoft.Data.Sqlite;

namespace DrawingTree.Data;

public class OeSyncRepository
{
    /// <summary>
    /// Returns every order_item that is active (order_item.is_active = 1) and belongs to an
    /// active purchase_order (purchase_order.is_active = 1) — this is "set B" for OE sync diffing.
    /// </summary>
    public List<OeDbRow> GetActiveSnapshot()
    {
        var result = new List<OeDbRow>();
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT
                    oi.id, oi.job_id, j.job_number, po.id, po.po_number, po.oe_number,
                    c.customer_name, cc.contact_name,
                    p.id, p.drawing_number, p.revision, p.description,
                    oi.line_number, oi.quantity, oi.actual_price,
                    oi.drawing_release_date, oi.delivery_required_date
                FROM order_item oi
                JOIN job j ON j.id = oi.job_id
                JOIN purchase_order po ON po.id = j.po_id
                LEFT JOIN customer_contact cc ON cc.id = po.contact_id
                LEFT JOIN customer c ON c.id = cc.customer_id
                LEFT JOIN part p ON p.id = oi.part_id
                WHERE po.is_active = 1 AND oi.is_active = 1
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new OeDbRow
                {
                    OrderItemId = reader.GetInt64(0),
                    JobId = reader.GetInt64(1),
                    JobNumber = reader.GetString(2),
                    PoId = reader.GetInt64(3),
                    PoNumber = reader.GetString(4),
                    OeNumber = reader.IsDBNull(5) ? null : reader.GetString(5),
                    CustomerName = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    ContactName = reader.IsDBNull(7) ? "" : reader.GetString(7),
                    PartId = reader.IsDBNull(8) ? null : reader.GetInt64(8),
                    DrawingNumber = reader.IsDBNull(9) ? "" : reader.GetString(9),
                    Revision = reader.IsDBNull(10) ? "" : reader.GetString(10),
                    Description = reader.IsDBNull(11) ? null : reader.GetString(11),
                    // GetValue().ToString(), not GetInt32(): line_number/quantity are TEXT-affinity
                    // enough that some legacy rows hold non-numeric values (e.g. "1&2") — GetInt32
                    // would silently truncate those instead of preserving them for matching/display.
                    LineNumber = reader.GetValue(12)?.ToString() ?? "",
                    Quantity = reader.GetValue(13)?.ToString() ?? "",
                    ActualPrice = reader.IsDBNull(14) ? null : reader.GetDecimal(14),
                    DrawingReleaseDate = reader.IsDBNull(15) ? null : reader.GetString(15),
                    DeliveryRequiredDate = reader.IsDBNull(16) ? null : reader.GetString(16),
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"OeSyncRepository.GetActiveSnapshot failed: {ex.Message}");
            throw;
        }
        return result;
    }

    /// <summary>
    /// Returns the (job_number, line_number) key of every order_item in the database, regardless
    /// of its own or its PO's is_active status. Used to catch the case where an Excel row would
    /// otherwise be classified as "Add" but actually collides with an already-closed historical
    /// order_item — the OE file is an append-only log, so old rows for shipped/archived orders
    /// never disappear from it, and blindly re-inserting would violate UNIQUE(job_id, line_number).
    /// </summary>
    public HashSet<(string Job, string Line)> GetAllJobLineKeys()
    {
        var result = new HashSet<(string, string)>();
        using var conn = DatabaseConnectionFactory.OpenDevConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT j.job_number, oi.line_number FROM order_item oi JOIN job j ON j.id = oi.job_id";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add((
                OeNormalization.NormalizeJobNumber(reader.GetString(0)),
                OeNormalization.NormalizeLineNumber(reader.GetValue(1)?.ToString() ?? "")));
        }
        return result;
    }

    /// <summary>
    /// Applies an Add change: upserts the customer/contact/PO/job/part chain via PoRepository's
    /// internal helpers, then inserts the order_item directly in the same transaction. line_number
    /// and quantity are written via OeNormalization.ResolveQuantity/the raw M text rather than a
    /// forced int, since real OE data legitimately contains compound values ("3+5", "3+3", "Lot")
    /// that a single-integer NewJobInput cannot represent.
    /// </summary>
    public (bool Success, string? Error) ApplyAdd(OeSyncChange change)
    {
        var row = change.ExcelRow ?? throw new InvalidOperationException("ApplyAdd requires ExcelRow");

        if (string.IsNullOrWhiteSpace(row.LineNumber))
            return (false, "M column is blank");
        var (qtyStored, qtyIsNumeric) = OeNormalization.ResolveQuantity(row.Quantity);
        if (qtyStored.Length == 0)
            return (false, "Qty is blank");

        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var tx = conn.BeginTransaction();

            long customerId = PoRepository.UpsertCustomer(conn, tx, row.Customer);
            long contactId = PoRepository.UpsertContact(conn, tx, customerId, row.Contact);
            string poNumber = string.IsNullOrWhiteSpace(row.PoNumber) ? $"NPO-{row.JobNumber}" : row.PoNumber;
            long poId = PoRepository.UpsertPurchaseOrder(conn, tx, poNumber, row.OeNumber, contactId);
            long jobId = PoRepository.UpsertJob(conn, tx, row.JobNumber, poId);

            long? partId = null;
            if (!string.IsNullOrWhiteSpace(row.PartNumber))
                partId = PoRepository.UpsertPart(conn, tx, row.PartNumber, row.Revision, row.Description, "",
                    overwriteDescriptionOnConflict: false);

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO order_item
                    (job_id, part_id, line_number, quantity, actual_price, drawing_release_date, delivery_required_date, updated_at)
                VALUES
                    (@jobId, @partId, @ln, @qty, @price, @drd, @del, datetime('now','localtime'))
                """;
            cmd.Parameters.AddWithValue("@jobId", jobId);
            cmd.Parameters.AddWithValue("@partId", partId.HasValue ? (object)partId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@ln", row.LineNumber.Trim());
            cmd.Parameters.AddWithValue("@qty", qtyIsNumeric ? (object)int.Parse(qtyStored, CultureInfo.InvariantCulture) : qtyStored);
            cmd.Parameters.AddWithValue("@price", row.Price.HasValue ? (object)row.Price.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@drd", string.IsNullOrWhiteSpace(row.DrawingReleaseDate) ? DBNull.Value : (object)row.DrawingReleaseDate);
            cmd.Parameters.AddWithValue("@del", string.IsNullOrWhiteSpace(row.DeliveryRequiredDate) ? DBNull.Value : (object)row.DeliveryRequiredDate);
            cmd.ExecuteNonQuery();

            cmd.CommandText = "SELECT last_insert_rowid()";
            long newId = Convert.ToInt64(cmd.ExecuteScalar()!);

            tx.Commit();
            Logger.Instance.Info($"OeSyncRepository.ApplyAdd: created order_item id={newId}");
            return (true, null);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"OeSyncRepository.ApplyAdd failed: {ex.Message}");
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Applies a Modify change: updates the order_item's OE-sourced fields in one transaction.
    /// A genuine "P.O. :" difference (base PO number actually changed, not just a tolerated
    /// revision suffix — see OeNormalization.ArePoNumbersEquivalent) reassigns the owning job to
    /// the correct/looked-up purchase_order rather than renaming the existing PO record in place,
    /// so distinct PO revisions keep coexisting in storage as the user required.
    /// </summary>
    public (bool Success, string? Error) ApplyModify(OeSyncChange change)
    {
        var excelRow = change.ExcelRow ?? throw new InvalidOperationException("ApplyModify requires ExcelRow");
        var dbRow = change.DbRow ?? throw new InvalidOperationException("ApplyModify requires DbRow");

        var (qtyStored, qtyIsNumeric) = OeNormalization.ResolveQuantity(excelRow.Quantity);
        if (qtyStored.Length == 0)
            return (false, "Qty is blank");

        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var tx = conn.BeginTransaction();

            var fieldLabels = change.FieldChanges.Select(f => f.FieldLabel).ToHashSet();
            long targetPoId = dbRow.PoId;

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    UPDATE order_item
                    SET quantity = @qty, actual_price = @price,
                        drawing_release_date = @drd, delivery_required_date = @del,
                        updated_at = datetime('now','localtime')
                    WHERE id = @id
                    """;
                cmd.Parameters.AddWithValue("@qty", qtyIsNumeric ? (object)int.Parse(qtyStored, CultureInfo.InvariantCulture) : qtyStored);
                cmd.Parameters.AddWithValue("@price", excelRow.Price.HasValue ? (object)excelRow.Price.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@drd", string.IsNullOrWhiteSpace(excelRow.DrawingReleaseDate) ? DBNull.Value : (object)excelRow.DrawingReleaseDate);
                cmd.Parameters.AddWithValue("@del", string.IsNullOrWhiteSpace(excelRow.DeliveryRequiredDate) ? DBNull.Value : (object)excelRow.DeliveryRequiredDate);
                cmd.Parameters.AddWithValue("@id", dbRow.OrderItemId);
                cmd.ExecuteNonQuery();
            }

            if (fieldLabels.Contains("Part #") || fieldLabels.Contains("Rev") || fieldLabels.Contains("Descriptions:"))
            {
                // A blank Excel revision never clobbers a DB value that's already populated.
                var revision = string.IsNullOrWhiteSpace(excelRow.Revision) ? dbRow.Revision : excelRow.Revision;
                var partId = PoRepository.UpsertPart(conn, tx, excelRow.PartNumber, revision, excelRow.Description, "");
                using var linkCmd = conn.CreateCommand();
                linkCmd.Transaction = tx;
                linkCmd.CommandText = "UPDATE order_item SET part_id = @pid, updated_at = datetime('now','localtime') WHERE id = @id";
                linkCmd.Parameters.AddWithValue("@pid", partId);
                linkCmd.Parameters.AddWithValue("@id", dbRow.OrderItemId);
                linkCmd.ExecuteNonQuery();
            }

            if (fieldLabels.Contains("P.O. :"))
            {
                using var lookupCmd = conn.CreateCommand();
                lookupCmd.Transaction = tx;
                lookupCmd.CommandText = "SELECT contact_id FROM purchase_order WHERE id = @pid";
                lookupCmd.Parameters.AddWithValue("@pid", dbRow.PoId);
                long contactId = Convert.ToInt64(lookupCmd.ExecuteScalar() ?? 0L);

                targetPoId = PoRepository.UpsertPurchaseOrder(conn, tx, excelRow.PoNumber, excelRow.OeNumber, contactId);

                using var jobCmd = conn.CreateCommand();
                jobCmd.Transaction = tx;
                jobCmd.CommandText = "UPDATE job SET po_id = @pid, updated_at = datetime('now','localtime') WHERE id = @jid";
                jobCmd.Parameters.AddWithValue("@pid", targetPoId);
                jobCmd.Parameters.AddWithValue("@jid", dbRow.JobId);
                jobCmd.ExecuteNonQuery();
            }

            if (fieldLabels.Contains("O.E.:"))
            {
                using var oeCmd = conn.CreateCommand();
                oeCmd.Transaction = tx;
                oeCmd.CommandText = "UPDATE purchase_order SET oe_number = @oe, updated_at = datetime('now','localtime') WHERE id = @pid";
                oeCmd.Parameters.AddWithValue("@oe", string.IsNullOrWhiteSpace(excelRow.OeNumber) ? DBNull.Value : (object)excelRow.OeNumber);
                oeCmd.Parameters.AddWithValue("@pid", targetPoId);
                oeCmd.ExecuteNonQuery();
            }

            if (fieldLabels.Contains("Customer:") || fieldLabels.Contains("Contact:"))
            {
                long customerId = PoRepository.UpsertCustomer(conn, tx, excelRow.Customer);
                long contactId = PoRepository.UpsertContact(conn, tx, customerId, excelRow.Contact);
                using var poCmd = conn.CreateCommand();
                poCmd.Transaction = tx;
                poCmd.CommandText = "UPDATE purchase_order SET contact_id = @cid, updated_at = datetime('now','localtime') WHERE id = @pid";
                poCmd.Parameters.AddWithValue("@cid", contactId);
                poCmd.Parameters.AddWithValue("@pid", targetPoId);
                poCmd.ExecuteNonQuery();
            }

            tx.Commit();
            Logger.Instance.Info($"OeSyncRepository.ApplyModify: updated order_item id={dbRow.OrderItemId}");
            return (true, null);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"OeSyncRepository.ApplyModify failed for order_item id={dbRow.OrderItemId}: {ex.Message}");
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Applies a Deactivate change: marks the owning purchase_order and every one of its
    /// order_items inactive together ("shipped"), in a single transaction.
    /// </summary>
    public (bool Success, string? Error) ApplyDeactivate(OeSyncChange change)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var tx = conn.BeginTransaction();

            using (var poCmd = conn.CreateCommand())
            {
                poCmd.Transaction = tx;
                poCmd.CommandText = """
                    UPDATE purchase_order
                    SET is_active = 0, closed_at = datetime('now','localtime'), updated_at = datetime('now','localtime')
                    WHERE id = @id
                    """;
                poCmd.Parameters.AddWithValue("@id", change.PoId);
                poCmd.ExecuteNonQuery();
            }

            using (var itemCmd = conn.CreateCommand())
            {
                itemCmd.Transaction = tx;
                itemCmd.CommandText = "UPDATE order_item SET is_active = 0, updated_at = datetime('now','localtime') WHERE id = @id";
                var idParam = itemCmd.CreateParameter();
                idParam.ParameterName = "@id";
                itemCmd.Parameters.Add(idParam);
                foreach (var item in change.DeactivateItems)
                {
                    idParam.Value = item.OrderItemId;
                    itemCmd.ExecuteNonQuery();
                }
            }

            tx.Commit();
            Logger.Instance.Info($"OeSyncRepository.ApplyDeactivate: poId={change.PoId}, items={change.DeactivateItems.Count}");
            return (true, null);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"OeSyncRepository.ApplyDeactivate failed for poId={change.PoId}: {ex.Message}");
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Resolves an Anomaly change backed by an existing order_item: the human has confirmed this
    /// one item should also be treated as inactive/shipped, without touching its sibling items or
    /// the owning PO. Anomalies with no DbRow (pure Excel parse warnings) cannot be applied here —
    /// the source data must be corrected first.
    /// </summary>
    public (bool Success, string? Error) ApplyAnomaly(OeSyncChange change)
    {
        if (change.OrderItemId == null)
            return (false, "This anomaly has no actionable database record — the OE file or database must be corrected manually, then re-run sync");

        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE order_item SET is_active = 0, updated_at = datetime('now','localtime') WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", change.OrderItemId.Value);
            cmd.ExecuteNonQuery();
            Logger.Instance.Info($"OeSyncRepository.ApplyAnomaly: marked order_item id={change.OrderItemId} inactive (manual resolution)");
            return (true, null);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"OeSyncRepository.ApplyAnomaly failed for order_item id={change.OrderItemId}: {ex.Message}");
            return (false, ex.Message);
        }
    }
}
