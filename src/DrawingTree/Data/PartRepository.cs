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
