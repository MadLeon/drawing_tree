/// <summary>
/// ScheduleRepository.cs
/// Data access for the Manufacturing Schedule Gantt view: loads all active order items
/// with their step_tracker records and latest part note in minimal round-trips.
/// </summary>

using DrawingTree.Logging;

namespace DrawingTree.Data;

public class ScheduleRepository
{
    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all order items from active POs, each augmented with its step trackers
    /// and latest memo text, ready for rendering in the Gantt view.
    /// Uses a single connection for all queries to minimise network overhead on prod DB.
    /// </summary>
    public List<ScheduleViewModel> GetScheduleViewModels()
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();

            var rows = GetScheduleRows(conn);
            if (rows.Count == 0) return [];

            var orderItemIds  = rows.Select(r => r.OrderItemId).ToList();
            var partIds       = rows.Where(r => r.PartId > 0).Select(r => r.PartId).Distinct().ToList();

            var stepMap       = GetAllStepTrackers(conn, orderItemIds)
                .GroupBy(s => s.OrderItemId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var memoMap       = GetLatestNotePerPart(conn, partIds);
            var templateCount = GetTemplateStepCounts(conn, partIds);

            return rows.Select(row => new ScheduleViewModel(
                row,
                stepMap.TryGetValue(row.OrderItemId, out var steps) ? steps : [],
                row.PartId > 0 ? memoMap.GetValueOrDefault(row.PartId) : null,
                row.PartId > 0 ? templateCount.GetValueOrDefault(row.PartId, 0) : 0
            )).ToList();
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"ScheduleRepository.GetScheduleViewModels failed: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Returns all step_tracker records with start_time for the given order item.
    /// Used to refresh a single row after saving a step.
    /// </summary>
    /// <param name="orderItemId">order_item.id</param>
    public List<ScheduleStepTracker> GetStepTrackers(int orderItemId)
    {
        var results = new List<ScheduleStepTracker>();
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = """
                SELECT st.id, st.order_item_id, st.process_template_id, pt.row_number,
                       pt.shop_code, pt.description, st.start_time, st.end_time
                FROM step_tracker st
                JOIN process_template pt ON pt.id = st.process_template_id
                WHERE st.order_item_id = @oi AND st.start_time IS NOT NULL
                ORDER BY pt.row_number
                """;
            cmd.Parameters.AddWithValue("@oi", orderItemId);
            ReadStepTrackers(cmd, results);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"ScheduleRepository.GetStepTrackers failed: {ex.Message}");
        }
        return results;
    }

    /// <summary>
    /// Returns the process template steps for the given part.
    /// Used to populate the step-assignment dialog dropdown.
    /// </summary>
    /// <param name="partId">part.id</param>
    public List<ProcessTemplateStep> GetProcessTemplate(int partId)
    {
        var results = new List<ProcessTemplateStep>();
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, row_number, shop_code, description
                FROM process_template
                WHERE part_id = @partId
                ORDER BY row_number
                """;
            cmd.Parameters.AddWithValue("@partId", partId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new ProcessTemplateStep(
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)));
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"ScheduleRepository.GetProcessTemplate failed for partId={partId}: {ex.Message}");
        }
        return results;
    }

    /// <summary>
    /// Inserts or updates a step_tracker record keyed on (order_item_id, process_template_id).
    /// Applies application-level UPSERT; avoids adding a UNIQUE constraint that could conflict
    /// with barcode-scanner records already in the table.
    /// </summary>
    public void UpsertStepTracker(int orderItemId, int processTemplateId,
                                   string startTime, string? endTime)
    {
        try
        {
            using var conn     = DatabaseConnectionFactory.OpenDevConnection();
            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = """
                SELECT id FROM step_tracker
                WHERE order_item_id = @oi AND process_template_id = @pt
                LIMIT 1
                """;
            checkCmd.Parameters.AddWithValue("@oi", orderItemId);
            checkCmd.Parameters.AddWithValue("@pt", processTemplateId);
            var existingId = checkCmd.ExecuteScalar();

            using var cmd = conn.CreateCommand();
            if (existingId != null)
            {
                cmd.CommandText = """
                    UPDATE step_tracker
                    SET start_time = @start, end_time = @end,
                        updated_at = datetime('now', 'localtime')
                    WHERE id = @id
                    """;
                cmd.Parameters.AddWithValue("@id", existingId);
            }
            else
            {
                cmd.CommandText = """
                    INSERT INTO step_tracker (order_item_id, process_template_id, start_time, end_time)
                    VALUES (@oi, @pt, @start, @end)
                    """;
                cmd.Parameters.AddWithValue("@oi", orderItemId);
                cmd.Parameters.AddWithValue("@pt", processTemplateId);
            }
            cmd.Parameters.AddWithValue("@start", startTime);
            cmd.Parameters.AddWithValue("@end", endTime ?? (object)DBNull.Value);
            cmd.ExecuteNonQuery();
            Logger.Instance.Info($"ScheduleRepository: upserted step_tracker oi={orderItemId} pt={processTemplateId}");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"ScheduleRepository.UpsertStepTracker failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Marks a set of process template steps as complete for the given order item.
    /// Steps that already have an end_time are skipped. Steps without a tracker
    /// are inserted with start_time = end_time = endDate. All mutations run in
    /// a single transaction.
    /// </summary>
    /// <param name="orderItemId">order_item.id</param>
    /// <param name="processTemplateIds">process_template.id values to mark complete</param>
    /// <param name="endDate">ISO date string used as the completion date</param>
    public void MarkPreviousStepsComplete(int orderItemId, List<int> processTemplateIds, string endDate)
    {
        if (processTemplateIds.Count == 0) return;
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var tx   = conn.BeginTransaction();

            foreach (int ptId in processTemplateIds)
            {
                using var checkCmd = conn.CreateCommand();
                checkCmd.Transaction = tx;
                checkCmd.CommandText = """
                    SELECT id, end_time FROM step_tracker
                    WHERE order_item_id = @oi AND process_template_id = @pt
                    LIMIT 1
                    """;
                checkCmd.Parameters.AddWithValue("@oi", orderItemId);
                checkCmd.Parameters.AddWithValue("@pt", ptId);

                int?   existingId  = null;
                bool   alreadyDone = false;
                using (var r = checkCmd.ExecuteReader())
                {
                    if (r.Read())
                    {
                        existingId  = r.GetInt32(0);
                        alreadyDone = !r.IsDBNull(1);
                    }
                }
                if (alreadyDone) continue;

                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                if (existingId.HasValue)
                {
                    cmd.CommandText = """
                        UPDATE step_tracker
                        SET end_time = @end, updated_at = datetime('now', 'localtime')
                        WHERE id = @id
                        """;
                    cmd.Parameters.AddWithValue("@id", existingId.Value);
                }
                else
                {
                    cmd.CommandText = """
                        INSERT INTO step_tracker (order_item_id, process_template_id, start_time, end_time)
                        VALUES (@oi, @pt, @end, @end)
                        """;
                    cmd.Parameters.AddWithValue("@oi", orderItemId);
                    cmd.Parameters.AddWithValue("@pt", ptId);
                }
                cmd.Parameters.AddWithValue("@end", endDate);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            Logger.Instance.Info(
                $"ScheduleRepository: marked previous steps complete oi={orderItemId} count={processTemplateIds.Count}");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"ScheduleRepository.MarkPreviousStepsComplete failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Batch-fetches all BOM children for the given root part IDs via part_tree recursion.
    /// Returns cumulative path quantities (excluding order_item.quantity multiplier),
    /// latest part_note, and process_template step count per child part.
    /// </summary>
    public Dictionary<int, List<ScheduleChildItemData>> GetAllScheduleChildItems(List<int> partIds)
    {
        var result = new Dictionary<int, List<ScheduleChildItemData>>();
        if (partIds.Count == 0) return result;
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();

            var paramNames   = partIds.Select((_, i) => $"@r{i}").ToList();
            var rootInClause = string.Join(",", paramNames);

            var childRows = new List<(int RootId, int ChildId, string Drawing, string? Desc, int PathQty)>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"""
                    WITH RECURSIVE bom(root_id, child_id, cum_qty) AS (
                        SELECT parent_id, child_id, quantity
                        FROM part_tree
                        WHERE parent_id IN ({rootInClause})
                        UNION ALL
                        SELECT bom.root_id, pt.child_id, bom.cum_qty * pt.quantity
                        FROM part_tree pt
                        JOIN bom ON pt.parent_id = bom.child_id
                    )
                    SELECT bom.root_id,
                           latest.id,
                           latest.drawing_number,
                           latest.description,
                           CAST(SUM(bom.cum_qty) AS INTEGER) AS total_path_qty
                    FROM bom
                    JOIN part orig ON orig.id = bom.child_id
                    JOIN part latest ON latest.id = (
                        SELECT id FROM part
                        WHERE UPPER(drawing_number) = UPPER(orig.drawing_number)
                        ORDER BY revision DESC
                        LIMIT 1
                    )
                    GROUP BY bom.root_id, latest.id, latest.drawing_number, latest.description
                    ORDER BY bom.root_id, latest.drawing_number
                    """;
                for (int i = 0; i < partIds.Count; i++)
                    cmd.Parameters.AddWithValue(paramNames[i], partIds[i]);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    childRows.Add((rdr.GetInt32(0), rdr.GetInt32(1),
                        rdr.GetString(2),
                        rdr.IsDBNull(3) ? null : rdr.GetString(3),
                        rdr.GetInt32(4)));
            }

            if (childRows.Count == 0) return result;

            var childPartIds = childRows.Select(r => r.ChildId).Distinct().ToList();
            var memoMap      = GetLatestNotePerPart(conn, childPartIds);
            var stepMap      = GetTemplateStepCounts(conn, childPartIds);

            foreach (var (rootId, childId, drawing, desc, pathQty) in childRows)
            {
                var item = new ScheduleChildItemData(
                    RootPartId:        rootId,
                    ChildPartId:       childId,
                    DrawingNumber:     drawing,
                    Description:       desc,
                    CumulativePathQty: pathQty,
                    MemoText:          memoMap.GetValueOrDefault(childId),
                    TemplateSteps:     stepMap.GetValueOrDefault(childId, 0));
                if (!result.TryGetValue(rootId, out var list))
                    result[rootId] = list = [];
                list.Add(item);
            }

            Logger.Instance.Info(
                $"ScheduleRepository: child items fetched for {result.Count}/{partIds.Count} parts");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"ScheduleRepository.GetAllScheduleChildItems failed: {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// Fetches step_tracker records for child BOM parts under the given order items.
    /// Returns a lookup of (order_item_id, child_part_id) to step trackers, used to
    /// show Gantt bars and status fractions for expanded BOM child rows.
    /// </summary>
    public Dictionary<(int OiId, int ChildPartId), List<ScheduleStepTracker>> GetChildStepTrackers(
        List<int> orderItemIds, List<int> childPartIds)
    {
        var result = new Dictionary<(int, int), List<ScheduleStepTracker>>();
        if (orderItemIds.Count == 0 || childPartIds.Count == 0) return result;
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd  = conn.CreateCommand();

            var oiParams = string.Join(",", orderItemIds.Select((_, i) => $"@oi{i}"));
            var cpParams = string.Join(",", childPartIds.Select((_, i) => $"@cp{i}"));

            cmd.CommandText = $"""
                SELECT pt.part_id, st.order_item_id,
                       st.id, st.process_template_id, pt.row_number,
                       pt.shop_code, pt.description, st.start_time, st.end_time
                FROM step_tracker st
                JOIN process_template pt ON pt.id = st.process_template_id
                WHERE st.order_item_id IN ({oiParams})
                  AND pt.part_id IN ({cpParams})
                  AND st.start_time IS NOT NULL
                ORDER BY st.order_item_id, pt.row_number
                """;

            for (int i = 0; i < orderItemIds.Count; i++)
                cmd.Parameters.AddWithValue($"@oi{i}", orderItemIds[i]);
            for (int i = 0; i < childPartIds.Count; i++)
                cmd.Parameters.AddWithValue($"@cp{i}", childPartIds[i]);

            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                int childPartId = rdr.GetInt32(0);
                int oiId        = rdr.GetInt32(1);
                var step = new ScheduleStepTracker(
                    Id:                rdr.GetInt32(2),
                    OrderItemId:       oiId,
                    ProcessTemplateId: rdr.GetInt32(3),
                    RowNumber:         rdr.GetInt32(4),
                    ShopCode:          rdr.GetString(5),
                    Description:       rdr.IsDBNull(6) ? null : rdr.GetString(6),
                    StartTime:         rdr.GetString(7),
                    EndTime:           rdr.IsDBNull(8) ? null : rdr.GetString(8));

                var key = (oiId, childPartId);
                if (!result.TryGetValue(key, out var list))
                    result[key] = list = [];
                list.Add(step);
            }

            Logger.Instance.Info(
                $"ScheduleRepository: child step trackers for {result.Count} (oi,part) pairs");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"ScheduleRepository.GetChildStepTrackers failed: {ex.Message}");
        }
        return result;
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private List<ScheduleRow> GetScheduleRows(Microsoft.Data.Sqlite.SqliteConnection conn)
    {
        var results = new List<ScheduleRow>();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT oi.id, COALESCE(oi.part_id, 0), po.po_number,
                   j.job_number, oi.line_number,
                   c.customer_name, p.drawing_number, p.description,
                   oi.quantity, oi.delivery_required_date
            FROM order_item oi
            JOIN job j ON j.id = oi.job_id
            JOIN purchase_order po ON po.id = j.po_id
            LEFT JOIN customer_contact cc ON cc.id = po.contact_id
            LEFT JOIN customer c ON c.id = cc.customer_id
            LEFT JOIN part p ON p.id = oi.part_id
            WHERE po.is_active = 1
            ORDER BY po.po_number, j.job_number, oi.line_number
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new ScheduleRow(
                OrderItemId:  reader.GetInt32(0),
                PartId:       reader.GetInt32(1),
                PoNumber:     reader.GetString(2),
                JobNumber:    reader.GetString(3),
                LineNumber:   reader.GetInt32(4),
                CustomerName: reader.IsDBNull(5) ? null : reader.GetString(5),
                DrawingNumber:reader.IsDBNull(6) ? null : reader.GetString(6),
                Description:  reader.IsDBNull(7) ? null : reader.GetString(7),
                Quantity:     reader.GetInt32(8),
                DueDate:      reader.IsDBNull(9) ? null : reader.GetString(9)));
        }
        Logger.Instance.Info($"ScheduleRepository: loaded {results.Count} schedule rows");
        return results;
    }

    private List<ScheduleStepTracker> GetAllStepTrackers(
        Microsoft.Data.Sqlite.SqliteConnection conn, List<int> orderItemIds)
    {
        if (orderItemIds.Count == 0) return [];

        var results     = new List<ScheduleStepTracker>();
        using var cmd   = conn.CreateCommand();
        var placeholders = string.Join(",", orderItemIds.Select((_, i) => $"@id{i}"));
        cmd.CommandText = $"""
            SELECT st.id, st.order_item_id, st.process_template_id, pt.row_number,
                   pt.shop_code, pt.description, st.start_time, st.end_time
            FROM step_tracker st
            JOIN process_template pt ON pt.id = st.process_template_id
            WHERE st.order_item_id IN ({placeholders}) AND st.start_time IS NOT NULL
            ORDER BY st.order_item_id, pt.row_number
            """;
        for (int i = 0; i < orderItemIds.Count; i++)
            cmd.Parameters.AddWithValue($"@id{i}", orderItemIds[i]);
        ReadStepTrackers(cmd, results);
        return results;
    }

    private Dictionary<int, string> GetLatestNotePerPart(
        Microsoft.Data.Sqlite.SqliteConnection conn, List<int> partIds)
    {
        var result = new Dictionary<int, string>();
        if (partIds.Count == 0) return result;
        using var cmd   = conn.CreateCommand();
        var placeholders = string.Join(",", partIds.Select((_, i) => $"@pid{i}"));
        cmd.CommandText = $"""
            SELECT pn.part_id, pn.content
            FROM part_note pn
            JOIN (
                SELECT part_id, MAX(created_at) AS max_created
                FROM part_note
                WHERE part_id IN ({placeholders})
                GROUP BY part_id
            ) latest ON latest.part_id = pn.part_id
                     AND latest.max_created = pn.created_at
            """;
        for (int i = 0; i < partIds.Count; i++)
            cmd.Parameters.AddWithValue($"@pid{i}", partIds[i]);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            int pid = reader.GetInt32(0);
            if (!result.ContainsKey(pid))
                result[pid] = reader.GetString(1);
        }
        return result;
    }

    private Dictionary<int, int> GetTemplateStepCounts(
        Microsoft.Data.Sqlite.SqliteConnection conn, List<int> partIds)
    {
        var result = new Dictionary<int, int>();
        if (partIds.Count == 0) return result;
        using var cmd   = conn.CreateCommand();
        var placeholders = string.Join(",", partIds.Select((_, i) => $"@pid{i}"));
        cmd.CommandText = $"""
            SELECT part_id, COUNT(*) FROM process_template
            WHERE part_id IN ({placeholders})
            GROUP BY part_id
            """;
        for (int i = 0; i < partIds.Count; i++)
            cmd.Parameters.AddWithValue($"@pid{i}", partIds[i]);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result[reader.GetInt32(0)] = reader.GetInt32(1);
        return result;
    }

    private static void ReadStepTrackers(Microsoft.Data.Sqlite.SqliteCommand cmd,
                                          List<ScheduleStepTracker> results)
    {
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new ScheduleStepTracker(
                Id:                reader.GetInt32(0),
                OrderItemId:       reader.GetInt32(1),
                ProcessTemplateId: reader.GetInt32(2),
                RowNumber:         reader.GetInt32(3),
                ShopCode:          reader.GetString(4),
                Description:       reader.IsDBNull(5) ? null : reader.GetString(5),
                StartTime:         reader.GetString(6),
                EndTime:           reader.IsDBNull(7) ? null : reader.GetString(7)));
        }
    }
}

// ── Data models ────────────────────────────────────────────────────────────

/// <param name="OrderItemId">order_item.id</param>
/// <param name="PartId">part.id (0 when no linked part)</param>
/// <param name="PoNumber">Purchase order number</param>
/// <param name="JobNumber">Job number</param>
/// <param name="LineNumber">Order item line number</param>
/// <param name="CustomerName">Customer name (nullable)</param>
/// <param name="DrawingNumber">Part drawing number (nullable)</param>
/// <param name="Description">Part description (nullable)</param>
/// <param name="Quantity">Order quantity</param>
/// <param name="DueDate">delivery_required_date ISO string (nullable)</param>
public record ScheduleRow(
    int OrderItemId, int PartId,
    string PoNumber, string JobNumber, int LineNumber,
    string? CustomerName, string? DrawingNumber, string? Description,
    int Quantity, string? DueDate);

/// <param name="Id">step_tracker.id</param>
/// <param name="OrderItemId">order_item.id</param>
/// <param name="ProcessTemplateId">process_template.id</param>
/// <param name="RowNumber">process_template.row_number</param>
/// <param name="ShopCode">Shop code for color mapping</param>
/// <param name="Description">Step description (nullable)</param>
/// <param name="StartTime">ISO date string (always non-null in this result set)</param>
/// <param name="EndTime">ISO date string; null means step is in progress</param>
public record ScheduleStepTracker(
    int Id, int OrderItemId, int ProcessTemplateId, int RowNumber,
    string ShopCode, string? Description,
    string? StartTime, string? EndTime);

/// <param name="Id">process_template.id</param>
/// <param name="RowNumber">Sequence number</param>
/// <param name="ShopCode">Shop code</param>
/// <param name="Description">Step description (nullable)</param>
public record ProcessTemplateStep(int Id, int RowNumber, string ShopCode, string? Description);

/// <summary>Child BOM part data for the MP Schedule expand view.</summary>
/// <param name="RootPartId">part.id of the top-level (order_item) part</param>
/// <param name="ChildPartId">part.id of this child part</param>
/// <param name="DrawingNumber">Child drawing number</param>
/// <param name="Description">Child part description (nullable)</param>
/// <param name="CumulativePathQty">Product of part_tree.quantity values along the path from root to child</param>
/// <param name="MemoText">Latest part_note content (nullable)</param>
/// <param name="TemplateSteps">Total process_template steps for this child part</param>
public record ScheduleChildItemData(
    int    RootPartId,
    int    ChildPartId,
    string DrawingNumber,
    string? Description,
    int    CumulativePathQty,
    string? MemoText,
    int    TemplateSteps);

/// <summary>One row in the Manufacturing Schedule Gantt view.</summary>
/// <param name="Row">Base order-item data</param>
/// <param name="Steps">Step tracker records that have a start_time</param>
/// <param name="MemoText">Latest part note content (nullable)</param>
/// <param name="TotalTemplateSteps">Total steps in the process template for this part</param>
public record ScheduleViewModel(ScheduleRow Row, List<ScheduleStepTracker> Steps, string? MemoText, int TotalTemplateSteps)
{
    /// <summary>True when the row's due date has passed today.</summary>
    public bool IsOverdue =>
        Row.DueDate != null &&
        DateTime.TryParse(Row.DueDate, out var due) &&
        due.Date < DateTime.Today;

    /// <summary>Fraction of completed steps over total template steps, e.g. "3/12".</summary>
    public string StatusText
    {
        get
        {
            int completed = Steps.Count(s => s.EndTime != null);
            return $"{completed}/{TotalTemplateSteps}";
        }
    }
}
