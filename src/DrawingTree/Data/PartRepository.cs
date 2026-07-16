/// <summary>
/// PartRepository.cs
/// Database queries for the Part detail page: part header, drawing_file list,
/// process_template + step_tracker join, and part_note CRUD.
/// </summary>

using DrawingTree.Logging;

namespace DrawingTree.Data;

public class PartRepository
{
    /// <summary>
    /// Queries the part header (revision/description/is_assembly) for the Part detail page.
    /// </summary>
    /// <param name="partId">part.id</param>
    /// <returns>Null if no part with that id exists.</returns>
    public PartHeader? GetPartHeader(int partId)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, drawing_number, revision, description, is_assembly
                FROM part WHERE id = @partId
                """;
            cmd.Parameters.AddWithValue("@partId", partId);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new PartHeader(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4) != 0);
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PartRepository.GetPartHeader failed for partId={partId}: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Lists every drawing_file row for the part, active file first, then by created_at descending.
    /// </summary>
    /// <param name="partId">part.id</param>
    public List<PartDrawingFile> GetDrawingFiles(int partId)
    {
        var results = new List<PartDrawingFile>();
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT file_name, file_path, is_active, revision, last_modified_at
                FROM drawing_file
                WHERE part_id = @partId
                ORDER BY is_active DESC, created_at DESC
                """;
            cmd.Parameters.AddWithValue("@partId", partId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new PartDrawingFile(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2) != 0,
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4)));
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PartRepository.GetDrawingFiles failed for partId={partId}: {ex.Message}");
        }
        return results;
    }

    /// <summary>
    /// Lists the part's process template steps, augmented with step_tracker execution data
    /// for the given order item (left join — steps with no tracking yet come back blank).
    /// </summary>
    /// <param name="partId">part.id</param>
    /// <param name="orderItemId">order_item.id whose execution records to show</param>
    public List<ProcessStepRow> GetProcessSteps(int partId, int orderItemId)
    {
        var results = new List<ProcessStepRow>();
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT pt.row_number, pt.shop_code, pt.description, pt.remark,
                       st.operator_id, st.machine_id, st.status, st.start_time, st.end_time
                FROM process_template pt
                LEFT JOIN step_tracker st
                    ON st.process_template_id = pt.id AND st.order_item_id = @orderItemId
                WHERE pt.part_id = @partId
                ORDER BY pt.row_number
                """;
            cmd.Parameters.AddWithValue("@partId", partId);
            cmd.Parameters.AddWithValue("@orderItemId", orderItemId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new ProcessStepRow(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8)));
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PartRepository.GetProcessSteps failed for partId={partId}, orderItemId={orderItemId}: {ex.Message}");
        }
        return results;
    }

    /// <summary>
    /// Lists all notes for the part, newest first.
    /// </summary>
    /// <param name="partId">part.id</param>
    public List<PartNoteRow> GetPartNotes(int partId)
    {
        var results = new List<PartNoteRow>();
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, content, author, created_at
                FROM part_note
                WHERE part_id = @partId
                ORDER BY created_at DESC
                """;
            cmd.Parameters.AddWithValue("@partId", partId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new PartNoteRow(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetString(3)));
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PartRepository.GetPartNotes failed for partId={partId}: {ex.Message}");
        }
        return results;
    }

    /// <summary>
    /// Inserts a new note for the part, authored by the current Windows user.
    /// </summary>
    /// <param name="partId">part.id</param>
    /// <param name="content">Note text</param>
    public void AddPartNote(int partId, string content)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO part_note (part_id, content, author)
                VALUES (@partId, @content, @author)
                """;
            cmd.Parameters.AddWithValue("@partId", partId);
            cmd.Parameters.AddWithValue("@content", content);
            cmd.Parameters.AddWithValue("@author", Environment.UserName);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PartRepository.AddPartNote failed for partId={partId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates an existing note's content, re-stamping the author as the current Windows user.
    /// </summary>
    /// <param name="noteId">part_note.id</param>
    /// <param name="content">New note text</param>
    public void UpdatePartNote(int noteId, string content)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE part_note
                SET content = @content, author = @author, updated_at = datetime('now','localtime')
                WHERE id = @id
                """;
            cmd.Parameters.AddWithValue("@id", noteId);
            cmd.Parameters.AddWithValue("@content", content);
            cmd.Parameters.AddWithValue("@author", Environment.UserName);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PartRepository.UpdatePartNote failed for noteId={noteId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Deletes a note by id.
    /// </summary>
    /// <param name="noteId">part_note.id</param>
    public void DeletePartNote(int noteId)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM part_note WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", noteId);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PartRepository.DeletePartNote failed for noteId={noteId}: {ex.Message}");
        }
    }

    // ── MP Attachments ────────────────────────────────────────────────────

    /// <summary>
    /// Returns all MP file attachments recorded for the given part.
    /// </summary>
    /// <param name="partId">part.id</param>
    public List<MpAttachmentRow> GetMpAttachments(int partId)
    {
        var results = new List<MpAttachmentRow>();
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, file_name, file_path
                FROM part_attachment
                WHERE part_id = @pid AND file_type = 'MP' COLLATE NOCASE
                ORDER BY created_at DESC
                """;
            cmd.Parameters.AddWithValue("@pid", partId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add(new MpAttachmentRow(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PartRepository.GetMpAttachments failed for partId={partId}: {ex.Message}");
        }
        return results;
    }

    /// <summary>
    /// Checks whether an MP attachment already exists for the given part under the given order item.
    /// </summary>
    /// <param name="partId">part.id</param>
    /// <param name="orderItemId">order_item.id</param>
    /// <returns>True if a matching part_attachment row (part_id and order_item_id both match) exists</returns>
    public bool HasOrderItemMpAttachment(int partId, int orderItemId)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT 1
                FROM part_attachment
                WHERE part_id = @pid AND order_item_id = @oid AND file_type = 'MP' COLLATE NOCASE
                LIMIT 1
                """;
            cmd.Parameters.AddWithValue("@pid", partId);
            cmd.Parameters.AddWithValue("@oid", orderItemId);
            using var reader = cmd.ExecuteReader();
            return reader.Read();
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PartRepository.HasOrderItemMpAttachment failed for partId={partId}, orderItemId={orderItemId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Inserts a new MP file attachment record.
    /// </summary>
    /// <param name="partId">part.id (0 if no part linked)</param>
    /// <param name="orderItemId">order_item.id</param>
    /// <param name="fileName">File name (without path)</param>
    /// <param name="filePath">Full file path</param>
    public void AddMpAttachment(int partId, int orderItemId, string fileName, string filePath)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO part_attachment (part_id, order_item_id, file_type, file_name, file_path, is_active)
                VALUES (@partId, @oid, 'MP', @name, @path, 1)
                """;
            cmd.Parameters.AddWithValue("@partId", partId > 0 ? partId : DBNull.Value);
            cmd.Parameters.AddWithValue("@oid",    orderItemId);
            cmd.Parameters.AddWithValue("@name",   fileName);
            cmd.Parameters.AddWithValue("@path",   filePath);
            cmd.ExecuteNonQuery();
            Logger.Instance.Info($"PartRepository.AddMpAttachment: recorded '{filePath}'");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PartRepository.AddMpAttachment failed for '{filePath}': {ex.Message}");
        }
    }

    /// <summary>
    /// Deletes a MP attachment record from the database.
    /// </summary>
    /// <param name="attachmentId">part_attachment.id</param>
    public void RemoveMpAttachment(int attachmentId)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM part_attachment WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", attachmentId);
            cmd.ExecuteNonQuery();
            Logger.Instance.Info($"PartRepository.RemoveMpAttachment: removed attachment id={attachmentId}");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PartRepository.RemoveMpAttachment failed for id={attachmentId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns all DIR file attachments recorded for every part sharing the given
    /// drawing_number, regardless of revision or order.
    /// </summary>
    /// <param name="drawingNumber">part.drawing_number</param>
    public List<DirAttachmentRow> GetDirAttachmentsByDrawingNumber(string drawingNumber)
    {
        var results = new List<DirAttachmentRow>();
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT pa.id, pa.file_name, pa.file_path, pa.status, pa.order_item_id, pa.created_at, p.revision
                FROM part_attachment pa
                JOIN part p ON p.id = pa.part_id
                WHERE pa.file_type = 'DIR' COLLATE NOCASE
                  AND pa.part_id IN (SELECT id FROM part WHERE drawing_number = @dn COLLATE NOCASE)
                ORDER BY pa.created_at DESC
                """;
            cmd.Parameters.AddWithValue("@dn", drawingNumber);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new DirAttachmentRow(
                    reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.GetString(5), reader.GetString(6)));
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PartRepository.GetDirAttachmentsByDrawingNumber failed for drawingNumber={drawingNumber}: {ex.Message}");
        }
        return results;
    }

    /// <summary>
    /// Inserts a new DIR file attachment record.
    /// </summary>
    /// <param name="partId">part.id (0 if no part linked)</param>
    /// <param name="orderItemId">order_item.id</param>
    /// <param name="fileName">File name (without path)</param>
    /// <param name="filePath">Full file path</param>
    public void AddDirAttachment(int partId, int orderItemId, string fileName, string filePath)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO part_attachment (part_id, order_item_id, file_type, file_name, file_path, is_active)
                VALUES (@partId, @oid, 'DIR', @name, @path, 1)
                """;
            cmd.Parameters.AddWithValue("@partId", partId > 0 ? partId : DBNull.Value);
            cmd.Parameters.AddWithValue("@oid",    orderItemId);
            cmd.Parameters.AddWithValue("@name",   fileName);
            cmd.Parameters.AddWithValue("@path",   filePath);
            cmd.ExecuteNonQuery();
            Logger.Instance.Info($"PartRepository.AddDirAttachment: recorded '{filePath}'");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PartRepository.AddDirAttachment failed for '{filePath}': {ex.Message}");
        }
    }

    /// <summary>
    /// Deletes a DIR attachment record from the database.
    /// </summary>
    /// <param name="attachmentId">part_attachment.id</param>
    public void RemoveDirAttachment(int attachmentId)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM part_attachment WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", attachmentId);
            cmd.ExecuteNonQuery();
            Logger.Instance.Info($"PartRepository.RemoveDirAttachment: removed attachment id={attachmentId}");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PartRepository.RemoveDirAttachment failed for id={attachmentId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the most recent DIR attachment for the given part that is also linked
    /// to the given order item, or null if none exists.
    /// </summary>
    /// <param name="partId">part.id</param>
    /// <param name="orderItemId">order_item.id</param>
    public PartAttachmentRow? GetDirAttachmentForOrderItem(int partId, int orderItemId)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, file_name, file_path, status
                FROM part_attachment
                WHERE part_id = @pid AND order_item_id = @oid AND file_type = 'DIR' COLLATE NOCASE
                ORDER BY created_at DESC
                LIMIT 1
                """;
            cmd.Parameters.AddWithValue("@pid", partId);
            cmd.Parameters.AddWithValue("@oid", orderItemId);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new PartAttachmentRow(
                    reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PartRepository.GetDirAttachmentForOrderItem failed for partId={partId}, orderItemId={orderItemId}: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Returns the most recent bubble drawing attachment recorded for the given part,
    /// or null if none exists.
    /// </summary>
    /// <param name="partId">part.id</param>
    public PartAttachmentRow? GetBubbleAttachment(int partId)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, file_name, file_path, status
                FROM part_attachment
                WHERE part_id = @pid AND file_type = 'BUBBLE' COLLATE NOCASE
                ORDER BY created_at DESC
                LIMIT 1
                """;
            cmd.Parameters.AddWithValue("@pid", partId);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new PartAttachmentRow(
                    reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PartRepository.GetBubbleAttachment failed for partId={partId}: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Returns all bubble drawing attachments recorded for every part sharing the given
    /// drawing_number, regardless of revision.
    /// </summary>
    /// <param name="drawingNumber">part.drawing_number</param>
    public List<BubbleAttachmentRow> GetBubbleAttachmentsByDrawingNumber(string drawingNumber)
    {
        var results = new List<BubbleAttachmentRow>();
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT pa.id, pa.file_name, pa.file_path, pa.created_at
                FROM part_attachment pa
                WHERE pa.file_type = 'bubble' COLLATE NOCASE
                  AND pa.part_id IN (SELECT id FROM part WHERE drawing_number = @dn COLLATE NOCASE)
                ORDER BY pa.created_at DESC
                """;
            cmd.Parameters.AddWithValue("@dn", drawingNumber);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add(new BubbleAttachmentRow(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PartRepository.GetBubbleAttachmentsByDrawingNumber failed for drawingNumber={drawingNumber}: {ex.Message}");
        }
        return results;
    }

    /// <summary>
    /// Deletes a bubble drawing attachment record from the database.
    /// </summary>
    /// <param name="attachmentId">part_attachment.id</param>
    public void RemoveBubbleAttachment(int attachmentId)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM part_attachment WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", attachmentId);
            cmd.ExecuteNonQuery();
            Logger.Instance.Info($"PartRepository.RemoveBubbleAttachment: removed attachment id={attachmentId}");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PartRepository.RemoveBubbleAttachment failed for id={attachmentId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates the status field of a part_attachment record.
    /// </summary>
    /// <param name="attachmentId">part_attachment.id</param>
    /// <param name="status">New status value (e.g. "reviewed")</param>
    public bool UpdateAttachmentStatus(int attachmentId, string status)
    {
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE part_attachment
                SET status = @status, updated_at = datetime('now','localtime')
                WHERE id = @id
                """;
            cmd.Parameters.AddWithValue("@status", status);
            cmd.Parameters.AddWithValue("@id", attachmentId);
            cmd.ExecuteNonQuery();
            Logger.Instance.Info($"PartRepository.UpdateAttachmentStatus: attachment id={attachmentId} -> '{status}'");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"PartRepository.UpdateAttachmentStatus failed for id={attachmentId}: {ex.Message}");
            return false;
        }
    }
}

/// <param name="PartId">part.id</param>
/// <param name="DrawingNumber">Drawing number</param>
/// <param name="Revision">Current revision</param>
/// <param name="Description">Drawing description</param>
/// <param name="IsAssembly">True/False, or null when unknown</param>
public record PartHeader(int PartId, string DrawingNumber, string Revision, string? Description, bool? IsAssembly);

/// <param name="FileName">drawing_file.file_name</param>
/// <param name="FilePath">drawing_file.file_path</param>
/// <param name="IsActive">Whether this is the currently active file</param>
/// <param name="Revision">drawing_file.revision</param>
/// <param name="LastModifiedAt">drawing_file.last_modified_at</param>
public record PartDrawingFile(string FileName, string FilePath, bool IsActive, string Revision, string? LastModifiedAt);

/// <param name="RowNumber">process_template.row_number</param>
/// <param name="ShopCode">process_template.shop_code</param>
/// <param name="Description">process_template.description</param>
/// <param name="Remark">process_template.remark</param>
/// <param name="OperatorId">step_tracker.operator_id; null if not yet tracked</param>
/// <param name="MachineId">step_tracker.machine_id; null if not yet tracked</param>
/// <param name="Status">step_tracker.status; null if not yet tracked</param>
/// <param name="StartTime">step_tracker.start_time; null if not yet tracked</param>
/// <param name="EndTime">step_tracker.end_time; null if not yet tracked</param>
public record ProcessStepRow(
    int RowNumber, string ShopCode, string? Description, string? Remark,
    string? OperatorId, string? MachineId, string? Status, string? StartTime, string? EndTime);

/// <param name="Id">part_note.id</param>
/// <param name="Content">Note text</param>
/// <param name="Author">Note author (Windows username)</param>
/// <param name="CreatedAt">Creation timestamp</param>
public record PartNoteRow(int Id, string Content, string? Author, string CreatedAt);

/// <param name="AttachmentId">part_attachment.id</param>
/// <param name="FileName">File name (without path)</param>
/// <param name="FilePath">Full file path</param>
public record MpAttachmentRow(int AttachmentId, string FileName, string FilePath);

/// <param name="AttachmentId">part_attachment.id</param>
/// <param name="FileName">File name (without path)</param>
/// <param name="FilePath">Full file path</param>
/// <param name="Status">part_attachment.status</param>
/// <param name="OrderItemId">part_attachment.order_item_id; null if not linked to an order item</param>
/// <param name="CreatedAt">part_attachment.created_at</param>
/// <param name="Revision">part.revision of the attachment's part</param>
public record DirAttachmentRow(
    int AttachmentId, string FileName, string FilePath, string Status,
    int? OrderItemId, string CreatedAt, string Revision);

/// <param name="AttachmentId">part_attachment.id</param>
/// <param name="FileName">File name (without path)</param>
/// <param name="FilePath">Full file path</param>
/// <param name="Status">part_attachment.status</param>
public record PartAttachmentRow(int AttachmentId, string FileName, string FilePath, string Status);

/// <param name="AttachmentId">part_attachment.id</param>
/// <param name="FileName">File name (without path)</param>
/// <param name="FilePath">Full file path</param>
/// <param name="CreatedAt">part_attachment.created_at</param>
public record BubbleAttachmentRow(int AttachmentId, string FileName, string FilePath, string CreatedAt);
