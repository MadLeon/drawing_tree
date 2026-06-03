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
