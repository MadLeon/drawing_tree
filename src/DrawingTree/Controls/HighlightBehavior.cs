/// <summary>
/// HighlightBehavior.cs
/// Attached properties that rebuild a TextBlock's Inlines to highlight
/// matching substrings (case-insensitive, yellow background) based on a search term.
/// </summary>

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace DrawingTree.Controls;

public static class HighlightBehavior
{
    // ── Attached properties ───────────────────────────────────────────────────

    public static readonly DependencyProperty FullTextProperty =
        DependencyProperty.RegisterAttached("FullText", typeof(string),
            typeof(HighlightBehavior), new PropertyMetadata(string.Empty, OnChanged));

    public static readonly DependencyProperty SearchTermProperty =
        DependencyProperty.RegisterAttached("SearchTerm", typeof(string),
            typeof(HighlightBehavior), new PropertyMetadata(string.Empty, OnChanged));

    public static string GetFullText(DependencyObject obj) => (string)obj.GetValue(FullTextProperty);
    public static void SetFullText(DependencyObject obj, string value) => obj.SetValue(FullTextProperty, value);

    public static string GetSearchTerm(DependencyObject obj) => (string)obj.GetValue(SearchTermProperty);
    public static void SetSearchTerm(DependencyObject obj, string value) => obj.SetValue(SearchTermProperty, value);

    // ── Internal ──────────────────────────────────────────────────────────────

    private static readonly Brush HighlightBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0x3B));

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock tb) UpdateInlines(tb);
    }

    private static void UpdateInlines(TextBlock tb)
    {
        tb.Inlines.Clear();

        var text = GetFullText(tb) ?? string.Empty;
        var term = GetSearchTerm(tb) ?? string.Empty;

        if (string.IsNullOrEmpty(term) || string.IsNullOrEmpty(text))
        {
            if (!string.IsNullOrEmpty(text))
                tb.Inlines.Add(new Run(text));
            return;
        }

        int i = 0;
        while (i < text.Length)
        {
            int idx = text.IndexOf(term, i, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                tb.Inlines.Add(new Run(text[i..]));
                break;
            }
            if (idx > i)
                tb.Inlines.Add(new Run(text[i..idx]));

            tb.Inlines.Add(new Run(text[idx..(idx + term.Length)])
            {
                Background = HighlightBrush,
                FontWeight = FontWeights.SemiBold
            });

            i = idx + term.Length;
        }
    }
}
