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
    private bool _isAssembly;
    private string _pdfPath = string.Empty;
    private SaveStatus _status = SaveStatus.None;

    public int   Index               { get; init; }
    public string OriginalDrawingNumber { get; init; } = string.Empty;

    private string _drawingNumber = string.Empty;
    public string DrawingNumber
    {
        get => _drawingNumber;
        set { if (_drawingNumber != value) { _drawingNumber = value; OnPropertyChanged(); ClearStatus(); } }
    }

    public int? PartId
    {
        get => _partId;
        set { if (_partId != value) { _partId = value; OnPropertyChanged(); } }
    }

    public string Revision
    {
        get => _revision;
        set { if (_revision != value) { _revision = value; OnPropertyChanged(); ClearStatus(); } }
    }

    public string Description
    {
        get => _description;
        set { if (_description != value) { _description = value; OnPropertyChanged(); ClearStatus(); } }
    }

    /// <summary>Drawing number this part carried before a rename, as plain historical text (optional)</summary>
    public string PreviousDrawingNumber
    {
        get => _previousDrawingNumber;
        set { if (_previousDrawingNumber != value) { _previousDrawingNumber = value; OnPropertyChanged(); ClearStatus(); } }
    }

    public bool IsAssembly
    {
        get => _isAssembly;
        set { if (_isAssembly != value) { _isAssembly = value; OnPropertyChanged(); ClearStatus(); } }
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

    /// <summary>
    /// Drops the save result icon as soon as an editable field changes, so the icon
    /// always reflects the values currently shown in the row rather than an older save.
    /// </summary>
    private void ClearStatus() => Status = SaveStatus.None;

    public bool IsSuccess => _status == SaveStatus.Success;
    public bool IsError   => _status == SaveStatus.Error;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
