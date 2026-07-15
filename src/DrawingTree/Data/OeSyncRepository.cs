/// <summary>
/// OeSyncRepository.cs
/// Database access for the OE sync feature (Issue #44): reads the current "active" snapshot
/// of order_item rows to diff against the OE Excel file, and applies/undoes human-approved
/// changes. Each Apply*/Undo* method runs in its own transaction, independent of every other
/// row — this is what makes retry/cancel/undo semantics in the review dialog safe (see
/// OeSyncDialog).
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
                    p.id, p.drawing_number, p.revision, oi.description,
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
                    // order_item.description, not part.description: the OE file's Description text
                    // is per-order, not a physical-part attribute — see db_changes.sql 2026-07-14.
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
    /// Looks up one order_item by (job_number, line_number) regardless of its own or its PO's
    /// is_active status — the counterpart to GetActiveSnapshot's active-only query, used by
    /// OeAnomalyEditDialog when the reviewer saves a "Job # + M already exists (inactive)" Anomaly:
    /// that row must be reactivated and updated in place via ApplyModify, not inserted, or it would
    /// violate UNIQUE(order_item.job_id, order_item.line_number).
    /// </summary>
    /// <returns>Null if no order_item exists for this key. Otherwise the row plus whether the item
    /// and/or its owning PO were inactive (so the caller knows what ApplyModify must reactivate).</returns>
    public (OeDbRow Row, bool ItemWasInactive, bool PoWasInactive)? FindOrderItemAnyStatus(string? jobNumber, string? lineNumber)
    {
        var job = OeNormalization.NormalizeJobNumber(jobNumber);
        var line = OeNormalization.NormalizeLineNumber(lineNumber);
        if (job.Length == 0 || line.Length == 0) return null;

        using var conn = DatabaseConnectionFactory.OpenDevConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                oi.id, oi.job_id, j.job_number, po.id, po.po_number, po.oe_number,
                c.customer_name, cc.contact_name,
                p.id, p.drawing_number, p.revision, oi.description,
                oi.line_number, oi.quantity, oi.actual_price,
                oi.drawing_release_date, oi.delivery_required_date,
                oi.is_active, po.is_active
            FROM order_item oi
            JOIN job j ON j.id = oi.job_id
            JOIN purchase_order po ON po.id = j.po_id
            LEFT JOIN customer_contact cc ON cc.id = po.contact_id
            LEFT JOIN customer c ON c.id = cc.customer_id
            LEFT JOIN part p ON p.id = oi.part_id
            WHERE j.job_number = @job COLLATE NOCASE
            """;
        cmd.Parameters.AddWithValue("@job", job);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var rowLine = OeNormalization.NormalizeLineNumber(reader.GetValue(12)?.ToString() ?? "");
            if (rowLine != line) continue;

            var row = new OeDbRow
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
                LineNumber = reader.GetValue(12)?.ToString() ?? "",
                Quantity = reader.GetValue(13)?.ToString() ?? "",
                ActualPrice = reader.IsDBNull(14) ? null : reader.GetDecimal(14),
                DrawingReleaseDate = reader.IsDBNull(15) ? null : reader.GetString(15),
                DeliveryRequiredDate = reader.IsDBNull(16) ? null : reader.GetString(16),
            };
            bool itemWasInactive = reader.GetInt64(17) == 0;
            bool poWasInactive = reader.GetInt64(18) == 0;
            return (row, itemWasInactive, poWasInactive);
        }
        return null;
    }

    /// <summary>
    /// Returns every order_item in the database keyed by (job_number, line_number), regardless of
    /// its own or its PO's is_active status, with the full row plus each item's/PO's active flag.
    /// Used so ComputeDiff can recognize a natural-key match to an archived order_item — a
    /// purchase_order's active status is fully derived from its order_items, so such a match is
    /// folded straight into a normal Modify with reactivation, not routed through Add (which would
    /// violate UNIQUE(job_id, line_number) against the still-present archived row) or a separate
    /// Anomaly review step — see OeSyncChange.NeedsItemReactivation/NeedsPoReactivation.
    /// </summary>
    public Dictionary<(string Job, string Line), (OeDbRow Row, bool ItemInactive, bool PoInactive)> GetAllOrderItemsByKey()
    {
        var result = new Dictionary<(string, string), (OeDbRow, bool, bool)>();
        using var conn = DatabaseConnectionFactory.OpenDevConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                oi.id, oi.job_id, j.job_number, po.id, po.po_number, po.oe_number,
                c.customer_name, cc.contact_name,
                p.id, p.drawing_number, p.revision, oi.description,
                oi.line_number, oi.quantity, oi.actual_price,
                oi.drawing_release_date, oi.delivery_required_date,
                oi.is_active, po.is_active
            FROM order_item oi
            JOIN job j ON j.id = oi.job_id
            JOIN purchase_order po ON po.id = j.po_id
            LEFT JOIN customer_contact cc ON cc.id = po.contact_id
            LEFT JOIN customer c ON c.id = cc.customer_id
            LEFT JOIN part p ON p.id = oi.part_id
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var row = new OeDbRow
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
                LineNumber = reader.GetValue(12)?.ToString() ?? "",
                Quantity = reader.GetValue(13)?.ToString() ?? "",
                ActualPrice = reader.IsDBNull(14) ? null : reader.GetDecimal(14),
                DrawingReleaseDate = reader.IsDBNull(15) ? null : reader.GetString(15),
                DeliveryRequiredDate = reader.IsDBNull(16) ? null : reader.GetString(16),
            };
            var key = (OeNormalization.NormalizeJobNumber(row.JobNumber), OeNormalization.NormalizeLineNumber(row.LineNumber));
            result[key] = (row, reader.GetInt64(17) == 0, reader.GetInt64(18) == 0);
        }
        return result;
    }

    /// <summary>
    /// Returns every "handled" (ignored/corrected) anomaly memory record, keyed by normalized
    /// (job_number, line_number) — see oe_anomaly_handled in data/db_changes.sql.
    /// </summary>
    public Dictionary<(string Job, string Line), OeAnomalyHandled> GetHandledAnomalies()
    {
        var result = new Dictionary<(string, string), OeAnomalyHandled>();
        using var conn = DatabaseConnectionFactory.OpenDevConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT job_number, line_number, anomaly_reason, fingerprint, action, handled_at FROM oe_anomaly_handled";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result[(reader.GetString(0), reader.GetString(1))] = new OeAnomalyHandled
            {
                AnomalyReason = reader.GetString(2),
                Fingerprint = reader.GetString(3),
                Action = reader.GetString(4),
                HandledAt = DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
            };
        }
        return result;
    }

    /// <summary>
    /// Records that a human reviewed <paramref name="change"/> (Ignore click, or a successful
    /// Update of a correctable Anomaly) so future OE sync runs recognizing the same
    /// Fingerprint — the same proposed field value(s), not just the same (job_number, line_number)
    /// — can suppress it instead of re-surfacing it as brand new. Blank job numbers are never
    /// persisted — see OeSyncService.AddAnomaly, the key would collide across unrelated rows.
    /// Best-effort: a failure here does not affect the caller's Ignore/Update, which has already
    /// succeeded by the time this runs.
    /// </summary>
    public void RecordAnomalyHandled(OeSyncChange change, string action)
    {
        if (change.Kind == OeSyncChangeKind.Deactivate)
        {
            RecordDeactivateHandled(change, action);
            return;
        }

        // HandledJobNumber/HandledLineNumber (frozen at diff time), not ExcelRow/DbRow — by the
        // time this runs for a hand-corrected Anomaly, OeAnomalyEditDialog has already replaced
        // ExcelRow with the reviewer's edited values, so deriving the key from it would write under
        // whatever value the reviewer just typed instead of the value this change was detected
        // under, silently colliding with (and clobbering) an unrelated row's own memory at that key.
        var jobNumber = change.HandledJobNumber;
        var lineNumber = change.HandledLineNumber;
        if (jobNumber.Length == 0) return;

        // Modify-kind changes carry no free-text AnomalyReason — build a readable summary from the
        // field diffs instead, purely for debugging/display; matching itself uses Fingerprint.
        var summary = change.AnomalyReason.Length > 0
            ? change.AnomalyReason
            : string.Join("; ", change.FieldChanges.Select(f => $"{f.FieldLabel} -> {f.NewValue}"));

        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO oe_anomaly_handled (job_number, line_number, anomaly_reason, fingerprint, action, handled_at)
                VALUES (@job, @line, @reason, @fingerprint, @action, datetime('now','localtime'))
                ON CONFLICT(job_number, line_number) DO UPDATE SET
                    anomaly_reason = excluded.anomaly_reason,
                    fingerprint = excluded.fingerprint,
                    action = excluded.action,
                    handled_at = excluded.handled_at
                """;
            cmd.Parameters.AddWithValue("@job", jobNumber);
            cmd.Parameters.AddWithValue("@line", lineNumber);
            cmd.Parameters.AddWithValue("@reason", summary);
            cmd.Parameters.AddWithValue("@fingerprint", change.Fingerprint);
            cmd.Parameters.AddWithValue("@action", action);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"OeSyncRepository.RecordAnomalyHandled failed: {ex.Message}");
        }
    }

    /// <summary>
    /// A Deactivate change is one PO-level group of order_items, not a single (job_number,
    /// line_number) — so it's remembered as one oe_anomaly_handled row PER item in the group, all
    /// sharing the same group-membership Fingerprint (see OeSyncService.ComputeDeactivateFingerprint).
    /// If the group's membership changes next sync (an item joins or leaves), at least one item's
    /// fingerprint stops matching and the whole group correctly resurfaces for review — see the
    /// `items.All(...)` check in OeSyncService.ComputeDiff's Deactivate branch.
    /// </summary>
    private void RecordDeactivateHandled(OeSyncChange change, string action)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var tx = conn.BeginTransaction();
            foreach (var item in change.DeactivateItems)
            {
                var jobNumber = OeNormalization.NormalizeJobNumber(item.JobNumber);
                var lineNumber = OeNormalization.NormalizeLineNumber(item.LineNumber);
                if (jobNumber.Length == 0) continue;

                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO oe_anomaly_handled (job_number, line_number, anomaly_reason, fingerprint, action, handled_at)
                    VALUES (@job, @line, @reason, @fingerprint, @action, datetime('now','localtime'))
                    ON CONFLICT(job_number, line_number) DO UPDATE SET
                        anomaly_reason = excluded.anomaly_reason,
                        fingerprint = excluded.fingerprint,
                        action = excluded.action,
                        handled_at = excluded.handled_at
                    """;
                cmd.Parameters.AddWithValue("@job", jobNumber);
                cmd.Parameters.AddWithValue("@line", lineNumber);
                cmd.Parameters.AddWithValue("@reason", change.HeaderText);
                cmd.Parameters.AddWithValue("@fingerprint", change.Fingerprint);
                cmd.Parameters.AddWithValue("@action", action);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"OeSyncRepository.RecordDeactivateHandled failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes a "handled" anomaly memory record — called when an Undo reverses the correction
    /// that created it (see OeSyncDialog.RowUndoButton_Click). Ship-time cleanup (the order_item
    /// itself going inactive) is handled inline inside ApplyDeactivate/ApplyAnomaly instead, since
    /// that must be atomic with the is_active write.
    /// </summary>
    public void DeleteHandledAnomaly(string? jobNumber, string? lineNumber)
    {
        var job = OeNormalization.NormalizeJobNumber(jobNumber);
        var line = OeNormalization.NormalizeLineNumber(lineNumber);
        if (job.Length == 0) return;

        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM oe_anomaly_handled WHERE job_number = @job AND line_number = @line";
            cmd.Parameters.AddWithValue("@job", job);
            cmd.Parameters.AddWithValue("@line", line);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"OeSyncRepository.DeleteHandledAnomaly failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies an Add change: upserts the customer/contact/PO/job/part chain via PoRepository's
    /// internal helpers, then inserts the order_item directly in the same transaction. line_number
    /// and quantity are written via OeNormalization.ResolveQuantity/the raw M text rather than a
    /// forced int, since real OE data legitimately contains compound values ("3+5", "3+3", "Lot")
    /// that a single-integer NewJobInput cannot represent.
    /// </summary>
    /// <param name="change">The Add (or hand-corrected-Anomaly-as-Add) change to apply.</param>
    public (bool Success, string? Error, long? NewOrderItemId) ApplyAdd(OeSyncChange change)
    {
        var row = change.ExcelRow ?? throw new InvalidOperationException("ApplyAdd requires ExcelRow");

        var effectiveLineNumber = row.LineNumber.Trim();
        if (string.IsNullOrWhiteSpace(effectiveLineNumber))
            return (false, "M column is blank", null);

        var (qtyStored, qtyIsNumeric) = OeNormalization.ResolveQuantity(row.Quantity);
        if (qtyStored.Length == 0)
            return (false, "Qty is blank", null);

        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var tx = conn.BeginTransaction();

            long customerId = PoRepository.UpsertCustomer(conn, tx, row.Customer);
            long contactId = PoRepository.UpsertContact(conn, tx, customerId, row.Contact);
            string poNumber = OeNormalization.ResolvePoNumber(row.PoNumber, row.JobNumber);
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
                    (job_id, part_id, line_number, quantity, actual_price, drawing_release_date, delivery_required_date, description, updated_at)
                VALUES
                    (@jobId, @partId, @ln, @qty, @price, @drd, @del, @desc, datetime('now','localtime'))
                """;
            cmd.Parameters.AddWithValue("@jobId", jobId);
            cmd.Parameters.AddWithValue("@partId", partId.HasValue ? (object)partId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@ln", effectiveLineNumber);
            cmd.Parameters.AddWithValue("@qty", qtyIsNumeric ? (object)int.Parse(qtyStored, CultureInfo.InvariantCulture) : qtyStored);
            cmd.Parameters.AddWithValue("@price", row.Price.HasValue ? (object)row.Price.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@drd", string.IsNullOrWhiteSpace(row.DrawingReleaseDate) ? DBNull.Value : (object)row.DrawingReleaseDate);
            cmd.Parameters.AddWithValue("@del", string.IsNullOrWhiteSpace(row.DeliveryRequiredDate) ? DBNull.Value : (object)row.DeliveryRequiredDate);
            cmd.Parameters.AddWithValue("@desc", string.IsNullOrWhiteSpace(row.Description) ? DBNull.Value : (object)row.Description);
            cmd.ExecuteNonQuery();

            cmd.CommandText = "SELECT last_insert_rowid()";
            long newId = Convert.ToInt64(cmd.ExecuteScalar()!);

            tx.Commit();
            Logger.Instance.Info($"OeSyncRepository.ApplyAdd: created order_item id={newId}");
            return (true, null, newId);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"OeSyncRepository.ApplyAdd failed: {ex.Message}");
            return (false, ex.Message, null);
        }
    }

    /// <summary>
    /// Deletes the order_item created by a prior ApplyAdd (including a corrected-Anomaly-as-Add).
    /// Does not touch the customer/contact/PO/job/part rows that Add's upsert chain may have
    /// created — those are left in place rather than reference-counted away, a deliberate
    /// minimal-scope limitation of Undo.
    /// </summary>
    public (bool Success, string? Error) UndoAdd(OeSyncChange change)
    {
        if (change.CreatedOrderItemId == null)
            return (false, "This Add has not been applied yet");

        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM order_item WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", change.CreatedOrderItemId.Value);
            cmd.ExecuteNonQuery();
            Logger.Instance.Info($"OeSyncRepository.UndoAdd: deleted order_item id={change.CreatedOrderItemId}");
            return (true, null);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"OeSyncRepository.UndoAdd failed for order_item id={change.CreatedOrderItemId}: {ex.Message}");
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
    /// <param name="change">The Modify (or hand-corrected-Anomaly-as-Modify) change to apply.</param>
    /// <param name="reactivateItem">
    /// True when this Modify is really a "Job # + M already exists but inactive" Anomaly the
    /// reviewer chose to reactivate (see OeAnomalyEditDialog/FindOrderItemAnyStatus) — sets
    /// order_item.is_active back to 1 alongside the normal field writes.
    /// </param>
    /// <param name="reactivatePo">
    /// Same, for the owning purchase_order — only needed when the PO itself was also inactive
    /// (see the invariant that an inactive PO's items must all be inactive).
    /// </param>
    public (bool Success, string? Error) ApplyModify(OeSyncChange change, bool reactivateItem = false, bool reactivatePo = false)
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
                cmd.CommandText = reactivateItem
                    ? """
                        UPDATE order_item
                        SET quantity = @qty, actual_price = @price,
                            drawing_release_date = @drd, delivery_required_date = @del,
                            description = @desc,
                            is_active = 1, updated_at = datetime('now','localtime')
                        WHERE id = @id
                        """
                    : """
                        UPDATE order_item
                        SET quantity = @qty, actual_price = @price,
                            drawing_release_date = @drd, delivery_required_date = @del,
                            description = @desc,
                            updated_at = datetime('now','localtime')
                        WHERE id = @id
                        """;
                cmd.Parameters.AddWithValue("@qty", qtyIsNumeric ? (object)int.Parse(qtyStored, CultureInfo.InvariantCulture) : qtyStored);
                cmd.Parameters.AddWithValue("@price", excelRow.Price.HasValue ? (object)excelRow.Price.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@drd", string.IsNullOrWhiteSpace(excelRow.DrawingReleaseDate) ? DBNull.Value : (object)excelRow.DrawingReleaseDate);
                cmd.Parameters.AddWithValue("@del", string.IsNullOrWhiteSpace(excelRow.DeliveryRequiredDate) ? DBNull.Value : (object)excelRow.DeliveryRequiredDate);
                // order_item.description, not part.description — it's per-order text, and this
                // shared part row may back other order_items with their own different description
                // (see db_changes.sql 2026-07-14).
                cmd.Parameters.AddWithValue("@desc", string.IsNullOrWhiteSpace(excelRow.Description) ? DBNull.Value : (object)excelRow.Description);
                cmd.Parameters.AddWithValue("@id", dbRow.OrderItemId);
                cmd.ExecuteNonQuery();
            }

            if (reactivatePo)
            {
                using var poCmd = conn.CreateCommand();
                poCmd.Transaction = tx;
                poCmd.CommandText = "UPDATE purchase_order SET is_active = 1, updated_at = datetime('now','localtime') WHERE id = @id";
                poCmd.Parameters.AddWithValue("@id", dbRow.PoId);
                poCmd.ExecuteNonQuery();
            }

            if (fieldLabels.Contains("Part #") || fieldLabels.Contains("Rev"))
            {
                // A blank Excel revision never clobbers a DB value that's already populated.
                // overwriteDescriptionOnConflict: false — part.description is only ever seeded on
                // first creation; Description itself is written above, directly on order_item.
                var revision = string.IsNullOrWhiteSpace(excelRow.Revision) ? dbRow.Revision : excelRow.Revision;
                var partId = PoRepository.UpsertPart(conn, tx, excelRow.PartNumber, revision, excelRow.Description, "",
                    overwriteDescriptionOnConflict: false);
                using var linkCmd = conn.CreateCommand();
                linkCmd.Transaction = tx;
                linkCmd.CommandText = "UPDATE order_item SET part_id = @pid, updated_at = datetime('now','localtime') WHERE id = @id";
                linkCmd.Parameters.AddWithValue("@pid", partId);
                linkCmd.Parameters.AddWithValue("@id", dbRow.OrderItemId);
                linkCmd.ExecuteNonQuery();
            }

            if (fieldLabels.Contains("M (line #):"))
            {
                using var lineCmd = conn.CreateCommand();
                lineCmd.Transaction = tx;
                lineCmd.CommandText = "UPDATE order_item SET line_number = @ln, updated_at = datetime('now','localtime') WHERE id = @id";
                lineCmd.Parameters.AddWithValue("@ln", excelRow.LineNumber.Trim());
                lineCmd.Parameters.AddWithValue("@id", dbRow.OrderItemId);
                lineCmd.ExecuteNonQuery();
            }

            if (fieldLabels.Contains("Job #:"))
            {
                // A corrected Job # (see OeSyncService.ComputeDiff's cross-job rename-candidate
                // pass) — reassign this order_item to the corrected/looked-up job, same PO as
                // before (targetPoId hasn't been reassigned yet at this point; this pass never
                // proposes a simultaneous P.O. change alongside a Job # correction — see its doc
                // comment). The old job row, even if it now has zero order_items, is left in place,
                // same as a PO reassignment leaves the old purchase_order row in place.
                var targetJobId = PoRepository.UpsertJob(conn, tx, excelRow.JobNumber.Trim(), targetPoId);
                using var jobMoveCmd = conn.CreateCommand();
                jobMoveCmd.Transaction = tx;
                jobMoveCmd.CommandText = "UPDATE order_item SET job_id = @jid, updated_at = datetime('now','localtime') WHERE id = @id";
                jobMoveCmd.Parameters.AddWithValue("@jid", targetJobId);
                jobMoveCmd.Parameters.AddWithValue("@id", dbRow.OrderItemId);
                jobMoveCmd.ExecuteNonQuery();
            }

            if (fieldLabels.Contains("P.O. :"))
            {
                using var lookupCmd = conn.CreateCommand();
                lookupCmd.Transaction = tx;
                lookupCmd.CommandText = "SELECT contact_id FROM purchase_order WHERE id = @pid";
                lookupCmd.Parameters.AddWithValue("@pid", dbRow.PoId);
                long contactId = Convert.ToInt64(lookupCmd.ExecuteScalar() ?? 0L);

                var targetPoNumber = OeNormalization.ResolvePoNumber(excelRow.PoNumber, dbRow.JobNumber);
                targetPoId = PoRepository.UpsertPurchaseOrder(conn, tx, targetPoNumber, excelRow.OeNumber, contactId);

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
                // Snapshot the PO's pre-change contact so UndoModify can restore it exactly.
                using (var prevCmd = conn.CreateCommand())
                {
                    prevCmd.Transaction = tx;
                    prevCmd.CommandText = "SELECT contact_id FROM purchase_order WHERE id = @pid";
                    prevCmd.Parameters.AddWithValue("@pid", dbRow.PoId);
                    var prevContactId = prevCmd.ExecuteScalar();
                    change.PreviousContactId = prevContactId == null || prevContactId is DBNull ? null : Convert.ToInt64(prevContactId);
                }

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
            change.WasReactivated = reactivateItem;
            change.PoWasReactivated = reactivatePo;
            Logger.Instance.Info($"OeSyncRepository.ApplyModify: updated order_item id={dbRow.OrderItemId}" +
                (reactivateItem ? " (reactivated)" : ""));
            return (true, null);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"OeSyncRepository.ApplyModify failed for order_item id={dbRow.OrderItemId}: {ex.Message}");
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Reverses ApplyModify by writing every changed field back to the value captured on
    /// change.DbRow (the pre-change snapshot), instead of re-deriving "old" values from the
    /// string-only FieldChanges list. The PO/part rows a genuine PO reassignment created are left
    /// in place (same rationale as UndoAdd) — only the order_item/job pointers are restored.
    /// </summary>
    public (bool Success, string? Error) UndoModify(OeSyncChange change)
    {
        var dbRow = change.DbRow;
        if (dbRow == null)
            return (false, "No pre-change snapshot to restore");

        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var tx = conn.BeginTransaction();

            var fieldLabels = change.FieldChanges.Select(f => f.FieldLabel).ToHashSet();
            var (qtyStored, qtyIsNumeric) = OeNormalization.ResolveQuantity(dbRow.Quantity);

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = change.WasReactivated
                    ? """
                        UPDATE order_item
                        SET quantity = @qty, actual_price = @price,
                            drawing_release_date = @drd, delivery_required_date = @del,
                            description = @desc,
                            is_active = 0, updated_at = datetime('now','localtime')
                        WHERE id = @id
                        """
                    : """
                        UPDATE order_item
                        SET quantity = @qty, actual_price = @price,
                            drawing_release_date = @drd, delivery_required_date = @del,
                            description = @desc,
                            updated_at = datetime('now','localtime')
                        WHERE id = @id
                        """;
                cmd.Parameters.AddWithValue("@qty", qtyIsNumeric ? (object)int.Parse(qtyStored, CultureInfo.InvariantCulture) : qtyStored);
                cmd.Parameters.AddWithValue("@price", dbRow.ActualPrice.HasValue ? (object)dbRow.ActualPrice.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@drd", string.IsNullOrWhiteSpace(dbRow.DrawingReleaseDate) ? DBNull.Value : (object)dbRow.DrawingReleaseDate);
                cmd.Parameters.AddWithValue("@del", string.IsNullOrWhiteSpace(dbRow.DeliveryRequiredDate) ? DBNull.Value : (object)dbRow.DeliveryRequiredDate);
                cmd.Parameters.AddWithValue("@desc", string.IsNullOrWhiteSpace(dbRow.Description) ? DBNull.Value : (object)dbRow.Description);
                cmd.Parameters.AddWithValue("@id", dbRow.OrderItemId);
                cmd.ExecuteNonQuery();
            }

            if (change.PoWasReactivated)
            {
                using var undoPoCmd = conn.CreateCommand();
                undoPoCmd.Transaction = tx;
                undoPoCmd.CommandText = "UPDATE purchase_order SET is_active = 0, updated_at = datetime('now','localtime') WHERE id = @id";
                undoPoCmd.Parameters.AddWithValue("@id", dbRow.PoId);
                undoPoCmd.ExecuteNonQuery();
            }

            if (fieldLabels.Contains("Part #") || fieldLabels.Contains("Rev"))
            {
                using var linkCmd = conn.CreateCommand();
                linkCmd.Transaction = tx;
                linkCmd.CommandText = "UPDATE order_item SET part_id = @pid, updated_at = datetime('now','localtime') WHERE id = @id";
                linkCmd.Parameters.AddWithValue("@pid", dbRow.PartId.HasValue ? (object)dbRow.PartId.Value : DBNull.Value);
                linkCmd.Parameters.AddWithValue("@id", dbRow.OrderItemId);
                linkCmd.ExecuteNonQuery();
            }

            if (fieldLabels.Contains("M (line #):"))
            {
                using var lineCmd = conn.CreateCommand();
                lineCmd.Transaction = tx;
                lineCmd.CommandText = "UPDATE order_item SET line_number = @ln, updated_at = datetime('now','localtime') WHERE id = @id";
                lineCmd.Parameters.AddWithValue("@ln", dbRow.LineNumber);
                lineCmd.Parameters.AddWithValue("@id", dbRow.OrderItemId);
                lineCmd.ExecuteNonQuery();
            }

            if (fieldLabels.Contains("Job #:"))
            {
                // Restores the original job_id from the pre-change snapshot — no upsert needed,
                // the original job row was never touched by ApplyModify.
                using var jobMoveCmd = conn.CreateCommand();
                jobMoveCmd.Transaction = tx;
                jobMoveCmd.CommandText = "UPDATE order_item SET job_id = @jid, updated_at = datetime('now','localtime') WHERE id = @id";
                jobMoveCmd.Parameters.AddWithValue("@jid", dbRow.JobId);
                jobMoveCmd.Parameters.AddWithValue("@id", dbRow.OrderItemId);
                jobMoveCmd.ExecuteNonQuery();
            }

            bool poChanged = fieldLabels.Contains("P.O. :");
            if (poChanged)
            {
                using var jobCmd = conn.CreateCommand();
                jobCmd.Transaction = tx;
                jobCmd.CommandText = "UPDATE job SET po_id = @pid, updated_at = datetime('now','localtime') WHERE id = @jid";
                jobCmd.Parameters.AddWithValue("@pid", dbRow.PoId);
                jobCmd.Parameters.AddWithValue("@jid", dbRow.JobId);
                jobCmd.ExecuteNonQuery();
            }
            else
            {
                if (fieldLabels.Contains("O.E.:"))
                {
                    using var oeCmd = conn.CreateCommand();
                    oeCmd.Transaction = tx;
                    oeCmd.CommandText = "UPDATE purchase_order SET oe_number = @oe, updated_at = datetime('now','localtime') WHERE id = @pid";
                    oeCmd.Parameters.AddWithValue("@oe", string.IsNullOrWhiteSpace(dbRow.OeNumber) ? DBNull.Value : (object)dbRow.OeNumber);
                    oeCmd.Parameters.AddWithValue("@pid", dbRow.PoId);
                    oeCmd.ExecuteNonQuery();
                }

                if ((fieldLabels.Contains("Customer:") || fieldLabels.Contains("Contact:")) && change.PreviousContactId.HasValue)
                {
                    using var poCmd = conn.CreateCommand();
                    poCmd.Transaction = tx;
                    poCmd.CommandText = "UPDATE purchase_order SET contact_id = @cid, updated_at = datetime('now','localtime') WHERE id = @pid";
                    poCmd.Parameters.AddWithValue("@cid", change.PreviousContactId.Value);
                    poCmd.Parameters.AddWithValue("@pid", dbRow.PoId);
                    poCmd.ExecuteNonQuery();
                }
            }

            tx.Commit();
            Logger.Instance.Info($"OeSyncRepository.UndoModify: restored order_item id={dbRow.OrderItemId}");
            return (true, null);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"OeSyncRepository.UndoModify failed for order_item id={dbRow.OrderItemId}: {ex.Message}");
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Applies a Deactivate change: marks the owning purchase_order and every one of its
    /// order_items inactive together ("shipped"), in a single transaction. Also clears any
    /// "handled anomaly" memory for those items — see data/db_changes.sql's oe_anomaly_handled —
    /// since a shipped item never resurfaces in future OE sync diffs, so remembering it was once
    /// reviewed no longer serves any purpose.
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
            using (var memoryCmd = conn.CreateCommand())
            {
                itemCmd.Transaction = tx;
                itemCmd.CommandText = "UPDATE order_item SET is_active = 0, updated_at = datetime('now','localtime') WHERE id = @id";
                var idParam = itemCmd.CreateParameter();
                idParam.ParameterName = "@id";
                itemCmd.Parameters.Add(idParam);

                memoryCmd.Transaction = tx;
                memoryCmd.CommandText = "DELETE FROM oe_anomaly_handled WHERE job_number = @job AND line_number = @line";
                var jobParam = memoryCmd.CreateParameter();
                jobParam.ParameterName = "@job";
                var lineParam = memoryCmd.CreateParameter();
                lineParam.ParameterName = "@line";
                memoryCmd.Parameters.Add(jobParam);
                memoryCmd.Parameters.Add(lineParam);

                foreach (var item in change.DeactivateItems)
                {
                    idParam.Value = item.OrderItemId;
                    itemCmd.ExecuteNonQuery();

                    jobParam.Value = OeNormalization.NormalizeJobNumber(item.JobNumber);
                    lineParam.Value = OeNormalization.NormalizeLineNumber(item.LineNumber);
                    memoryCmd.ExecuteNonQuery();
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

    /// <summary>Reverses ApplyDeactivate: reactivates the PO and every one of its DeactivateItems.</summary>
    public (bool Success, string? Error) UndoDeactivate(OeSyncChange change)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var tx = conn.BeginTransaction();

            using (var poCmd = conn.CreateCommand())
            {
                poCmd.Transaction = tx;
                poCmd.CommandText = "UPDATE purchase_order SET is_active = 1, closed_at = NULL, updated_at = datetime('now','localtime') WHERE id = @id";
                poCmd.Parameters.AddWithValue("@id", change.PoId);
                poCmd.ExecuteNonQuery();
            }

            using (var itemCmd = conn.CreateCommand())
            {
                itemCmd.Transaction = tx;
                itemCmd.CommandText = "UPDATE order_item SET is_active = 1, updated_at = datetime('now','localtime') WHERE id = @id";
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
            Logger.Instance.Info($"OeSyncRepository.UndoDeactivate: poId={change.PoId}, items={change.DeactivateItems.Count}");
            return (true, null);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"OeSyncRepository.UndoDeactivate failed for poId={change.PoId}: {ex.Message}");
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Resolves an Anomaly change backed by an existing order_item: the human has confirmed this
    /// one item should also be treated as inactive/shipped, without touching its sibling items or
    /// the owning PO. Anomalies with no DbRow (pure Excel parse warnings) cannot be applied here —
    /// the source data must be corrected first. Also clears any "handled anomaly" memory for this
    /// item, for the same reason as ApplyDeactivate.
    /// </summary>
    public (bool Success, string? Error) ApplyAnomaly(OeSyncChange change)
    {
        if (change.OrderItemId == null)
            return (false, "This anomaly has no actionable database record — the OE file or database must be corrected manually, then re-run sync");

        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var tx = conn.BeginTransaction();

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE order_item SET is_active = 0, updated_at = datetime('now','localtime') WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", change.OrderItemId.Value);
                cmd.ExecuteNonQuery();
            }

            if (change.DbRow != null)
            {
                using var memoryCmd = conn.CreateCommand();
                memoryCmd.Transaction = tx;
                memoryCmd.CommandText = "DELETE FROM oe_anomaly_handled WHERE job_number = @job AND line_number = @line";
                memoryCmd.Parameters.AddWithValue("@job", OeNormalization.NormalizeJobNumber(change.DbRow.JobNumber));
                memoryCmd.Parameters.AddWithValue("@line", OeNormalization.NormalizeLineNumber(change.DbRow.LineNumber));
                memoryCmd.ExecuteNonQuery();
            }

            tx.Commit();
            Logger.Instance.Info($"OeSyncRepository.ApplyAnomaly: marked order_item id={change.OrderItemId} inactive (manual resolution)");
            return (true, null);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"OeSyncRepository.ApplyAnomaly failed for order_item id={change.OrderItemId}: {ex.Message}");
            return (false, ex.Message);
        }
    }

    /// <summary>Reverses ApplyAnomaly: reactivates the order_item. Never touched purchase_order, so
    /// Undo doesn't either.</summary>
    public (bool Success, string? Error) UndoAnomaly(OeSyncChange change)
    {
        if (change.OrderItemId == null)
            return (false, "This anomaly has no actionable database record");

        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE order_item SET is_active = 1, updated_at = datetime('now','localtime') WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", change.OrderItemId.Value);
            cmd.ExecuteNonQuery();
            Logger.Instance.Info($"OeSyncRepository.UndoAnomaly: reactivated order_item id={change.OrderItemId}");
            return (true, null);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"OeSyncRepository.UndoAnomaly failed for order_item id={change.OrderItemId}: {ex.Message}");
            return (false, ex.Message);
        }
    }
}
