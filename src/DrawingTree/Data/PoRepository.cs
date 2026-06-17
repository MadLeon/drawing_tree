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
                       t.drawing_number,
                       t.revision,
                       t.description,
                       t.is_assembly,
                       t.parent_part_id,
                       t.quantity,
                       df.file_path
                FROM tree t
                LEFT JOIN drawing_file df ON df.part_id = t.part_id AND df.is_active = 1
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
    public List<PoListRow> GetAllPoLines()
    {
        var results = new List<PoListRow>();
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT po.id, po.po_number, po.oe_number,
                       j.job_number, oi.line_number,
                       cust.customer_name, cc.contact_name,
                       oi.quantity, p.drawing_number, p.revision, p.description,
                       oi.drawing_release_date, oi.delivery_required_date
                FROM purchase_order po
                JOIN job j ON j.po_id = po.id
                JOIN order_item oi ON oi.job_id = j.id
                LEFT JOIN part p ON p.id = oi.part_id
                LEFT JOIN customer_contact cc ON cc.id = po.contact_id
                LEFT JOIN customer cust ON cust.id = cc.customer_id
                ORDER BY po.po_number, j.job_number, oi.line_number
                """;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new PoListRow(
                    PoId:         reader.GetInt32(0),
                    PoNumber:     reader.GetString(1),
                    OeNumber:     reader.IsDBNull(2) ? null : reader.GetString(2),
                    JobNumber:    reader.GetString(3),
                    LineNumber:   reader.GetInt32(4),
                    CustomerName: reader.IsDBNull(5) ? null : reader.GetString(5),
                    ContactName:  reader.IsDBNull(6) ? null : reader.GetString(6),
                    Quantity:     reader.GetInt32(7),
                    DrawingNumber: reader.IsDBNull(8) ? null : reader.GetString(8),
                    Revision:     reader.IsDBNull(9) ? null : reader.GetString(9),
                    Description:  reader.IsDBNull(10) ? null : reader.GetString(10),
                    ReleaseDate:  reader.IsDBNull(11) ? null : reader.GetString(11),
                    DueDate:      reader.IsDBNull(12) ? null : reader.GetString(12)
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
public record PoListRow(
    int PoId, string PoNumber, string? OeNumber,
    string JobNumber, int LineNumber,
    string? CustomerName, string? ContactName,
    int Quantity, string? DrawingNumber, string? Revision, string? Description,
    string? ReleaseDate, string? DueDate);
