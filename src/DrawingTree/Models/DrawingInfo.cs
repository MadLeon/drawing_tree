using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace DrawingTree.Models;

/// <summary>
/// Drawing information model containing drawing number and PDF file path
/// Implements INotifyPropertyChanged for WPF data binding support
/// </summary>
public class DrawingInfo : INotifyPropertyChanged
{
    private string _drawingNumber = string.Empty;
    private string _pdfPath = string.Empty;
    private bool _hasDuplicate = false;
    private string _revision = string.Empty;
    private string _description = string.Empty;
    private string _quantityInAssembly = string.Empty;
    private bool _isAssembly = false;
    private bool _isDragging = false;
    private bool _isSelected = false;

    /// <summary>Database part.id; null for drawings not yet persisted</summary>
    public int? PartId { get; set; }

    /// <summary>
    /// Drawing number extracted from PDF filename
    /// </summary>
    public string DrawingNumber
    {
        get => _drawingNumber;
        set
        {
            if (_drawingNumber != value)
            {
                _drawingNumber = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Full path to the PDF file
    /// </summary>
    public string PdfPath
    {
        get => _pdfPath;
        set
        {
            if (_pdfPath != value)
            {
                _pdfPath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FileName));
            }
        }
    }

    /// <summary>
    /// File name extracted from PdfPath
    /// </summary>
    public string FileName
    {
        get => string.IsNullOrEmpty(_pdfPath) ? string.Empty : Path.GetFileName(_pdfPath);
    }

    /// <summary>
    /// Revision identifier (e.g. "A", "B", "1")
    /// </summary>
    public string Revision
    {
        get => _revision;
        set { if (_revision != value) { _revision = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Drawing description or title
    /// </summary>
    public string Description
    {
        get => _description;
        set { if (_description != value) { _description = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Number of this item required in the parent assembly
    /// </summary>
    public string QuantityInAssembly
    {
        get => _quantityInAssembly;
        set { if (_quantityInAssembly != value) { _quantityInAssembly = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Whether this drawing represents an assembly (composite part)
    /// </summary>
    public bool IsAssembly
    {
        get => _isAssembly;
        set { if (_isAssembly != value) { _isAssembly = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// True while this item is being dragged (UI state for placeholder display)
    /// </summary>
    public bool IsDragging
    {
        get => _isDragging;
        set { if (_isDragging != value) { _isDragging = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// True when this item is selected in the left panel or tree view
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Indicates if this drawing has duplicate drawing number or path
    /// </summary>
    public bool HasDuplicate
    {
        get => _hasDuplicate;
        set
        {
            if (_hasDuplicate != value)
            {
                _hasDuplicate = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BorderBrush));
            }
        }
    }

    /// <summary>
    /// Border brush color based on duplicate status
    /// </summary>
    public System.Windows.Media.Brush BorderBrush
    {
        get => _hasDuplicate ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.Transparent;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
