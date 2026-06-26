/// <summary>
/// MpConfig.cs
/// Manages the [CustomerFolderMappings] section of config.txt.
/// Maps customer names to their folder names under \\rtdnas2\Manufacturing Process.
/// </summary>

using System.IO;

namespace DrawingTree.Services;

/// <summary>
/// Thrown when a customer has no configured MP folder mapping in config.txt.
/// </summary>
public class MpFolderNotConfiguredException : Exception
{
    public string CustomerName         { get; }
    public string ConfigFilePath       { get; }
    public List<string> Unconfigured   { get; }

    public MpFolderNotConfiguredException(string customerName, string configFilePath, List<string> unconfigured)
        : base($"No MP folder configured for customer '{customerName}'.")
    {
        CustomerName   = customerName;
        ConfigFilePath = configFilePath;
        Unconfigured   = unconfigured;
    }
}

/// <summary>
/// Reads and writes the [CustomerFolderMappings] section of config.txt.
/// Does not touch any other section of the file.
/// </summary>
public static class MpConfig
{
    private const string SectionHeader = "[CustomerFolderMappings]";

    public static readonly string ConfigFilePath =
        Path.Combine(AppContext.BaseDirectory, "config.txt");

    /// <summary>
    /// Returns the configured folder name for the given customer, or null if absent or empty.
    /// </summary>
    public static string? GetFolderName(string customerName)
    {
        if (!File.Exists(ConfigFilePath)) return null;
        var map = ReadSectionKeys(File.ReadAllLines(ConfigFilePath));
        return map.TryGetValue(customerName, out var val) && !string.IsNullOrWhiteSpace(val)
            ? val : null;
    }

    /// <summary>
    /// Ensures every customer in <paramref name="allCustomerNames"/> has a key in the section.
    /// Adds missing keys with an empty value so the user can fill them in.
    /// Returns the names whose folder value is still empty (i.e. needs to be configured).
    /// </summary>
    public static List<string> SyncCustomerKeys(IEnumerable<string> allCustomerNames)
    {
        var customers = allCustomerNames.ToList();
        var lines = File.Exists(ConfigFilePath)
            ? File.ReadAllLines(ConfigFilePath).ToList()
            : new List<string>();

        var existing = ReadSectionKeys(lines);
        var missing  = customers.Where(c => !existing.ContainsKey(c)).ToList();

        if (missing.Count > 0)
        {
            EnsureSectionExists(lines);
            InsertKeysIntoSection(lines, missing);
            File.WriteAllLines(ConfigFilePath, lines);
            existing = ReadSectionKeys(lines);
        }

        return customers
            .Where(c => !existing.TryGetValue(c, out var v) || string.IsNullOrWhiteSpace(v))
            .ToList();
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
        lines.Add("# MP Customer Folder Mappings");
        lines.Add("# Map each customer name to its folder under \\\\rtdnas2\\Manufacturing Process");
        lines.Add(SectionHeader);
    }

    private static void InsertKeysIntoSection(List<string> lines, List<string> keys)
    {
        int headerIdx = lines.FindIndex(
            l => l.Trim().Equals(SectionHeader, StringComparison.OrdinalIgnoreCase));
        if (headerIdx < 0) return;

        int insertAt = lines.Count;
        for (int i = headerIdx + 1; i < lines.Count; i++)
        {
            if (lines[i].Trim().StartsWith('[')) { insertAt = i; break; }
        }

        lines.InsertRange(insertAt, keys.Select(k => $"{k}="));
    }
}
