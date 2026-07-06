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

    /// <summary>
    /// Sets the bubble drawing folder for the given customer, adding the key if it doesn't exist yet.
    /// </summary>
    public static void SetBubbleFolder(string customerName, string folderPath)
    {
        var lines = File.Exists(ConfigFilePath)
            ? File.ReadAllLines(ConfigFilePath).ToList()
            : new List<string>();

        EnsureSectionExists(lines);

        int headerIdx = lines.FindIndex(
            l => l.Trim().Equals(SectionHeader, StringComparison.OrdinalIgnoreCase));

        int keyIdx = -1;
        int sectionEnd = lines.Count;
        for (int i = headerIdx + 1; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith('[')) { sectionEnd = i; break; }
            var eq = t.IndexOf('=');
            if (eq > 0 && t[..eq].Trim().Equals(customerName, StringComparison.OrdinalIgnoreCase))
            {
                keyIdx = i;
                break;
            }
        }

        if (keyIdx >= 0)
            lines[keyIdx] = $"{customerName}={folderPath}";
        else
            lines.Insert(sectionEnd, $"{customerName}={folderPath}");

        File.WriteAllLines(ConfigFilePath, lines);
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

    private static void EnsureSectionExists(List<string> lines)
    {
        if (lines.Any(l => l.Trim().Equals(SectionHeader, StringComparison.OrdinalIgnoreCase)))
            return;
        if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
            lines.Add(string.Empty);
        lines.Add("# Bubble Drawing Network Paths");
        lines.Add("# Map each customer name to its bubble drawing network folder");
        lines.Add(SectionHeader);
    }
}
