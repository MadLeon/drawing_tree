using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DrawingTree.Models;

/// <summary>
/// SaveStatus.cs / PartEditorRow.cs
/// Per-row view model for the Part Editor screen.
/// Tracks editable fields and save result state.
/// </summary>
public enum SaveStatus { None, Success, Error }

public class PartEditorRow : INotifyPropertyChanged
{
    private int?  _partId;
    private string _revision = string.Empty;
    private string _description = string.Empty;
    private string _previousDrawingNumber = string.Empty;
    private string _quantityInAssembly = string.Empty;
    private bool _isAssembly;
    private string _pdfPath = string.Empty;
    private SaveStatus _status = SaveStatus.None;

    public int   Index               { get; init; }
    public string OriginalDrawingNumber { get; init; } = string.Empty;

    private string _drawingNumber = string.Empty;
    public string DrawingNumber
    {
        get => _drawingNumber;
        set { if (_drawingNumber != value) { _drawingNumber = value; OnPropertyChanged(); } }
    }

    public int? PartId
    {
        get => _partId;
        set { if (_partId != value) { _partId = value; OnPropertyChanged(); } }
    }

    public string Revision
    {
        get => _revision;
        set { if (_revision != value) { _revision = value; OnPropertyChanged(); } }
    }

    public string Description
    {
        get => _description;
        set { if (_description != value) { _description = value; OnPropertyChanged(); } }
    }

    /// <summary>Drawing number this part carried before a rename, as plain historical text (optional)</summary>
    public string PreviousDrawingNumber
    {
        get => _previousDrawingNumber;
        set { if (_previousDrawingNumber != value) { _previousDrawingNumber = value; OnPropertyChanged(); } }
    }

    /// <summary>Quantity this part appears with in its parent assembly (stored in part_tree).</summary>
    public string QuantityInAssembly
    {
        get => _quantityInAssembly;
        set { if (_quantityInAssembly != value) { _quantityInAssembly = value; OnPropertyChanged(); } }
    }

    public bool IsAssembly
    {
        get => _isAssembly;
        set { if (_isAssembly != value) { _isAssembly = value; OnPropertyChanged(); } }
    }

    public string PdfPath
    {
        get => _pdfPath;
        set { if (_pdfPath != value) { _pdfPath = value; OnPropertyChanged(); } }
    }

    public SaveStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSuccess));
                OnPropertyChanged(nameof(IsError));
            }
        }
    }

    public bool IsSuccess => _status == SaveStatus.Success;
    public bool IsError   => _status == SaveStatus.Error;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
