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
    /// </summary>
    public List<ScheduleViewModel> GetScheduleViewModels()
    {
        var rows = GetScheduleRows();
        if (rows.Count == 0) return [];

        var orderItemIds = rows.Select(r => r.OrderItemId).ToList();
        var partIds      = rows.Where(r => r.PartId > 0).Select(r => r.PartId).Distinct().ToList();

        var stepMap = GetAllStepTrackers(orderItemIds)
            .GroupBy(s => s.OrderItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var memoMap = GetLatestNotePerPart(partIds);

        return rows.Select(row => new ScheduleViewModel(
            row,
            stepMap.TryGetValue(row.OrderItemId, out var steps) ? steps : [],
            row.PartId > 0 ? memoMap.GetValueOrDefault(row.PartId) : null
        )).ToList();
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

    // ── Private helpers ────────────────────────────────────────────────────

    private List<ScheduleRow> GetScheduleRows()
    {
        var results = new List<ScheduleRow>();
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
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
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"ScheduleRepository.GetScheduleRows failed: {ex.Message}");
        }
        return results;
    }

    private List<ScheduleStepTracker> GetAllStepTrackers(List<int> orderItemIds)
    {
        if (orderItemIds.Count == 0) return [];

        var results = new List<ScheduleStepTracker>();
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd  = conn.CreateCommand();
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
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"ScheduleRepository.GetAllStepTrackers failed: {ex.Message}");
        }
        return results;
    }

    private Dictionary<int, string> GetLatestNotePerPart(List<int> partIds)
    {
        var result = new Dictionary<int, string>();
        if (partIds.Count == 0) return result;
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd  = conn.CreateCommand();
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
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"ScheduleRepository.GetLatestNotePerPart failed: {ex.Message}");
        }
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

/// <summary>One row in the Manufacturing Schedule Gantt view.</summary>
/// <param name="Row">Base order-item data</param>
/// <param name="Steps">Step tracker records that have a start_time</param>
/// <param name="MemoText">Latest part note content (nullable)</param>
public record ScheduleViewModel(ScheduleRow Row, List<ScheduleStepTracker> Steps, string? MemoText)
{
    /// <summary>True when the row's due date has passed today.</summary>
    public bool IsOverdue =>
        Row.DueDate != null &&
        DateTime.TryParse(Row.DueDate, out var due) &&
        due.Date < DateTime.Today;

    /// <summary>Human-readable current status derived from step tracker data.</summary>
    public string StatusText
    {
        get
        {
            if (Steps.Count == 0) return "Not Started";
            var inProgress = Steps.FirstOrDefault(s => s.EndTime == null);
            if (inProgress != null) return $"Step {inProgress.RowNumber}: {inProgress.ShopCode}";
            return "Complete";
        }
    }
}
