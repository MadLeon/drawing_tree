/// <summary>
/// PartNotesDialog.xaml.cs
/// Dialog for viewing all part notes and adding new ones.
/// Mirrors the notes panel in PartDetailControl.
/// </summary>

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DrawingTree.Data;

using Color            = System.Windows.Media.Color;
using Button            = System.Windows.Controls.Button;
using Path              = System.Windows.Shapes.Path;
using Brushes           = System.Windows.Media.Brushes;
using MessageBox        = System.Windows.MessageBox;
using MessageBoxButton  = System.Windows.MessageBoxButton;
using MessageBoxImage   = System.Windows.MessageBoxImage;
using MessageBoxResult  = System.Windows.MessageBoxResult;

namespace DrawingTree.Dialogs;

public partial class PartNotesDialog : Window
{
    private readonly int             _partId;
    private readonly PartRepository  _repo = new();

    private int? _editingNoteId;

    /// <summary>True when a note was added, edited, or deleted during this session.</summary>
    public bool NotesChanged { get; private set; }

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
            NotesPanel.Children.Add(BuildNoteRow(note));
    }

    private Grid BuildNoteRow(PartNoteRow note)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

        var textStack = new StackPanel();
        textStack.Children.Add(new TextBlock
        {
            Text        = $"[{note.CreatedAt}]  {note.Author ?? "unknown"}",
            FontSize    = 9,
            Foreground  = new SolidColorBrush(Color.FromRgb(130, 130, 130)),
            Margin      = new Thickness(0, 2, 0, 1),
        });
        textStack.Children.Add(new TextBlock
        {
            Text         = note.Content,
            FontSize     = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, 2),
        });
        Grid.SetColumn(textStack, 0);
        grid.Children.Add(textStack);

        var editBtn = new Button
        {
            Style   = (Style)FindResource("IconLinkBtn"),
            Padding = new Thickness(2, 0, 2, 0),
            ToolTip = "Edit",
            Content = new Path
            {
                Data    = (Geometry)Resources["EditDocumentGeo"],
                Stretch = Stretch.Uniform,
                Fill    = Brushes.DimGray,
                Width   = 13,
                Height  = 13,
            },
            VerticalAlignment = VerticalAlignment.Top,
        };
        editBtn.Click += (_, _) => EditNote(note);
        Grid.SetColumn(editBtn, 1);
        grid.Children.Add(editBtn);

        var deleteBtn = new Button
        {
            Style   = (Style)FindResource("IconLinkBtn"),
            Padding = new Thickness(2, 0, 2, 0),
            ToolTip = "Delete",
            Content = new Path
            {
                Data    = (Geometry)Resources["ScanDeleteGeo"],
                Stretch = Stretch.Uniform,
                Fill    = Brushes.IndianRed,
                Width   = 13,
                Height  = 13,
            },
            VerticalAlignment = VerticalAlignment.Top,
        };
        deleteBtn.Click += (_, _) => DeleteNote(note);
        Grid.SetColumn(deleteBtn, 2);
        grid.Children.Add(deleteBtn);

        return grid;
    }

    private void EditNote(PartNoteRow note)
    {
        _editingNoteId               = note.Id;
        NewNoteBox.Text              = note.Content;
        NewNoteBox.Focus();
        AddOrSaveButton.Content      = "Save Edit";
        CancelEditButton.Visibility  = Visibility.Visible;
    }

    private void CancelEdit()
    {
        _editingNoteId              = null;
        NewNoteBox.Text             = string.Empty;
        AddOrSaveButton.Content     = "Add Note";
        CancelEditButton.Visibility = Visibility.Collapsed;
    }

    private void CancelEditButton_Click(object sender, RoutedEventArgs e) => CancelEdit();

    private void DeleteNote(PartNoteRow note)
    {
        var choice = MessageBox.Show(
            $"Delete this memo?\n\n{note.Content}",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (choice != MessageBoxResult.Yes) return;

        _repo.DeletePartNote(note.Id);
        if (_editingNoteId == note.Id) CancelEdit();
        NotesChanged = true;
        LoadNotes();
    }

    private void AddNote_Click(object sender, RoutedEventArgs e)
    {
        var content = NewNoteBox.Text.Trim();
        if (content.Length == 0) return;

        if (_editingNoteId.HasValue)
        {
            _repo.UpdatePartNote(_editingNoteId.Value, content);
            CancelEdit();
        }
        else
        {
            _repo.AddPartNote(_partId, content);
            NewNoteBox.Text = string.Empty;
        }

        NotesChanged = true;
        LoadNotes();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
