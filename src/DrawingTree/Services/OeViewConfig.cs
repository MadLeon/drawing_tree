/// <summary>
/// OeViewConfig.cs
/// Reads and writes the top-level "LandingPage" key in config.txt (outside any [Section]).
/// Controls whether the OE list starts in "simple view" or "oe view".
/// </summary>

using System.IO;

namespace DrawingTree.Services;

/// <summary>
/// Reads and writes the top-level LandingPage key of config.txt.
/// Does not touch any [Section] content.
/// </summary>
public static class OeViewConfig
{
    private const string Key = "LandingPage";
    private const string DefaultValue = "simple view";

    public static readonly string ConfigFilePath =
        Path.Combine(AppContext.BaseDirectory, "config.txt");

    /// <summary>Returns the configured landing page ("oe view" or "simple view"), defaulting to "simple view".</summary>
    public static string GetLandingPage()
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

    /// <summary>Sets the landing page value, adding the key at the top of the file if it doesn't exist yet.</summary>
    public static void SetLandingPage(string value)
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
