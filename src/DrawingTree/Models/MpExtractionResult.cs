/// <summary>
/// MpExtractionResult.cs
/// Data extracted from a Manufacturing Process (.xlsm) workbook: the header info block
/// and the production process steps.
/// </summary>
/// <remarks>
/// Populated by <see cref="DrawingTree.Services.MpExtractorService"/> and shown read-only
/// in the MP associate dialog before being written to the database.
/// </remarks>

namespace DrawingTree.Models;

public class MpExtractionResult
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;

    public string PoNumber { get; set; } = string.Empty;
    public string OeNumber { get; set; } = string.Empty;
    public string JobNumber { get; set; } = string.Empty;
    public string LineNumber { get; set; } = string.Empty;
    public string DrawingNumber { get; set; } = string.Empty;
    public string Revision { get; set; } = string.Empty;
    public string DrawingReleaseDate { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DeliveryRequiredDate { get; set; } = string.Empty;
    public string Quantity { get; set; } = string.Empty;

    public List<MpProcessStep> ProcessSteps { get; } = new();
}

/// <summary>
/// A single production process step read from the MP workbook.
/// </summary>
/// <param name="RowNumber">Step order number (column E)</param>
/// <param name="ShopCode">Shop code such as FI, P, RT, SC, I, H, W, PI (column D)</param>
/// <param name="ProcessDescription">Step description (merged columns F:N)</param>
public record MpProcessStep(int RowNumber, string ShopCode, string ProcessDescription);
