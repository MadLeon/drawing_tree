/// <summary>
/// BubbleConfig.cs
/// Reads the [BubbleDrawingPaths] section of config.txt.
/// Maps customer names to their bubble drawing network folder.
/// </summary>

using System.IO;

namespace DrawingTree.Services;

/// <summary>
/// Read-only accessor for the [BubbleDrawingPaths] section of config.txt.
/// Does not touch any other section of the file.
/// </summary>
public static class BubbleConfig
{
    private const string SectionHeader = "[BubbleDrawingPaths]";

    public static readonly string ConfigFilePath =
        Path.Combine(AppContext.BaseDirectory, "config.txt");

    /// <summary>
    /// Returns the configured bubble drawing folder for the given customer, or null if absent or empty.
    /// </summary>
    public static string? GetBubbleFolder(string customerName)
    {
        if (string.IsNullOrWhiteSpace(customerName) || !File.Exists(ConfigFilePath)) return null;
        var map = ReadSectionKeys(File.ReadAllLines(ConfigFilePath));
        return map.TryGetValue(customerName, out var val) && !string.IsNullOrWhiteSpace(val)
            ? val : null;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static Dictionary<string, string> ReadSectionKeys(IEnumerable<string> lines)
    {
        var result    = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool inSection = false;

        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.Equals(SectionHeader, StringComparison.OrdinalIgnoreCase)) { inSection = true; continue; }
            if (!inSection) continue;
            if (t.StartsWith('[')) break;
            if (t.StartsWith('#') || t.StartsWith("//") || t.Length == 0) continue;

            var eq = t.IndexOf('=');
            if (eq <= 0) continue;
            result[t[..eq].Trim()] = t[(eq + 1)..].Trim();
        }
        return result;
    }
}
