using System.Collections.Generic;
using System.Linq;
using DrawingTree.Models;

namespace DrawingTree.Services;

/// <summary>
/// PoTreeService.cs
/// Provides root assembly groups for a given PO number.
/// </summary>
/// <remarks>
/// Usage:
/// - Dev environment: returns hardcoded mock data
/// - Production: will query purchase_order → job → order_item → part
/// </remarks>
public class PoTreeService
{
    /// <summary>
    /// Returns root assembly groups for the given PO number.
    /// Groups that share the same drawing_number are merged into one entry.
    /// </summary>
    /// <param name="poNumber">The PO number to look up</param>
    /// <returns>List of root groups, empty if PO not found</returns>
    public List<PoTreeGroup> GetGroupsForPo(string poNumber)
    {
        return GetMockGroups(poNumber);
    }

    // ── Mock data (dev) ───────────────────────────────────────────────────

    private static List<PoTreeGroup> GetMockGroups(string poNumber)
    {
        if (poNumber != "RT79-87630-PN-R005")
            return new List<PoTreeGroup>();

        // Raw order_item rows: (job_number, line_number, drawing_number)
        var rawItems = new (string Job, string Line, string Drawing)[]
        {
            ("72395", "1", "RT-87630-71254-1000-1-GA-D"),
        };

        // Group by drawing_number: merge job numbers (distinct) and line numbers
        return rawItems
            .GroupBy(x => x.Drawing)
            .Select(g => new PoTreeGroup
            {
                DrawingNumber = g.Key,
                JobNumbers = g.Select(x => x.Job).Distinct().ToList(),
                LineNumbers = g.Select(x => x.Line).ToList()
            })
            .ToList();
    }
}
