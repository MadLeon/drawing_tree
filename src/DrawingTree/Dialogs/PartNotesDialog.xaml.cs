/// <summary>
/// PartNotesDialog.xaml.cs
/// Dialog for viewing all part notes and adding new ones.
/// Mirrors the notes panel in PartDetailControl.
/// </summary>

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DrawingTree.Data;

using Color = System.Windows.Media.Color;

namespace DrawingTree.Dialogs;

public partial class PartNotesDialog : Window
{
    private readonly int             _partId;
    private readonly PartRepository  _repo = new();

    /// <summary>True when at least one note was added during this session.</summary>
    public bool NoteAdded { get; private set; }

    public PartNotesDialog(int partId, string drawingNumber)
    {
        InitializeComponent();
        Title   = $"Notes — {drawingNumber}";
        _partId = partId;
        LoadNotes();
    }

    private void LoadNotes()
    {
        NotesPanel.Children.Clear();
        var notes = _repo.GetPartNotes(_partId);
        if (notes.Count == 0)
        {
            NotesPanel.Children.Add(new TextBlock
            {
                Text       = "(no notes yet)",
                FontSize   = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                Margin     = new Thickness(0, 2, 0, 0),
            });
            return;
        }

        foreach (var note in notes)
        {
            NotesPanel.Children.Add(new TextBlock
            {
                Text        = $"[{note.CreatedAt}]  {note.Author ?? "unknown"}",
                FontSize    = 9,
                Foreground  = new SolidColorBrush(Color.FromRgb(130, 130, 130)),
                Margin      = new Thickness(0, 6, 0, 1),
            });
            NotesPanel.Children.Add(new TextBlock
            {
                Text        = note.Content,
                FontSize    = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin      = new Thickness(0, 0, 0, 2),
            });
        }
    }

    private void AddNote_Click(object sender, RoutedEventArgs e)
    {
        var content = NewNoteBox.Text.Trim();
        if (content.Length == 0) return;

        _repo.AddPartNote(_partId, content);
        NewNoteBox.Text = string.Empty;
        NoteAdded       = true;
        LoadNotes();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
