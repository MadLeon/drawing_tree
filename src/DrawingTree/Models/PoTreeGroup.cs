using System.Collections.Generic;

namespace DrawingTree.Models;

/// <summary>
/// PoTreeGroup.cs
/// One root assembly entry derived from a PO's order items.
/// Multiple jobs/lines sharing the same drawing_number are merged into a single group.
/// </summary>
public class PoTreeGroup
{
    /// <summary>Job numbers that reference this assembly drawing</summary>
    public List<string> JobNumbers { get; init; } = new();

    /// <summary>Order item line numbers associated with this assembly drawing</summary>
    public List<string> LineNumbers { get; init; } = new();

    /// <summary>The part drawing number that serves as the root assembly</summary>
    public string DrawingNumber { get; init; } = string.Empty;

    /// <summary>Database part.id for this root assembly</summary>
    public int PartId { get; init; }
}
