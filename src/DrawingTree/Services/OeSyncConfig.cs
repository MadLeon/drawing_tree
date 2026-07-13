/// <summary>
/// OeSyncConfig.cs
/// Reads and writes the top-level "OeExcelPath" key in config.txt (outside any [Section]).
/// Controls which OE Excel workbook the OE sync feature parses.
/// </summary>

using System.IO;

namespace DrawingTree.Services;

/// <summary>
/// Reads and writes the top-level OeExcelPath key of config.txt.
/// Does not touch any [Section] content.
/// </summary>
public static class OeSyncConfig
{
    private const string Key = "OeExcelPath";
    private const string DefaultValue = @"\\rtdnas2\OE\Order Entry Log.xlsm";

    public static readonly string ConfigFilePath =
        Path.Combine(AppContext.BaseDirectory, "config.txt");

    /// <summary>Returns the configured OE Excel workbook path, defaulting to the production UNC path.</summary>
    public static string GetOeExcelPath()
    {
        if (!File.Exists(ConfigFilePath)) return DefaultValue;

        foreach (var line in File.ReadAllLines(ConfigFilePath))
        {
            var t = line.Trim();
            if (t.StartsWith('[')) break;
            var eq = t.IndexOf('=');
            if (eq > 0 && t[..eq].Trim().Equals(Key, StringComparison.OrdinalIgnoreCase))
                return t[(eq + 1)..].Trim();
        }
        return DefaultValue;
    }

    /// <summary>Sets the OE Excel workbook path, adding the key at the top of the file if it doesn't exist yet.</summary>
    public static void SetOeExcelPath(string value)
    {
        var lines = File.Exists(ConfigFilePath)
            ? File.ReadAllLines(ConfigFilePath).ToList()
            : new List<string>();

        int keyIdx = -1;
        int topLevelEnd = lines.Count;
        for (int i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith('['))
            {
                topLevelEnd = i;
                break;
            }
            var eq = t.IndexOf('=');
            if (eq > 0 && t[..eq].Trim().Equals(Key, StringComparison.OrdinalIgnoreCase))
            {
                keyIdx = i;
                break;
            }
        }

        if (keyIdx >= 0)
            lines[keyIdx] = $"{Key}={value}";
        else
            lines.Insert(topLevelEnd, $"{Key}={value}");

        File.WriteAllLines(ConfigFilePath, lines);
    }
}
