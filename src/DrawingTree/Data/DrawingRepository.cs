/// <summary>
/// DrawingRepository.cs
/// Database queries for drawing info lookup, drawing_file UPSERT, part update, and part_tree save.
/// </summary>
/// <remarks>
/// Usage:
/// - GetDrawingInfo():    query part + active drawing_file by drawing number
/// - UpsertDrawingFile(): set active file for a part (INSERT or UPDATE on file_path conflict)
/// - UpdatePart():        save description/revision/is_assembly changes back to part table
/// - SaveTree():          persist current part_tree structure (INSERT new / UPDATE quantity / warn orphans)
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
    /// Persists the current in-memory tree to part_tree:
    ///   - New edges: INSERT
    ///   - Changed quantity: UPDATE
    ///   - DB edges absent from current tree: log WARNING (no delete)
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

        // Warn about DB edges absent from current tree
        CheckOrphanedEdges(conn, tx, parentPartId, currentChildIds);

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

    private static void CheckOrphanedEdges(SqliteConnection conn, SqliteTransaction tx,
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

        using var reader = sel.ExecuteReader();
        while (reader.Read())
        {
            int dbChildId = reader.GetInt32(1);
            if (!currentChildIds.Contains(dbChildId))
            {
                Logger.Instance.Warning(
                    $"SaveTree: DB edge (part_tree.id={reader.GetInt32(0)}) " +
                    $"child {reader.GetString(2)} rev {reader.GetString(3)} " +
                    $"exists in DB but is absent from current tree — not deleted");
            }
        }
    }
}
