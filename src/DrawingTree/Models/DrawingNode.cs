using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DrawingTree.Models;

/// <summary>
/// DrawingNode.cs
/// Tree node model for building drawing relationship hierarchy.
/// </summary>
/// <remarks>
/// Usage:
/// - Wraps a DrawingInfo and holds child nodes for tree structure
/// - Supports expand/collapse state for UI binding
/// </remarks>
public class DrawingNode : INotifyPropertyChanged
{
    private bool _isExpanded = true;
    private bool _isDropTarget = false;
    private bool _isDragging = false;
    private bool _isSelected = false;

    /// <summary>
    /// The drawing data this node represents
    /// </summary>
    public DrawingInfo Drawing { get; }

    /// <summary>
    /// Child nodes under this node in the hierarchy
    /// </summary>
    public ObservableCollection<DrawingNode> Children { get; } = new();

    /// <summary>
    /// Whether child nodes are visible
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Whether this node is currently highlighted as a drag-drop target
    /// </summary>
    public bool IsDropTarget
    {
        get => _isDropTarget;
        set
        {
            if (_isDropTarget != value)
            {
                _isDropTarget = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// True when this node is selected (shows info panel highlight)
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// True while this node is being dragged (UI state for placeholder display)
    /// </summary>
    public bool IsDragging
    {
        get => _isDragging;
        set
        {
            if (_isDragging != value)
            {
                _isDragging = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Whether this node has any children (controls +/- button visibility)
    /// </summary>
    public bool HasChildren => Children.Count > 0;

    public DrawingNode(DrawingInfo drawing)
    {
        Drawing = drawing;
        Children.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasChildren));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
