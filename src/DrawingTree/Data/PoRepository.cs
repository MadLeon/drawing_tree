/// <summary>
/// PoRepository.cs
/// Database queries for PO → job → order_item → part cascade and part_tree recursive traversal.
/// </summary>
/// <remarks>
/// Usage:
/// - GetGroupsForPo(): replaces PoTreeService mock; returns root assembly groups for a PO
/// - GetPartTree():    recursively loads saved parent-child relationships for a root drawing
/// </remarks>

using DrawingTree.Logging;
using DrawingTree.Models;
using Microsoft.Data.Sqlite;

namespace DrawingTree.Data;

public class PoRepository
{
    /// <summary>
    /// Queries all root assembly groups for the given PO number.
    /// Chain: purchase_order → job → order_item → part.
    /// </summary>
    /// <param name="poNumber">PO number (e.g. "RT79-87630-PN-R005")</param>
    /// <returns>List of root groups; empty if PO not found</returns>
    public List<PoTreeGroup> GetGroupsForPo(string poNumber)
    {
        var rows = new List<(string Job, string Line, string Drawing, int PartId)>();

        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT j.job_number,
                       CAST(oi.line_number AS TEXT),
                       p.drawing_number,
                       p.id AS part_id
                FROM purchase_order po
                JOIN job        j  ON j.po_id   = po.id
                JOIN order_item oi ON oi.job_id = j.id
                JOIN part       p  ON p.id      = oi.part_id
                WHERE po.po_number = @po
                ORDER BY j.job_number, oi.line_number
                """;
            cmd.Parameters.AddWithValue("@po", poNumber);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3)
                ));
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PoRepository.GetGroupsForPo failed for '{poNumber}': {ex.Message}");
            return new List<PoTreeGroup>();
        }

        return rows
            .GroupBy(r => (r.Drawing, r.PartId))
            .Select(g => new PoTreeGroup
            {
                DrawingNumber = g.Key.Drawing,
                PartId        = g.Key.PartId,
                JobNumbers    = g.Select(r => r.Job).Distinct().ToList(),
                LineNumbers   = g.Select(r => r.Line).ToList()
            })
            .ToList();
    }

    /// <summary>
    /// Walks up the part_tree from the given part ID to find the top-most root.
    /// Returns the root part.id, or partId itself if it has no parent.
    /// </summary>
    /// <param name="partId">Starting part.id</param>
    public int GetRootPartId(int partId)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                WITH RECURSIVE ancestors AS (
                    SELECT @partId AS part_id

                    UNION ALL

                    SELECT pt.parent_id
                    FROM ancestors a
                    JOIN part_tree pt ON pt.child_id = a.part_id
                )
                SELECT part_id FROM ancestors
                WHERE part_id NOT IN (SELECT child_id FROM part_tree)
                LIMIT 1
                """;
            cmd.Parameters.AddWithValue("@partId", partId);

            var result = cmd.ExecuteScalar();
            return result is long id ? (int)id : partId;
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PoRepository.GetRootPartId failed for partId={partId}: {ex.Message}");
            return partId;
        }
    }

    /// <summary>
    /// Recursively loads the saved part_tree rooted at the given part ID.
    /// Returns all descendant DrawingNodes with their part/file info populated.
    /// Returns an empty list if no children exist in the database.
    /// </summary>
    /// <param name="rootPartId">part.id of the root drawing</param>
    public List<DrawingNode> GetPartTree(int rootPartId)
    {
        var rows = new List<(int PartTreeId, int PartId, string Drawing, string Revision,
            string? Description, bool? IsAssembly, int? ParentPartId, int Quantity, string? FilePath)>();

        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                WITH RECURSIVE tree AS (
                    SELECT p.id             AS part_id,
                           p.drawing_number,
                           p.revision,
                           p.description,
                           p.is_assembly,
                           CAST(NULL AS INTEGER) AS parent_part_id,
                           CAST(NULL AS INTEGER) AS part_tree_id,
                           1                    AS quantity
                    FROM part p
                    WHERE p.id = @rootId

                    UNION ALL

                    SELECT child.id,
                           child.drawing_number,
                           child.revision,
                           child.description,
                           child.is_assembly,
                           parent.part_id,
                           pt.id,
                           pt.quantity
                    FROM tree parent
                    JOIN part_tree pt    ON pt.parent_id = parent.part_id
                    JOIN part      child ON child.id     = pt.child_id
                )
                SELECT t.part_tree_id,
                       t.part_id,
                       latest.drawing_number,
                       latest.revision,
                       latest.description,
                       latest.is_assembly,
                       t.parent_part_id,
                       t.quantity,
                       df.file_path
                FROM tree t
                JOIN part latest ON latest.id = (
                    SELECT id FROM part
                    WHERE UPPER(drawing_number) = UPPER(t.drawing_number)
                    ORDER BY revision DESC
                    LIMIT 1
                )
                LEFT JOIN drawing_file df ON df.part_id = latest.id AND df.is_active = 1
                WHERE t.parent_part_id IS NOT NULL
                ORDER BY t.drawing_number
                """;
            cmd.Parameters.AddWithValue("@rootId", rootPartId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((
                    reader.IsDBNull(0) ? 0       : reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null    : reader.GetString(4),
                    reader.IsDBNull(5) ? null    : reader.GetInt32(5) != 0,
                    reader.IsDBNull(6) ? null    : (int?)reader.GetInt32(6),
                    reader.GetInt32(7),
                    reader.IsDBNull(8) ? null    : reader.GetString(8)
                ));
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PoRepository.GetPartTree failed for partId={rootPartId}: {ex.Message}");
            return new List<DrawingNode>();
        }

        // Build node map keyed by part_id
        var nodeMap = new Dictionary<int, DrawingNode>();
        foreach (var r in rows)
        {
            if (!nodeMap.ContainsKey(r.PartId))
            {
                var info = new DrawingInfo
                {
                    PartId      = r.PartId,
                    DrawingNumber = r.Drawing,
                    Revision    = r.Revision,
                    Description = r.Description ?? string.Empty,
                    IsAssembly  = r.IsAssembly ?? false,
                    PdfPath     = r.FilePath ?? string.Empty,
                    QuantityInAssembly = r.Quantity.ToString()
                };
                nodeMap[r.PartId] = new DrawingNode(info) { PartTreeId = r.PartTreeId };
            }
        }

        // Wire parent → child
        var topLevel = new List<DrawingNode>();
        foreach (var r in rows)
        {
            if (!nodeMap.TryGetValue(r.PartId, out var node)) continue;
            if (r.ParentPartId == null || r.ParentPartId == rootPartId)
            {
                topLevel.Add(node);
            }
            else if (nodeMap.TryGetValue(r.ParentPartId.Value, out var parent))
            {
                if (!parent.Children.Contains(node))
                    parent.Children.Add(node);
            }
        }

        return topLevel;
    }

    /// <summary>
    /// Loads every order_item line across all purchase orders, joined up through
    /// job/purchase_order/customer and down to part, for the "All POs" overview screen.
    /// </summary>
    /// <param name="activeOnly">True to return is_active=1 POs; false for is_active=0 (history).</param>
    public List<PoListRow> GetAllPoLines(bool activeOnly = true)
    {
        var results = new List<PoListRow>();
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT po.id, po.po_number, po.oe_number,
                       j.job_number, oi.line_number,
                       cust.customer_name, cc.contact_name,
                       oi.quantity, p.drawing_number, p.revision, p.description,
                       oi.drawing_release_date, oi.delivery_required_date,
                       oi.part_id, oi.id AS order_item_id,
                       CASE WHEN oi.part_id IS NOT NULL
                                 AND EXISTS (SELECT 1 FROM part_tree WHERE parent_id = oi.part_id)
                            THEN 1 ELSE 0 END AS has_children
                FROM purchase_order po
                JOIN job j ON j.po_id = po.id
                JOIN order_item oi ON oi.job_id = j.id
                LEFT JOIN part p ON p.id = oi.part_id
                LEFT JOIN customer_contact cc ON cc.id = po.contact_id
                LEFT JOIN customer cust ON cust.id = cc.customer_id
                WHERE po.is_active = {(activeOnly ? 1 : 0)}
                ORDER BY po.po_number, j.job_number, oi.line_number
                """;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new PoListRow(
                    PoId:          reader.GetInt32(0),
                    PoNumber:      reader.GetString(1),
                    OeNumber:      reader.IsDBNull(2)  ? null : reader.GetString(2),
                    JobNumber:     reader.GetString(3),
                    LineNumber:    reader.GetInt32(4),
                    CustomerName:  reader.IsDBNull(5)  ? null : reader.GetString(5),
                    ContactName:   reader.IsDBNull(6)  ? null : reader.GetString(6),
                    Quantity:      reader.GetInt32(7),
                    DrawingNumber: reader.IsDBNull(8)  ? null : reader.GetString(8),
                    Revision:      reader.IsDBNull(9)  ? null : reader.GetString(9),
                    Description:   reader.IsDBNull(10) ? null : reader.GetString(10),
                    ReleaseDate:   reader.IsDBNull(11) ? null : reader.GetString(11),
                    DueDate:       reader.IsDBNull(12) ? null : reader.GetString(12),
                    PartId:        reader.IsDBNull(13) ? null : reader.GetInt32(13),
                    OrderItemId:   reader.GetInt32(14),
                    HasChildren:   reader.GetInt32(15) != 0
                ));
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PoRepository.GetAllPoLines failed: {ex.Message}");
        }
        return results;
    }

    /// <summary>
    /// Loads the PO/job/part context needed to populate a Manufacturing Process Excel file.
    /// </summary>
    /// <param name="orderItemId">order_item.id</param>
    /// <returns>MpContext, or null if the order item is not found.</returns>
    public MpContext? GetMpContext(int orderItemId)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT po.po_number, po.oe_number,
                       j.job_number, oi.line_number,
                       p.drawing_number, p.revision, p.description,
                       oi.drawing_release_date, oi.delivery_required_date,
                       oi.quantity, cust.customer_name,
                       COALESCE(p.id, 0) AS part_id,
                       oi.id AS order_item_id
                FROM order_item oi
                JOIN job j              ON j.id   = oi.job_id
                JOIN purchase_order po  ON po.id  = j.po_id
                LEFT JOIN customer_contact cc ON cc.id   = po.contact_id
                LEFT JOIN customer cust       ON cust.id = cc.customer_id
                LEFT JOIN part p              ON p.id    = oi.part_id
                WHERE oi.id = @id
                LIMIT 1
                """;
            cmd.Parameters.AddWithValue("@id", orderItemId);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            return new MpContext(
                PoNumber:      reader.GetString(0),
                OeNumber:      reader.IsDBNull(1)  ? null : reader.GetString(1),
                JobNumber:     reader.GetString(2),
                LineNumber:    reader.GetInt32(3),
                DrawingNumber: reader.IsDBNull(4)  ? null : reader.GetString(4),
                Revision:      reader.IsDBNull(5)  ? null : reader.GetString(5),
                Description:   reader.IsDBNull(6)  ? null : reader.GetString(6),
                ReleaseDate:   reader.IsDBNull(7)  ? null : reader.GetString(7),
                DueDate:       reader.IsDBNull(8)  ? null : reader.GetString(8),
                Quantity:      reader.GetInt32(9),
                CustomerName:  reader.IsDBNull(10) ? null : reader.GetString(10),
                PartId:        reader.GetInt32(11),
                OrderItemId:   reader.GetInt32(12)
            );
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PoRepository.GetMpContext failed for orderItemId={orderItemId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Sets purchase_order.is_active = 0 ("shipped / archived") for the given PO.
    /// </summary>
    public bool MarkAsShipped(int poId)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE purchase_order SET is_active = 0, updated_at = datetime('now','localtime') WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", poId);
            cmd.ExecuteNonQuery();
            Logger.Instance.Info($"PoRepository.MarkAsShipped: poId={poId} set is_active=0");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PoRepository.MarkAsShipped failed for poId={poId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Returns all descendant parts of the given root part (via part_tree), sorted by drawing_number.
    /// </summary>
    public List<ChildDrawingRow> GetChildDrawings(int partId)
    {
        var results = new List<ChildDrawingRow>();
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                WITH RECURSIVE descendants(child_id) AS (
                    SELECT child_id FROM part_tree WHERE parent_id = @partId
                    UNION ALL
                    SELECT pt.child_id FROM part_tree pt JOIN descendants d ON pt.parent_id = d.child_id
                )
                SELECT DISTINCT p2.id, p2.drawing_number, p2.revision, p2.description
                FROM descendants d
                JOIN part p1 ON p1.id = d.child_id
                JOIN part p2 ON UPPER(p2.drawing_number) = UPPER(p1.drawing_number)
                WHERE p2.id = (
                    SELECT id FROM part
                    WHERE UPPER(drawing_number) = UPPER(p1.drawing_number)
                    ORDER BY revision DESC
                    LIMIT 1
                )
                ORDER BY p2.drawing_number
                """;
            cmd.Parameters.AddWithValue("@partId", partId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new ChildDrawingRow(
                    PartId:        reader.GetInt32(0),
                    DrawingNumber: reader.GetString(1),
                    Revision:      reader.IsDBNull(2) ? null : reader.GetString(2),
                    Description:   reader.IsDBNull(3) ? null : reader.GetString(3)
                ));
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PoRepository.GetChildDrawings failed for partId={partId}: {ex.Message}");
        }
        return results;
    }

    /// <summary>
    /// Batch-fetches all descendant drawings for multiple root part IDs in a single query.
    /// Mirrors GetChildDrawings logic but carries root_id through the recursion.
    /// </summary>
    /// <param name="partIds">Root part IDs to fetch children for.</param>
    /// <returns>Dictionary keyed by root partId; missing keys mean no children found.</returns>
    public Dictionary<int, List<ChildDrawingRow>> GetAllChildDrawings(IEnumerable<int> partIds)
    {
        var result = new Dictionary<int, List<ChildDrawingRow>>();
        var ids = partIds.ToList();
        if (ids.Count == 0) return result;

        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();

            var paramNames = ids.Select((_, i) => $"@p{i}").ToList();
            cmd.CommandText = $"""
                WITH RECURSIVE descendants(root_id, child_id) AS (
                    SELECT parent_id, child_id FROM part_tree WHERE parent_id IN ({string.Join(",", paramNames)})
                    UNION ALL
                    SELECT d.root_id, pt.child_id
                    FROM part_tree pt JOIN descendants d ON pt.parent_id = d.child_id
                )
                SELECT DISTINCT d.root_id, p2.id, p2.drawing_number, p2.revision, p2.description
                FROM descendants d
                JOIN part p1 ON p1.id = d.child_id
                JOIN part p2 ON UPPER(p2.drawing_number) = UPPER(p1.drawing_number)
                WHERE p2.id = (
                    SELECT id FROM part
                    WHERE UPPER(drawing_number) = UPPER(p1.drawing_number)
                    ORDER BY revision DESC LIMIT 1
                )
                ORDER BY d.root_id, p2.drawing_number
                """;
            for (int i = 0; i < ids.Count; i++)
                cmd.Parameters.AddWithValue(paramNames[i], ids[i]);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                int rootId = reader.GetInt32(0);
                var row = new ChildDrawingRow(
                    PartId:        reader.GetInt32(1),
                    DrawingNumber: reader.GetString(2),
                    Revision:      reader.IsDBNull(3) ? null : reader.GetString(3),
                    Description:   reader.IsDBNull(4) ? null : reader.GetString(4)
                );
                if (!result.TryGetValue(rootId, out var list))
                    result[rootId] = list = new List<ChildDrawingRow>();
                list.Add(row);
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PoRepository.GetAllChildDrawings failed: {ex.Message}");
        }
        return result;
    }

    /// <summary>Returns the active PDF file path for the given part, or null if not found.</summary>
    public string? GetPdfPath(int partId)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT file_path FROM drawing_file WHERE part_id = @id AND is_active = 1 ORDER BY id DESC LIMIT 1";
            cmd.Parameters.AddWithValue("@id", partId);
            return cmd.ExecuteScalar() as string;
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PoRepository.GetPdfPath failed for partId={partId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Returns the set of order_item IDs (from the input list) that have at least one MP attachment.
    /// </summary>
    public HashSet<int> GetOrderItemIdsWithMp(IEnumerable<int> orderItemIds)
    {
        var result = new HashSet<int>();
        var ids = orderItemIds.ToList();
        if (ids.Count == 0) return result;
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            var placeholders = string.Join(",", ids.Select((_, i) => $"@id{i}"));
            cmd.CommandText = $"SELECT DISTINCT order_item_id FROM part_attachment WHERE file_type = 'MP' AND order_item_id IN ({placeholders})";
            for (int i = 0; i < ids.Count; i++)
                cmd.Parameters.AddWithValue($"@id{i}", ids[i]);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(reader.GetInt32(0));
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PoRepository.GetOrderItemIdsWithMp failed: {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// Returns the set of part IDs (from the input list) that have at least one MP attachment.
    /// </summary>
    public HashSet<int> GetPartIdsWithMp(IEnumerable<int> partIds)
    {
        var result = new HashSet<int>();
        var ids = partIds.ToList();
        if (ids.Count == 0) return result;
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            var placeholders = string.Join(",", ids.Select((_, i) => $"@id{i}"));
            cmd.CommandText = $"SELECT DISTINCT part_id FROM part_attachment WHERE file_type = 'MP' AND part_id IN ({placeholders})";
            for (int i = 0; i < ids.Count; i++)
                cmd.Parameters.AddWithValue($"@id{i}", ids[i]);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                if (!reader.IsDBNull(0)) result.Add(reader.GetInt32(0));
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PoRepository.GetPartIdsWithMp failed: {ex.Message}");
        }
        return result;
    }

    /// <summary>Returns true if the given part has any direct children in part_tree.</summary>
    public bool HasChildDrawings(int partId)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM part_tree WHERE parent_id = @partId";
            cmd.Parameters.AddWithValue("@partId", partId);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PoRepository.HasChildDrawings failed for partId={partId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Returns next OE number (max existing integer value + 1).</summary>
    public int GetNextOeNumber()
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT MAX(CAST(oe_number AS INTEGER)) FROM purchase_order WHERE oe_number GLOB '[0-9]*'";
            var val = cmd.ExecuteScalar();
            return (val == DBNull.Value || val == null) ? 1 : Convert.ToInt32(val) + 1;
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PoRepository.GetNextOeNumber failed: {ex.Message}");
            return 1;
        }
    }

    /// <summary>Returns next job number (max existing integer value + 1).</summary>
    public int GetNextJobNumber()
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT MAX(CAST(job_number AS INTEGER)) FROM job WHERE job_number GLOB '[0-9]*'";
            var val = cmd.ExecuteScalar();
            return (val == DBNull.Value || val == null) ? 1 : Convert.ToInt32(val) + 1;
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PoRepository.GetNextJobNumber failed: {ex.Message}");
            return 1;
        }
    }

    /// <summary>Returns current max job number (for "Use Previous" button).</summary>
    public int GetMaxJobNumber()
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT MAX(CAST(job_number AS INTEGER)) FROM job WHERE job_number GLOB '[0-9]*'";
            var val = cmd.ExecuteScalar();
            return (val == DBNull.Value || val == null) ? 1 : Convert.ToInt32(val);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PoRepository.GetMaxJobNumber failed: {ex.Message}");
            return 1;
        }
    }

    /// <summary>Returns all customers ordered by name.</summary>
    public List<CustomerRow> GetAllCustomers()
    {
        var results = new List<CustomerRow>();
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, customer_name FROM customer ORDER BY customer_name";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add(new CustomerRow(reader.GetInt32(0), reader.GetString(1)));
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PoRepository.GetAllCustomers failed: {ex.Message}");
        }
        return results;
    }

    /// <summary>Returns contacts for the given customer.</summary>
    public List<ContactRow> GetContactsByCustomer(int customerId)
    {
        var results = new List<ContactRow>();
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, contact_name FROM customer_contact WHERE customer_id = @cid ORDER BY contact_name";
            cmd.Parameters.AddWithValue("@cid", customerId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add(new ContactRow(reader.GetInt32(0), reader.GetString(1)));
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PoRepository.GetContactsByCustomer failed for customerId={customerId}: {ex.Message}");
        }
        return results;
    }

    /// <summary>
    /// Creates a new job cascade in a single transaction:
    /// customer → customer_contact → purchase_order → job → part → order_item.
    /// Returns the new order_item.id, or -1 on failure.
    /// </summary>
    public int CreateOrderItemCascade(NewJobInput input)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var tx = conn.BeginTransaction();

            long customerId = UpsertCustomer(conn, tx, input.CustomerName);
            long contactId  = UpsertContact(conn, tx, customerId, input.ContactName);
            string poNumber = string.IsNullOrWhiteSpace(input.PoNumber)
                ? $"NPO-{input.JobNumber}"
                : input.PoNumber;
            long poId = UpsertPurchaseOrder(conn, tx, poNumber, input.OeNumber, contactId);
            long jobId = UpsertJob(conn, tx, input.JobNumber, poId);

            long partId = -1;
            if (!string.IsNullOrWhiteSpace(input.DrawingNumber))
                partId = UpsertPart(conn, tx, input.DrawingNumber, input.Revision,
                                    input.Description, input.UnitPrice);

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO order_item (job_id, part_id, line_number, quantity, delivery_required_date, updated_at)
                VALUES (@jobId, @partId, @ln, @qty, @del, datetime('now','localtime'))
                """;
            cmd.Parameters.AddWithValue("@jobId",  jobId);
            cmd.Parameters.AddWithValue("@partId", partId > 0 ? partId : DBNull.Value);
            cmd.Parameters.AddWithValue("@ln",     input.LineNumber);
            cmd.Parameters.AddWithValue("@qty",    input.Quantity);
            cmd.Parameters.AddWithValue("@del",    string.IsNullOrWhiteSpace(input.DeliveryDate)
                                                       ? DBNull.Value : (object)input.DeliveryDate);
            cmd.ExecuteNonQuery();

            cmd.CommandText = "SELECT last_insert_rowid()";
            long newId = Convert.ToInt64(cmd.ExecuteScalar()!);
            tx.Commit();
            Logger.Instance.Info($"PoRepository.CreateOrderItemCascade: created order_item id={newId}");
            return (int)newId;
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PoRepository.CreateOrderItemCascade failed: {ex.Message}");
            return -1;
        }
    }

    /// <summary>
    /// Updates an existing order_item and its associated part fields.
    /// Returns true on success.
    /// </summary>
    public bool UpdateOrderItemCascade(EditItemInput input)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var tx = conn.BeginTransaction();

            if (!string.IsNullOrWhiteSpace(input.DrawingNumber))
            {
                long partId = UpsertPart(conn, tx, input.DrawingNumber, input.Revision,
                                         input.Description, input.UnitPrice);

                using var linkCmd = conn.CreateCommand();
                linkCmd.Transaction = tx;
                linkCmd.CommandText = "UPDATE order_item SET part_id = @pid, line_number = @ln, quantity = @qty, delivery_required_date = @del, updated_at = datetime('now','localtime') WHERE id = @id";
                linkCmd.Parameters.AddWithValue("@pid", partId);
                linkCmd.Parameters.AddWithValue("@ln",  input.LineNumber);
                linkCmd.Parameters.AddWithValue("@qty", input.Quantity);
                linkCmd.Parameters.AddWithValue("@del", string.IsNullOrWhiteSpace(input.DeliveryDate)
                                                            ? DBNull.Value : (object)input.DeliveryDate);
                linkCmd.Parameters.AddWithValue("@id",  input.OrderItemId);
                linkCmd.ExecuteNonQuery();
            }
            else
            {
                using var oiCmd = conn.CreateCommand();
                oiCmd.Transaction = tx;
                oiCmd.CommandText = "UPDATE order_item SET line_number = @ln, quantity = @qty, delivery_required_date = @del, updated_at = datetime('now','localtime') WHERE id = @id";
                oiCmd.Parameters.AddWithValue("@ln",  input.LineNumber);
                oiCmd.Parameters.AddWithValue("@qty", input.Quantity);
                oiCmd.Parameters.AddWithValue("@del", string.IsNullOrWhiteSpace(input.DeliveryDate)
                                                          ? DBNull.Value : (object)input.DeliveryDate);
                oiCmd.Parameters.AddWithValue("@id",  input.OrderItemId);
                oiCmd.ExecuteNonQuery();
            }

            if (!string.IsNullOrWhiteSpace(input.ContactName))
            {
                long customerId = UpsertCustomer(conn, tx, input.CustomerName);
                long contactId  = UpsertContact(conn, tx, customerId, input.ContactName);
                using var poCmd = conn.CreateCommand();
                poCmd.Transaction = tx;
                poCmd.CommandText = "UPDATE purchase_order SET contact_id = @cid, updated_at = datetime('now','localtime') WHERE id = @pid";
                poCmd.Parameters.AddWithValue("@cid", contactId);
                poCmd.Parameters.AddWithValue("@pid", input.PoId);
                poCmd.ExecuteNonQuery();
            }

            tx.Commit();
            Logger.Instance.Info($"PoRepository.UpdateOrderItemCascade: updated order_item id={input.OrderItemId}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PoRepository.UpdateOrderItemCascade failed: {ex.Message}");
            return false;
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static long UpsertCustomer(Microsoft.Data.Sqlite.SqliteConnection conn,
                                       Microsoft.Data.Sqlite.SqliteTransaction tx,
                                       string name)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR IGNORE INTO customer (customer_name) VALUES (@n)";
        cmd.Parameters.AddWithValue("@n", name);
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT id FROM customer WHERE customer_name = @n";
        return Convert.ToInt64(cmd.ExecuteScalar()!);
    }

    private static long UpsertContact(Microsoft.Data.Sqlite.SqliteConnection conn,
                                      Microsoft.Data.Sqlite.SqliteTransaction tx,
                                      long customerId, string contactName)
    {
        if (string.IsNullOrWhiteSpace(contactName))
        {
            using var defCmd = conn.CreateCommand();
            defCmd.Transaction = tx;
            defCmd.CommandText = "SELECT id FROM customer_contact WHERE customer_id = @cid LIMIT 1";
            defCmd.Parameters.AddWithValue("@cid", customerId);
            var existing = defCmd.ExecuteScalar();
            if (existing != null) return Convert.ToInt64(existing);
        }

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR IGNORE INTO customer_contact (customer_id, contact_name) VALUES (@cid, @n)";
        cmd.Parameters.AddWithValue("@cid", customerId);
        cmd.Parameters.AddWithValue("@n",   contactName);
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT id FROM customer_contact WHERE customer_id = @cid AND contact_name = @n";
        return Convert.ToInt64(cmd.ExecuteScalar()!);
    }

    private static long UpsertPurchaseOrder(Microsoft.Data.Sqlite.SqliteConnection conn,
                                            Microsoft.Data.Sqlite.SqliteTransaction tx,
                                            string poNumber, string oeNumber, long contactId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO purchase_order (po_number, oe_number, contact_id, is_active, updated_at)
            VALUES (@po, @oe, @cid, 1, datetime('now','localtime'))
            ON CONFLICT(po_number) DO UPDATE SET
                oe_number  = excluded.oe_number,
                contact_id = excluded.contact_id,
                updated_at = datetime('now','localtime')
            """;
        cmd.Parameters.AddWithValue("@po",  poNumber);
        cmd.Parameters.AddWithValue("@oe",  string.IsNullOrWhiteSpace(oeNumber) ? DBNull.Value : (object)oeNumber);
        cmd.Parameters.AddWithValue("@cid", contactId);
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT id FROM purchase_order WHERE po_number = @po";
        return Convert.ToInt64(cmd.ExecuteScalar()!);
    }

    private static long UpsertJob(Microsoft.Data.Sqlite.SqliteConnection conn,
                                  Microsoft.Data.Sqlite.SqliteTransaction tx,
                                  string jobNumber, long poId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO job (job_number, po_id, updated_at)
            VALUES (@jn, @pid, datetime('now','localtime'))
            ON CONFLICT(job_number) DO UPDATE SET updated_at = datetime('now','localtime')
            """;
        cmd.Parameters.AddWithValue("@jn",  jobNumber);
        cmd.Parameters.AddWithValue("@pid", poId);
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT id FROM job WHERE job_number = @jn";
        return Convert.ToInt64(cmd.ExecuteScalar()!);
    }

    private static long UpsertPart(Microsoft.Data.Sqlite.SqliteConnection conn,
                                   Microsoft.Data.Sqlite.SqliteTransaction tx,
                                   string drawingNumber, string revision,
                                   string description, string unitPrice)
    {
        decimal price = decimal.TryParse(unitPrice, out var p) ? p : 0m;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO part (drawing_number, revision, description, unit_price, updated_at)
            VALUES (@dn, @rev, @desc, @price, datetime('now','localtime'))
            ON CONFLICT(drawing_number) DO UPDATE SET
                revision    = excluded.revision,
                description = excluded.description,
                unit_price  = excluded.unit_price,
                updated_at  = datetime('now','localtime')
            """;
        cmd.Parameters.AddWithValue("@dn",    drawingNumber);
        cmd.Parameters.AddWithValue("@rev",   revision);
        cmd.Parameters.AddWithValue("@desc",  description);
        cmd.Parameters.AddWithValue("@price", price);
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT id FROM part WHERE drawing_number = @dn COLLATE NOCASE";
        return Convert.ToInt64(cmd.ExecuteScalar()!);
    }

    /// <summary>
    /// Loads the PO header (po_number, oe_number) for the single-PO detail page.
    /// </summary>
    /// <param name="poId">purchase_order.id</param>
    /// <returns>Null if no PO with that id exists.</returns>
    public PoHeader? GetPoHeader(int poId)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT po_number, oe_number FROM purchase_order WHERE id = @poId";
            cmd.Parameters.AddWithValue("@poId", poId);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new PoHeader(
                    poId,
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1));
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PoRepository.GetPoHeader failed for poId={poId}: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Loads every order_item line under the given PO, grouped by job, for the single-PO detail page.
    /// </summary>
    /// <param name="poId">purchase_order.id</param>
    public List<PoOrderItemRow> GetPoOrderItems(int poId)
    {
        var results = new List<PoOrderItemRow>();
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT oi.id, j.job_number, oi.line_number, oi.part_id, oi.quantity,
                       oi.drawing_release_date, oi.delivery_required_date
                FROM job j
                JOIN order_item oi ON oi.job_id = j.id
                WHERE j.po_id = @poId
                ORDER BY j.job_number, oi.line_number
                """;
            cmd.Parameters.AddWithValue("@poId", poId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new PoOrderItemRow(
                    OrderItemId: reader.GetInt32(0),
                    JobNumber:   reader.GetString(1),
                    LineNumber:  reader.GetInt32(2),
                    PartId:      reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    Quantity:    reader.GetInt32(4),
                    ReleaseDate: reader.IsDBNull(5) ? null : reader.GetString(5),
                    DueDate:     reader.IsDBNull(6) ? null : reader.GetString(6)
                ));
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PoRepository.GetPoOrderItems failed for poId={poId}: {ex.Message}");
        }
        return results;
    }
}

/// <param name="PoId">purchase_order.id</param>
/// <param name="PoNumber">Purchase order number</param>
/// <param name="OeNumber">Customer OE/order number</param>
public record PoHeader(int PoId, string PoNumber, string? OeNumber);

/// <param name="OrderItemId">order_item.id</param>
/// <param name="JobNumber">Job number</param>
/// <param name="LineNumber">Order item line number</param>
/// <param name="PartId">part.id; null if the order item has no linked part yet</param>
/// <param name="Quantity">Order item quantity</param>
/// <param name="ReleaseDate">Drawing release date</param>
/// <param name="DueDate">Delivery required date</param>
public record PoOrderItemRow(
    int OrderItemId, string JobNumber, int LineNumber, int? PartId,
    int Quantity, string? ReleaseDate, string? DueDate);

/// <param name="PoId">purchase_order.id for navigation to the single-PO page</param>
/// <param name="PoNumber">Purchase order number</param>
/// <param name="OeNumber">Customer OE/order number</param>
/// <param name="JobNumber">Job number</param>
/// <param name="LineNumber">Order item line number</param>
/// <param name="CustomerName">Customer name</param>
/// <param name="ContactName">Customer contact name</param>
/// <param name="Quantity">Order item quantity</param>
/// <param name="DrawingNumber">Part drawing number</param>
/// <param name="Revision">Part revision</param>
/// <param name="Description">Part description</param>
/// <param name="ReleaseDate">Drawing release date</param>
/// <param name="DueDate">Delivery required date</param>
/// <param name="PartId">part.id; null if no linked part</param>
/// <param name="OrderItemId">order_item.id</param>
public record PoListRow(
    int PoId, string PoNumber, string? OeNumber,
    string JobNumber, int LineNumber,
    string? CustomerName, string? ContactName,
    int Quantity, string? DrawingNumber, string? Revision, string? Description,
    string? ReleaseDate, string? DueDate,
    int? PartId = null, int OrderItemId = 0, bool HasChildren = false);

/// <summary>Flat child drawing row returned by GetChildDrawings.</summary>
public record ChildDrawingRow(int PartId, string DrawingNumber, string? Revision, string? Description);

/// <summary>Customer lookup row.</summary>
public record CustomerRow(int Id, string Name);

/// <summary>All fields needed to populate a Manufacturing Process Excel file.</summary>
/// <param name="PoNumber">purchase_order.po_number</param>
/// <param name="OeNumber">purchase_order.oe_number</param>
/// <param name="JobNumber">job.job_number</param>
/// <param name="LineNumber">order_item.line_number</param>
/// <param name="DrawingNumber">part.drawing_number</param>
/// <param name="Revision">part.revision</param>
/// <param name="Description">part.description</param>
/// <param name="ReleaseDate">order_item.drawing_release_date</param>
/// <param name="DueDate">order_item.delivery_required_date</param>
/// <param name="Quantity">order_item.quantity</param>
/// <param name="CustomerName">customer.customer_name</param>
/// <param name="PartId">part.id (0 if no part linked)</param>
/// <param name="OrderItemId">order_item.id</param>
public record MpContext(
    string PoNumber, string? OeNumber,
    string JobNumber, int LineNumber,
    string? DrawingNumber, string? Revision, string? Description,
    string? ReleaseDate, string? DueDate,
    int Quantity, string? CustomerName,
    int PartId, int OrderItemId);

/// <summary>Contact lookup row.</summary>
public record ContactRow(int Id, string Name);

/// <summary>Input DTO for creating a new job cascade.</summary>
public class NewJobInput
{
    public string OeNumber      { get; set; } = string.Empty;
    public string JobNumber     { get; set; } = string.Empty;
    public string CustomerName  { get; set; } = string.Empty;
    public string ContactName   { get; set; } = string.Empty;
    public int    Quantity      { get; set; } = 1;
    public string DrawingNumber { get; set; } = string.Empty;
    public string Revision      { get; set; } = string.Empty;
    public int    LineNumber    { get; set; } = 1;
    public string Description   { get; set; } = string.Empty;
    public string UnitPrice     { get; set; } = string.Empty;
    public string PoNumber      { get; set; } = string.Empty;
    public string DeliveryDate  { get; set; } = string.Empty;
}

/// <summary>Input DTO for editing an existing order_item cascade.</summary>
public class EditItemInput
{
    public int    OrderItemId   { get; set; }
    public string DrawingNumber { get; set; } = string.Empty;
    public string Revision      { get; set; } = string.Empty;
    public string Description   { get; set; } = string.Empty;
    public string UnitPrice     { get; set; } = string.Empty;
    public int    Quantity      { get; set; } = 1;
    public int    LineNumber    { get; set; } = 1;
    public string DeliveryDate  { get; set; } = string.Empty;
    public string ContactName   { get; set; } = string.Empty;
    public string CustomerName  { get; set; } = string.Empty;
    public int    PoId          { get; set; }
}
