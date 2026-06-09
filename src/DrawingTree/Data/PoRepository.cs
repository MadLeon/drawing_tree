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
}
