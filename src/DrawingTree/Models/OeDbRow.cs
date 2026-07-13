namespace DrawingTree.Models;

/// <summary>
/// A snapshot of one active order_item joined with its owning job/purchase_order/part/customer,
/// used as the comparison target ("set B") for OE sync diffing.
/// </summary>
public class OeDbRow
{
    public long OrderItemId { get; set; }
    public long JobId { get; set; }
    public string JobNumber { get; set; } = "";
    public long PoId { get; set; }
    public string PoNumber { get; set; } = "";
    public string? OeNumber { get; set; }
    public string CustomerName { get; set; } = "";
    public string ContactName { get; set; } = "";

    public long? PartId { get; set; }
    public string DrawingNumber { get; set; } = "";
    public string Revision { get; set; } = "";
    public string? Description { get; set; }

    // Stored as raw strings, not int: order_item.line_number/quantity have INTEGER column
    // affinity but SQLite still allows non-numeric TEXT values when the value can't be
    // losslessly converted (e.g. "1&2", "3~7") — reading these via SqliteDataReader.GetInt32
    // silently truncates them (e.g. "1&2" -> 1), which broke job+line matching for such rows.
    public string LineNumber { get; set; } = "";
    public string Quantity { get; set; } = "";
    public decimal? ActualPrice { get; set; }
    public string? DrawingReleaseDate { get; set; }
    public string? DeliveryRequiredDate { get; set; }

    public override string ToString()
        => $"Job #{JobNumber} / M{LineNumber} / {DrawingNumber} / Qty {Quantity} / PO {PoNumber}";
}
