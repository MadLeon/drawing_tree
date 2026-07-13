namespace DrawingTree.Models;

/// <summary>
/// One data row extracted from the OE Excel file's DELIVERY SCHEDULE sheet.
/// Column AA (Order Item ID write-back) is intentionally not represented here — it is
/// a deprecated artifact of the old VBA upload process and must not be used as a data source.
/// </summary>
public class OeExcelRow
{
    public string OeNumber { get; set; } = "";
    public string JobNumber { get; set; } = "";
    public string Customer { get; set; } = "";
    public string Quantity { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public string Revision { get; set; } = "";
    public string Contact { get; set; } = "";
    public string DrawingReleaseDate { get; set; } = "";
    public string LineNumber { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal? Price { get; set; }
    public string PoNumber { get; set; } = "";
    public string DeliveryRequiredDate { get; set; } = "";

    /// <summary>1-based row index in the source sheet, used for error reporting.</summary>
    public int SourceRowIndex { get; set; }

    /// <summary>Non-fatal parse issues on this row (e.g. unparseable quantity/date). Never causes a silent skip.</summary>
    public List<string> ParseWarnings { get; } = new();

    public override string ToString()
        => $"Job #{JobNumber} / M{LineNumber} / {PartNumber} / Qty {Quantity} / PO {PoNumber}";
}
