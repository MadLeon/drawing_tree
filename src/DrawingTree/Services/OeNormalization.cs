/// <summary>
/// OeNormalization.cs
/// Pure string-normalization helpers shared by the OE Excel parser and the OE sync diff engine.
/// No file/database access — kept independently testable.
/// </summary>

using System.Globalization;
using System.Text.RegularExpressions;

namespace DrawingTree.Services;

public static class OeNormalization
{
    // Trailing PO revision suffix: "-R.1", "-R01", "-R1", "-R.15" — exactly 1-2 raw digit
    // characters after "-R"/"-R.". A genuine drawing-style "-R002"/"-R978" tail (3+ raw digits)
    // is a real part of the base order identifier and must NOT match here.
    private static readonly Regex PoSuffixPattern = new(@"-R\.?(\d{1,2})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // "1 set", "2 Sets" -> the leading count.
    private static readonly Regex QuantitySetPattern = new(@"^(\d+)\s*sets?\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Strips a trailing customer-add-on revision suffix (e.g. "-R.1", "-R01") from a PO number,
    /// returning the base order identifier. Used only to tolerate suffix differences when comparing
    /// purchase_order.po_number — it must NOT be used to merge or rename purchase_order records.
    /// </summary>
    public static string GetPoBase(string? poNumber)
    {
        if (string.IsNullOrWhiteSpace(poNumber)) return "";
        var normalized = poNumber.Trim().ToUpperInvariant().Replace(" ", "").Replace("REV.", "R.");
        return PoSuffixPattern.Replace(normalized, "");
    }

    /// <summary>
    /// True when two PO numbers refer to the same base order, ignoring a trailing revision suffix
    /// and formatting noise (spaces, case).
    /// </summary>
    public static bool ArePoNumbersEquivalent(string? a, string? b)
        => GetPoBase(a) == GetPoBase(b);

    /// <summary>Trims/uppercases a job number for use as part of the order_item matching key.</summary>
    public static string NormalizeJobNumber(string? jobNumber)
        => string.IsNullOrWhiteSpace(jobNumber) ? "" : jobNumber.Trim().ToUpperInvariant();

    /// <summary>
    /// Canonicalizes a line-number ("M" column) value for comparison: trims, drops all internal
    /// whitespace, drops a trailing ".0", and strips leading zeros, so "01", "1", and "1.0" all
    /// compare equal, and "3+5" / "3 + 5" compare equal. Non-numeric/compound values (e.g. "1A",
    /// "3+5") are kept as literal text — a single Excel row can legitimately cover more than one
    /// item number, and that must be recorded as-is, not collapsed or computed.
    /// </summary>
    public static string NormalizeLineNumber(string? lineNumber)
    {
        if (string.IsNullOrWhiteSpace(lineNumber)) return "";
        var t = Regex.Replace(lineNumber.Trim(), @"\s+", "");
        if (t.EndsWith(".0")) t = t[..^2];
        if (int.TryParse(t, out var n)) return n.ToString();
        return t.ToUpperInvariant();
    }

    /// <summary>
    /// Resolves a Qty.: cell value for storage. order_item.quantity has INTEGER column affinity
    /// but SQLite does not require a clean number — legacy rows already hold literal text there.
    /// - A plain integer, or "N set"/"N sets", resolves to N (IsNumeric = true).
    /// - A sum expression like "3+3" / "3 + 3" is computed to its total (IsNumeric = true) —
    ///   left as literal text it would otherwise be silently mis-read as just "3" by any caller
    ///   that reads the column as an integer (see SqliteDataReader.GetInt32 on TEXT-affinity data).
    /// - Anything else (e.g. "Lot") is kept as literal text (IsNumeric = false); downstream
    ///   quantity totals for such rows should be treated as 0/N-A, not computed.
    /// </summary>
    public static (string StoredValue, bool IsNumeric) ResolveQuantity(string? raw)
    {
        var t = (raw ?? "").Trim();
        if (t.Length == 0) return ("", false);

        if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var direct))
            return (direct.ToString(CultureInfo.InvariantCulture), true);

        var setMatch = QuantitySetPattern.Match(t);
        if (setMatch.Success)
            return (setMatch.Groups[1].Value, true);

        var parts = t.Split('+');
        if (parts.Length >= 2 && parts.All(p => int.TryParse(p.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
        {
            int sum = parts.Sum(p => int.Parse(p.Trim(), CultureInfo.InvariantCulture));
            return (sum.ToString(CultureInfo.InvariantCulture), true);
        }

        return (t, false);
    }

    /// <summary>Trims and collapses a plain text field (customer/contact/description) for comparison.</summary>
    public static string NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
}
