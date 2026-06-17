/// <summary>
/// DrawingRepository.cs
/// Database queries for drawing info lookup, drawing_file UPSERT, part update, and part_tree save.
/// </summary>
/// <remarks>
/// Usage:
/// - GetDrawingInfo():      query part + active drawing_file by drawing number
/// - UpsertDrawingFile():   set active file for a part (INSERT or UPDATE on file_path conflict)
/// - UpdatePart():          save description/revision/is_assembly changes back to part table
/// - ComputeTreeChanges():  dry-run diff of current tree vs DB (no writes)
/// - SaveTree():            persist current part_tree structure (INSERT new / UPDATE quantity / DELETE removed)
/// </remarks>

using DrawingTree.Logging;
using DrawingTree.Models;
using Microsoft.Data.Sqlite;

namespace DrawingTree.Data;

public class DrawingRepository
{
    /// <summary>
    /// Queries part + active drawing_file for a given drawing number.
    /// Returns null if no matching part exists.
    /// </summary>
    /// <param name="drawingNumber">Drawing number to look up</param>
    public DrawingInfo? GetDrawingInfo(string drawingNumber)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT p.id,
                       p.drawing_number,
                       p.revision,
                       p.description,
                       p.is_assembly,
                       p.has_parent,
                       df.file_path
                FROM part p
                LEFT JOIN drawing_file df ON df.part_id = p.id AND df.is_active = 1
                WHERE p.drawing_number = @dn
                LIMIT 1
                """;
            cmd.Parameters.AddWithValue("@dn", drawingNumber);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            return new DrawingInfo
            {
                PartId        = reader.GetInt32(0),
                DrawingNumber = reader.GetString(1),
                Revision      = reader.GetString(2),
                Description   = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                IsAssembly    = !reader.IsDBNull(4) && reader.GetInt32(4) != 0,
                PdfPath       = reader.IsDBNull(6) ? string.Empty : reader.GetString(6)
            };
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"DrawingRepository.GetDrawingInfo failed for '{drawingNumber}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Queries part + active drawing_file for a given part ID.
    /// Returns null if no matching part exists.
    /// </summary>
    /// <param name="partId">part.id to look up</param>
    public DrawingInfo? GetDrawingInfo(int partId)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT p.id,
                       p.drawing_number,
                       p.revision,
                       p.description,
                       p.is_assembly,
                       p.has_parent,
                       df.file_path
                FROM part p
                LEFT JOIN drawing_file df ON df.part_id = p.id AND df.is_active = 1
                WHERE p.id = @pid
                LIMIT 1
                """;
            cmd.Parameters.AddWithValue("@pid", partId);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            return new DrawingInfo
            {
                PartId        = reader.GetInt32(0),
                DrawingNumber = reader.GetString(1),
                Revision      = reader.GetString(2),
                Description   = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                IsAssembly    = !reader.IsDBNull(4) && reader.GetInt32(4) != 0,
                PdfPath       = reader.IsDBNull(6) ? string.Empty : reader.GetString(6)
            };
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"DrawingRepository.GetDrawingInfo failed for partId={partId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Inserts a new part record. Returns the new part.id, or -1 on failure.
    /// </summary>
    /// <param name="drawingNumber">Drawing number</param>
    /// <param name="revision">Revision (use "-" if unknown)</param>
    public int InsertPart(string drawingNumber, string revision)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO part (drawing_number, revision)
                VALUES (@dn, @rev);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("@dn",  drawingNumber);
            cmd.Parameters.AddWithValue("@rev", string.IsNullOrEmpty(revision) ? "-" : revision);

            var result = cmd.ExecuteScalar();
            Logger.Instance.Warning($"Part not found in DB, created new: {drawingNumber} rev {revision}");
            return Convert.ToInt32(result);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"DrawingRepository.InsertPart failed for '{drawingNumber}': {ex.Message}");
            return -1;
        }
    }

    /// <summary>
    /// Updates description, revision, and is_assembly for an existing part.
    /// </summary>
    /// <param name="partId">part.id to update</param>
    /// <param name="revision">New revision value</param>
    /// <param name="description">New description</param>
    /// <param name="isAssembly">New is_assembly flag</param>
    public bool UpdatePart(int partId, string revision, string description, bool isAssembly)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE part
                SET revision    = @rev,
                    description = @desc,
                    is_assembly = @asm,
                    updated_at  = datetime('now', 'localtime')
                WHERE id = @id
                """;
            cmd.Parameters.AddWithValue("@rev",  revision);
            cmd.Parameters.AddWithValue("@desc", description);
            cmd.Parameters.AddWithValue("@asm",  isAssembly ? 1 : 0);
            cmd.Parameters.AddWithValue("@id",   partId);
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"DrawingRepository.UpdatePart failed for partId={partId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Deactivates all existing drawing_file records for the part, then upserts the new file path.
    /// Uses ON CONFLICT(file_path) DO UPDATE to handle G-drive pre-scanned records.
    /// </summary>
    /// <param name="partId">part.id to associate</param>
    /// <param name="fileName">File name (basename)</param>
    /// <param name="filePath">Full file path (must be unique)</param>
    /// <param name="revision">Revision label</param>
    public bool UpsertDrawingFile(int partId, string fileName, string filePath, string revision)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var tx = conn.BeginTransaction();

            // Step A: deactivate other active files for this part
            using (var deactivate = conn.CreateCommand())
            {
                deactivate.Transaction = tx;
                deactivate.CommandText = """
                    UPDATE drawing_file
                    SET    is_active  = 0,
                           updated_at = datetime('now', 'localtime')
                    WHERE  part_id = @pid
                    """;
                deactivate.Parameters.AddWithValue("@pid", partId);
                deactivate.ExecuteNonQuery();
            }

            // Step B: upsert the target file
            using (var upsert = conn.CreateCommand())
            {
                upsert.Transaction = tx;
                upsert.CommandText = """
                    INSERT INTO drawing_file (part_id, file_name, file_path, is_active, revision)
                    VALUES (@pid, @fn, @fp, 1, @rev)
                    ON CONFLICT(file_path) DO UPDATE SET
                        part_id    = excluded.part_id,
                        file_name  = excluded.file_name,
                        is_active  = 1,
                        revision   = excluded.revision,
                        updated_at = datetime('now', 'localtime')
                    """;
                upsert.Parameters.AddWithValue("@pid", partId);
                upsert.Parameters.AddWithValue("@fn",  fileName);
                upsert.Parameters.AddWithValue("@fp",  filePath);
                upsert.Parameters.AddWithValue("@rev", revision);
                upsert.ExecuteNonQuery();
            }

            tx.Commit();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"DrawingRepository.UpsertDrawingFile failed for partId={partId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Computes what SaveTree() would do without writing anything.
    /// Returns counts of added/deleted/modified relationships and the list of deleted items.
    /// </summary>
    /// <param name="rootNodes">Current root nodes of the tree</param>
    public TreeChangeSummary ComputeTreeChanges(IEnumerable<DrawingNode> rootNodes)
    {
        int added = 0, deleted = 0, modified = 0;
        var deletedItems = new List<DeletedRelationship>();

        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            foreach (var root in rootNodes)
                CollectNodeChanges(conn, root, ref added, ref deleted, ref modified, deletedItems);
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"DrawingRepository.ComputeTreeChanges failed: {ex.Message}");
            throw;
        }

        return new TreeChangeSummary(added, deleted, modified, deletedItems);
    }

    private static void CollectNodeChanges(SqliteConnection conn, DrawingNode parent,
        ref int added, ref int deleted, ref int modified, List<DeletedRelationship> deletedItems)
    {
        if (parent.Drawing.PartId == null) return;
        int parentPartId = parent.Drawing.PartId.Value;

        var currentChildIds = parent.Children
            .Where(c => c.Drawing.PartId != null)
            .Select(c => c.Drawing.PartId!.Value)
            .ToHashSet();

        // Find relationships in DB that are absent from current tree → deleted
        using var sel = conn.CreateCommand();
        sel.CommandText = """
            SELECT pt.child_id, p.drawing_number, p.revision
            FROM part_tree pt
            JOIN part p ON p.id = pt.child_id
            WHERE pt.parent_id = @pid
            """;
        sel.Parameters.AddWithValue("@pid", parentPartId);
        using (var reader = sel.ExecuteReader())
        {
            while (reader.Read())
            {
                int dbChildId = reader.GetInt32(0);
                if (!currentChildIds.Contains(dbChildId))
                {
                    deleted++;
                    deletedItems.Add(new DeletedRelationship(
                        parent.Drawing.DrawingNumber,
                        reader.GetString(1),
                        reader.GetString(2)));
                }
            }
        }

        // Check current children for new vs modified
        foreach (var child in parent.Children)
        {
            if (child.Drawing.PartId == null) continue;
            int quantity = int.TryParse(child.Drawing.QuantityInAssembly, out var q) ? q : 1;

            if (child.PartTreeId != null)
            {
                using var qCmd = conn.CreateCommand();
                qCmd.CommandText = "SELECT quantity FROM part_tree WHERE id = @id";
                qCmd.Parameters.AddWithValue("@id", child.PartTreeId.Value);
                var dbQty = Convert.ToInt32(qCmd.ExecuteScalar());
                if (dbQty != quantity) modified++;
            }
            else
            {
                added++;
            }

            CollectNodeChanges(conn, child, ref added, ref deleted, ref modified, deletedItems);
        }
    }

    /// <summary>
    /// Persists the current in-memory tree to part_tree:
    ///   - New relationships: INSERT
    ///   - Changed quantity: UPDATE
    ///   - Relationships absent from current tree: DELETE
    /// </summary>
    /// <param name="rootNodes">Current root nodes of the tree</param>
    public void SaveTree(IEnumerable<DrawingNode> rootNodes)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var tx = conn.BeginTransaction();

            foreach (var root in rootNodes)
                SaveNodeChildren(conn, tx, root);

            tx.Commit();
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"DrawingRepository.SaveTree failed: {ex.Message}");
            throw;
        }
    }

    private void SaveNodeChildren(SqliteConnection conn, SqliteTransaction tx, DrawingNode parent)
    {
        if (parent.Drawing.PartId == null) return;
        int parentPartId = parent.Drawing.PartId.Value;

        // Collect child part IDs present in the current tree
        var currentChildIds = parent.Children
            .Where(c => c.Drawing.PartId != null)
            .Select(c => c.Drawing.PartId!.Value)
            .ToHashSet();

        // Delete DB relationships absent from current tree
        DeleteRemovedChildren(conn, tx, parentPartId, currentChildIds);

        foreach (var child in parent.Children)
        {
            if (child.Drawing.PartId == null) continue;
            int childPartId = child.Drawing.PartId.Value;
            int quantity = int.TryParse(child.Drawing.QuantityInAssembly, out var q) ? q : 1;

            if (child.PartTreeId != null)
            {
                // Edge exists — update quantity if changed
                using var upd = conn.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = """
                    UPDATE part_tree
                    SET    quantity   = @qty,
                           updated_at = datetime('now', 'localtime')
                    WHERE  id = @id AND quantity != @qty
                    """;
                upd.Parameters.AddWithValue("@qty", quantity);
                upd.Parameters.AddWithValue("@id",  child.PartTreeId.Value);
                upd.ExecuteNonQuery();
            }
            else
            {
                // New edge — INSERT and store the generated id back on the node
                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = """
                    INSERT INTO part_tree (parent_id, child_id, quantity)
                    VALUES (@pid, @cid, @qty);
                    SELECT last_insert_rowid();
                    """;
                ins.Parameters.AddWithValue("@pid", parentPartId);
                ins.Parameters.AddWithValue("@cid", childPartId);
                ins.Parameters.AddWithValue("@qty", quantity);
                var newId = ins.ExecuteScalar();
                child.PartTreeId = Convert.ToInt32(newId);

                // Mark child as having a parent
                using var mark = conn.CreateCommand();
                mark.Transaction = tx;
                mark.CommandText = "UPDATE part SET has_parent = 1 WHERE id = @id";
                mark.Parameters.AddWithValue("@id", childPartId);
                mark.ExecuteNonQuery();
            }

            // Recurse into grandchildren
            SaveNodeChildren(conn, tx, child);
        }
    }

    /// <summary>
    /// Returns the quantity from part_tree for the given child part.
    /// If the part appears in multiple assemblies, returns the first found value.
    /// Returns empty string if the part has no part_tree entry.
    /// </summary>
    /// <param name="partId">part.id of the child part</param>
    public string GetPartQuantity(int partId)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT quantity FROM part_tree WHERE child_id = @pid LIMIT 1";
            cmd.Parameters.AddWithValue("@pid", partId);
            var result = cmd.ExecuteScalar();
            return result == null || result == DBNull.Value
                ? string.Empty
                : result.ToString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"DrawingRepository.GetPartQuantity failed for partId={partId}: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Updates quantity for all part_tree entries where this part is a child.
    /// No-op when quantity is empty. Parses value as integer; defaults to 1 on parse failure.
    /// </summary>
    /// <param name="partId">child part.id</param>
    /// <param name="quantity">New quantity string</param>
    public bool UpdatePartTreeQuantity(int partId, string quantity)
    {
        if (string.IsNullOrWhiteSpace(quantity)) return true;
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE part_tree
                SET    quantity   = @qty,
                       updated_at = datetime('now', 'localtime')
                WHERE  child_id = @pid
                """;
            int qty = int.TryParse(quantity, out var q) ? q : 1;
            cmd.Parameters.AddWithValue("@qty", qty);
            cmd.Parameters.AddWithValue("@pid", partId);
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"DrawingRepository.UpdatePartTreeQuantity failed for partId={partId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Searches parts by drawing number using a LIKE fuzzy match.
    /// Returns up to 100 results ordered by drawing_number.
    /// </summary>
    /// <param name="query">Search term (wrapped in % wildcards internally)</param>
    /// <returns>List of matching DrawingInfo records</returns>
    public List<DrawingInfo> SearchParts(string query)
    {
        var results = new List<DrawingInfo>();
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT p.id,
                       p.drawing_number,
                       p.revision,
                       p.description,
                       p.is_assembly
                FROM part p
                WHERE p.drawing_number LIKE @query
                ORDER BY p.drawing_number
                LIMIT 100
                """;
            cmd.Parameters.AddWithValue("@query", "%" + query + "%");

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new DrawingInfo
                {
                    PartId        = reader.GetInt32(0),
                    DrawingNumber = reader.GetString(1),
                    Revision      = reader.GetString(2),
                    Description   = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    IsAssembly    = !reader.IsDBNull(4) && reader.GetInt32(4) != 0,
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"DrawingRepository.SearchParts failed for '{query}': {ex.Message}");
        }
        return results;
    }

    /// <summary>
    /// Searches parts by drawing number, PO number, or job number.
    /// Resolves drawing-number matches upward through part_tree to surface PO/Job context.
    /// Returns up to 200 deduplicated results; drawing matches take priority over PO/job matches.
    /// </summary>
    /// <param name="query">Search term (wrapped in % wildcards internally)</param>
    public List<SearchResultRow> SearchPartsWithJobContext(string query)
    {
        var raw = new List<(int PoId, int PartId, string PoNumber, string JobNumber,
                             string DrawingNumber, string Revision, string Description,
                             SearchMatchSource MatchSource)>();
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                WITH RECURSIVE ancestors(original_part_id, ancestor_part_id, depth) AS (
                    SELECT p.id, p.id, 0
                    FROM part p
                    WHERE p.drawing_number LIKE @query

                    UNION ALL

                    SELECT a.original_part_id, pt.parent_id, a.depth + 1
                    FROM part_tree pt
                    JOIN ancestors a ON pt.child_id = a.ancestor_part_id
                    WHERE a.depth < 30
                )
                SELECT DISTINCT
                    po.id, p_orig.id,
                    po.po_number, j.job_number,
                    p_orig.drawing_number, p_orig.revision, p_orig.description,
                    1 AS match_source
                FROM ancestors a
                JOIN order_item oi ON oi.part_id = a.ancestor_part_id
                JOIN job j ON j.id = oi.job_id
                JOIN purchase_order po ON po.id = j.po_id
                JOIN part p_orig ON p_orig.id = a.original_part_id

                UNION ALL

                SELECT DISTINCT
                    po.id, p.id,
                    po.po_number, j.job_number,
                    p.drawing_number, p.revision, p.description,
                    2 AS match_source
                FROM purchase_order po
                JOIN job j ON j.po_id = po.id
                JOIN order_item oi ON oi.job_id = j.id
                JOIN part p ON p.id = oi.part_id
                WHERE po.po_number LIKE @query

                UNION ALL

                SELECT DISTINCT
                    po.id, p.id,
                    po.po_number, j.job_number,
                    p.drawing_number, p.revision, p.description,
                    3 AS match_source
                FROM purchase_order po
                JOIN job j ON j.po_id = po.id
                JOIN order_item oi ON oi.job_id = j.id
                JOIN part p ON p.id = oi.part_id
                WHERE j.job_number LIKE @query
                """;
            cmd.Parameters.AddWithValue("@query", "%" + query + "%");

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                int src = reader.GetInt32(7);
                raw.Add((
                    PoId:          reader.GetInt32(0),
                    PartId:        reader.GetInt32(1),
                    PoNumber:      reader.GetString(2),
                    JobNumber:     reader.GetString(3),
                    DrawingNumber: reader.GetString(4),
                    Revision:      reader.GetString(5),
                    Description:   reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    MatchSource:   src == 1 ? SearchMatchSource.Drawing
                                 : src == 2 ? SearchMatchSource.Po
                                 :             SearchMatchSource.Job
                ));
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"DrawingRepository.SearchPartsWithJobContext failed for '{query}': {ex.Message}");
            return new List<SearchResultRow>();
        }

        // Deduplicate by (PoNumber, JobNumber, DrawingNumber); drawing match wins over po/job.
        var seen = new Dictionary<(string, string, string), SearchResultRow>();
        foreach (var r in raw)
        {
            var key = (r.PoNumber, r.JobNumber, r.DrawingNumber);
            if (!seen.TryGetValue(key, out var existing) || r.MatchSource < existing.MatchSource)
            {
                seen[key] = new SearchResultRow(
                    PoId: r.PoId, PartId: r.PartId,
                    PoNumber: r.PoNumber, JobNumber: r.JobNumber,
                    DrawingNumber: r.DrawingNumber, Revision: r.Revision,
                    Description: r.Description, MatchSource: r.MatchSource);
            }
        }

        return seen.Values
            .OrderBy(r => r.PoNumber).ThenBy(r => r.JobNumber).ThenBy(r => r.DrawingNumber)
            .Take(200)
            .ToList();
    }

    private static void DeleteRemovedChildren(SqliteConnection conn, SqliteTransaction tx,
        int parentPartId, HashSet<int> currentChildIds)
    {
        using var sel = conn.CreateCommand();
        sel.Transaction = tx;
        sel.CommandText = """
            SELECT pt.id, pt.child_id, p.drawing_number, p.revision
            FROM part_tree pt
            JOIN part p ON p.id = pt.child_id
            WHERE pt.parent_id = @pid
            """;
        sel.Parameters.AddWithValue("@pid", parentPartId);

        var toDelete = new List<(int treeId, int childPartId, string drawingNumber, string revision)>();
        using (var reader = sel.ExecuteReader())
        {
            while (reader.Read())
            {
                int dbChildId = reader.GetInt32(1);
                if (!currentChildIds.Contains(dbChildId))
                    toDelete.Add((reader.GetInt32(0), dbChildId, reader.GetString(2), reader.GetString(3)));
            }
        }

        foreach (var (treeId, childPartId, drawingNumber, revision) in toDelete)
        {
            using var del = conn.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM part_tree WHERE id = @id";
            del.Parameters.AddWithValue("@id", treeId);
            del.ExecuteNonQuery();
            Logger.Instance.Info(
                $"SaveTree: removed {drawingNumber} rev {revision} from under parent part_id={parentPartId}");

            // Clear has_parent if this child no longer appears under any parent
            using var check = conn.CreateCommand();
            check.Transaction = tx;
            check.CommandText = "SELECT COUNT(*) FROM part_tree WHERE child_id = @cid";
            check.Parameters.AddWithValue("@cid", childPartId);
            var remaining = Convert.ToInt32(check.ExecuteScalar());
            if (remaining == 0)
            {
                using var reset = conn.CreateCommand();
                reset.Transaction = tx;
                reset.CommandText = "UPDATE part SET has_parent = 0 WHERE id = @id";
                reset.Parameters.AddWithValue("@id", childPartId);
                reset.ExecuteNonQuery();
            }
        }
    }
}

/// <param name="ParentDrawingNumber">Drawing number of the parent part</param>
/// <param name="ChildDrawingNumber">Drawing number of the removed child part</param>
/// <param name="ChildRevision">Revision of the removed child part</param>
public record DeletedRelationship(string ParentDrawingNumber, string ChildDrawingNumber, string ChildRevision);

/// <summary>Indicates which field caused a search result to appear.</summary>
public enum SearchMatchSource { Drawing = 1, Po = 2, Job = 3 }

/// <param name="PoId">purchase_order.id for navigation to the single-PO page</param>
/// <param name="PartId">part.id for navigation to drawing viewer</param>
/// <param name="PoNumber">Purchase order number</param>
/// <param name="JobNumber">Job number</param>
/// <param name="DrawingNumber">Matched part drawing number</param>
/// <param name="Revision">Matched part revision</param>
/// <param name="Description">Matched part description</param>
/// <param name="MatchSource">Which field matched the query</param>
public record SearchResultRow(
    int PoId, int PartId, string PoNumber, string JobNumber,
    string DrawingNumber, string Revision, string Description,
    SearchMatchSource MatchSource);

/// <param name="Added">Number of new parent-child relationships to be added</param>
/// <param name="Deleted">Number of existing relationships to be removed</param>
/// <param name="Modified">Number of relationships whose quantity has changed</param>
/// <param name="DeletedItems">Detail of each relationship to be removed</param>
public record TreeChangeSummary(int Added, int Deleted, int Modified, IReadOnlyList<DeletedRelationship> DeletedItems);
