/// <summary>
/// DescriptionFormatter.cs
/// Shared formatting helpers for part descriptions.
/// Descriptions spell out the whole assembly chain joined by dashes, so most
/// outputs only want the trailing segment naming the part itself.
/// </summary>

namespace DrawingTree.Services;

public static class DescriptionFormatter
{
    /// <summary>
    /// Returns the part of the description after the last dash, or the description unchanged
    /// when it contains no dash.
    /// ("Hook Assembly of Extension Tool - Hook Weldment - Hook" -> "Hook").
    /// </summary>
    /// <param name="description">Full description, possibly an assembly chain joined by dashes</param>
    /// <returns>Trimmed last segment of the description</returns>
    public static string LastSegment(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return string.Empty;

        var dash = description.LastIndexOf('-');
        return dash < 0 ? description.Trim() : description[(dash + 1)..].Trim();
    }
}
