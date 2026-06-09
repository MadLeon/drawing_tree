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
    private bool _isLastChild = false;
    private bool _isRootNode = false;
    private string? _jobHeader;
    private string? _lineHeader;

    /// <summary>Database part_tree.id for this node's parent→child edge; null for new/unsaved nodes</summary>
    public int? PartTreeId { get; set; }

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
    /// Whether this node has any children
    /// </summary>
    public bool HasChildren => Children.Count > 0;

    /// <summary>
    /// True when this node is the last sibling in its parent's children list (controls connector line rendering)
    /// </summary>
    public bool IsLastChild
    {
        get => _isLastChild;
        set
        {
            if (_isLastChild != value)
            {
                _isLastChild = value;
                OnPropertyChanged();
            }
        }
    }

    public DrawingNode(DrawingInfo drawing)
    {
        Drawing = drawing;
        Children.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasChildren));
            UpdateLastChildFlags(Children);
        };
    }

    /// <summary>
    /// True when this node is a direct root item (no parent node)
    /// </summary>
    public bool IsRootNode
    {
        get => _isRootNode;
        set
        {
            if (_isRootNode != value)
            {
                _isRootNode = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Header text shown above the node border for root assembly nodes: "Job Number: 72395 &amp; 72396"
    /// </summary>
    public string? JobHeader
    {
        get => _jobHeader;
        set
        {
            if (_jobHeader != value)
            {
                _jobHeader = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasJobInfo));
            }
        }
    }

    /// <summary>
    /// Header text shown below JobHeader for root assembly nodes: "Line Number: 1 &amp; 2"
    /// </summary>
    public string? LineHeader
    {
        get => _lineHeader;
        set
        {
            if (_lineHeader != value)
            {
                _lineHeader = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// True when this node was pre-assigned from a PO order item (has job/line context).
    /// Root assembly nodes with HasJobInfo=True cannot be removed or dragged by the user.
    /// </summary>
    public bool HasJobInfo => !string.IsNullOrEmpty(_jobHeader);

    /// <summary>
    /// Sets IsLastChild on each node in the collection based on its index.
    /// </summary>
    public static void UpdateLastChildFlags(System.Collections.Generic.IList<DrawingNode> nodes)
    {
        for (int i = 0; i < nodes.Count; i++)
            nodes[i].IsLastChild = (i == nodes.Count - 1);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
