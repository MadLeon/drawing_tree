/// <summary>
/// DirLogRepository.cs
/// Data access for the DIR Log page: DIR attachments completed within a date range,
/// joined to their PO/Job/Customer context and their matching bubble drawing file.
/// </summary>

using DrawingTree.Logging;

namespace DrawingTree.Data;

public class DirLogRepository
{
    /// <summary>
    /// Returns DIR attachment rows whose updated_at date falls within [startDate, endDate]
    /// (inclusive, local time), each joined to its part/job/PO/customer context and the
    /// latest matching bubble drawing attachment for the same drawing_number.
    /// </summary>
    /// <param name="startDate">Inclusive start of the date range</param>
    /// <param name="endDate">Inclusive end of the date range</param>
    public List<DirLogRow> GetDirLogRows(DateOnly startDate, DateOnly endDate)
    {
        var results = new List<DirLogRow>();
        try
        {
            using var conn = DatabaseConnectionFactory.OpenDevConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT pa.id, pa.file_name, pa.file_path, pa.created_at, pa.updated_at,
                       p.drawing_number, p.revision,
                       po.po_number, j.job_number, cust.customer_name,
                       bub.id, bub.file_name, bub.file_path
                FROM part_attachment pa
                JOIN part p             ON p.id  = pa.part_id
                JOIN order_item oi      ON oi.id = pa.order_item_id
                JOIN job j              ON j.id  = oi.job_id
                JOIN purchase_order po  ON po.id = j.po_id
                LEFT JOIN customer_contact cc ON cc.id   = po.contact_id
                LEFT JOIN customer cust       ON cust.id = cc.customer_id
                LEFT JOIN part_attachment bub ON bub.id = (
                    SELECT b.id FROM part_attachment b
                    WHERE b.file_type = 'BUBBLE' COLLATE NOCASE
                      AND b.part_id IN (SELECT id FROM part WHERE drawing_number = p.drawing_number COLLATE NOCASE)
                    ORDER BY b.created_at DESC
                    LIMIT 1
                )
                WHERE pa.file_type = 'DIR' COLLATE NOCASE
                  AND date(pa.updated_at) BETWEEN @start AND @end
                ORDER BY pa.created_at ASC
                """;
            cmd.Parameters.AddWithValue("@start", startDate.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@end",   endDate.ToString("yyyy-MM-dd"));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new DirLogRow(
                    DirAttachmentId: reader.GetInt32(0),
                    DirFileName:     reader.GetString(1),
                    DirFilePath:     reader.GetString(2),
                    CreatedAt:       reader.GetString(3),
                    UpdatedAt:       reader.GetString(4),
                    DrawingNumber:   reader.GetString(5),
                    Revision:        reader.GetString(6),
                    PoNumber:        reader.GetString(7),
                    JobNumber:       reader.GetString(8),
                    CustomerName:    reader.IsDBNull(9)  ? null : reader.GetString(9),
                    BubAttachmentId: reader.IsDBNull(10) ? null : reader.GetInt32(10),
                    BubFileName:     reader.IsDBNull(11) ? null : reader.GetString(11),
                    BubFilePath:     reader.IsDBNull(12) ? null : reader.GetString(12)));
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"DirLogRepository.GetDirLogRows failed for {startDate}..{endDate}: {ex.Message}");
        }
        return results;
    }
}

/// <param name="DirAttachmentId">part_attachment.id (DIR row)</param>
/// <param name="DirFileName">part_attachment.file_name (DIR)</param>
/// <param name="DirFilePath">part_attachment.file_path (DIR)</param>
/// <param name="CreatedAt">part_attachment.created_at (DIR) - "Start"</param>
/// <param name="UpdatedAt">part_attachment.updated_at (DIR) - "Finish"</param>
/// <param name="DrawingNumber">part.drawing_number</param>
/// <param name="Revision">part.revision</param>
/// <param name="PoNumber">purchase_order.po_number</param>
/// <param name="JobNumber">job.job_number</param>
/// <param name="CustomerName">customer.customer_name; null if no contact/customer linked</param>
/// <param name="BubAttachmentId">Latest matching BUBBLE part_attachment.id for this drawing_number; null if none</param>
/// <param name="BubFileName">Latest matching bubble file_name; null if none</param>
/// <param name="BubFilePath">Latest matching bubble file_path; null if none</param>
public record DirLogRow(
    int DirAttachmentId, string DirFileName, string DirFilePath,
    string CreatedAt, string UpdatedAt,
    string DrawingNumber, string Revision,
    string PoNumber, string JobNumber, string? CustomerName,
    int? BubAttachmentId, string? BubFileName, string? BubFilePath);
