using System;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Utilities;
using Avalonia.VisualTree;

namespace Avalonia.Controls.Primitives
{
    public class TreeDataGridRowsPresenter : TreeDataGridPresenterBase<IRow>, IChildIndexProvider
    {
        public static readonly DirectProperty<TreeDataGridRowsPresenter, IColumns?> ColumnsProperty =
            AvaloniaProperty.RegisterDirect<TreeDataGridRowsPresenter, IColumns?>(
                nameof(Columns),
                o => o.Columns,
                (o, v) => o.Columns = v);

        public static readonly StyledProperty<bool> CanUserResizeColumnsInRowsProperty =
            AvaloniaProperty.Register<TreeDataGridRowsPresenter, bool>(nameof(CanUserResizeColumnsInRows), true);

        private const double ResizeHitTestWidth = 5.0;
        private IColumns? _columns;
        private int _resizingColumnIndex = -1;
        private double _resizeStartX;
        private double _resizeStartWidth;
        private Cursor? _previousCursor;

        public event EventHandler<ChildIndexChangedEventArgs>? ChildIndexChanged;

        public IColumns? Columns
        {
            get => _columns;
            set => SetAndRaise(ColumnsProperty, ref _columns, value);
        }

        /// <summary>
        /// Gets or sets whether users can resize columns by dragging the column borders in the rows area.
        /// </summary>
        public bool CanUserResizeColumnsInRows
        {
            get => GetValue(CanUserResizeColumnsInRowsProperty);
            set => SetValue(CanUserResizeColumnsInRowsProperty, value);
        }

        protected override Orientation Orientation => Orientation.Vertical;

        protected override (int index, double position) GetElementAt(double position)
        {
            return ((IRows)Items!).GetRowAt(position);
        }

        protected override void RealizeElement(Control element, IRow rowModel, int index)
        {
            var row = (TreeDataGridRow)element;
            row.Realize(ElementFactory, GetSelection(), Columns, (IRows?)Items, index);
            ChildIndexChanged?.Invoke(this, new ChildIndexChangedEventArgs(element, index));
        }

        protected override void UpdateElementIndex(Control element, int oldIndex, int newIndex)
        {
            ((TreeDataGridRow)element).UpdateIndex(newIndex);
            ChildIndexChanged?.Invoke(this, new ChildIndexChangedEventArgs(element, newIndex));
        }

        protected override void UnrealizeElement(Control element)
        {
            ((TreeDataGridRow)element).Unrealize();
            ChildIndexChanged?.Invoke(this, new ChildIndexChangedEventArgs(element, ((TreeDataGridRow)element).RowIndex));
        }

        protected override void UnrealizeElementOnItemRemoved(Control element)
        {
            ((TreeDataGridRow)element).UnrealizeOnItemRemoved();
            ChildIndexChanged?.Invoke(this, new ChildIndexChangedEventArgs(element, ((TreeDataGridRow)element).RowIndex));
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var result = base.MeasureOverride(availableSize);

            // If we have no rows, then get the width from the columns.
            if (Columns is not null && (Items is null || Items.Count == 0))
                result = result.WithWidth(Columns.GetEstimatedWidth(availableSize.Width));

            return result;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Columns?.CommitActualWidths();
            return base.ArrangeOverride(finalSize);
        }

        protected override void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
        {
            base.OnEffectiveViewportChanged(sender, e);
            Columns?.ViewportChanged(Viewport);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            if (change.Property == ColumnsProperty)
            {
                var oldValue = change.GetOldValue<IColumns>();
                var newValue = change.GetNewValue<IColumns>();

                if (oldValue is object)
                    oldValue.LayoutInvalidated -= OnColumnLayoutInvalidated;
                if (newValue is object)
                    newValue.LayoutInvalidated += OnColumnLayoutInvalidated;

                // When for existing Presenter Columns would be recreated they won't get Viewport set so we need to track that
                // and pass Viewport for a newly created object. 
                if (oldValue != null && newValue != null)
                {
                    newValue.ViewportChanged(Viewport);
                }
            }

            base.OnPropertyChanged(change);
        }

        internal void UpdateSelection(ITreeDataGridSelectionInteraction? selection)
        {
            foreach (var element in RealizedElements)
            {
                if (element is TreeDataGridRow { RowIndex: >= 0 } row)
                    row.UpdateSelection(selection);
            }
        }

        /// <summary>
        /// Scrolls to ensure the row at the specified index is visible.
        /// If the row is already visible, does nothing.
        /// If the row is outside the viewport, scrolls to position it at the bottom of the visible area.
        /// </summary>
        /// <param name="rowIndex">The index of the row to scroll into view.</param>
        /// <returns>True if scrolling occurred, false if row was already visible or index invalid.</returns>
        public bool ScrollRowIntoViewAtBottom(int rowIndex)
        {
            if (Items is null || rowIndex < 0 || rowIndex >= Items.Count)
                return false;

            var scrollViewer = this.FindAncestorOfType<ScrollViewer>();
            if (scrollViewer is null)
                return false;

            var viewportHeight = scrollViewer.Viewport.Height;
            var scrollContentPresenter = scrollViewer.FindDescendantOfType<ScrollContentPresenter>();

            // Check if row is already realized and visible
            if (TryGetElement(rowIndex) is Control element && scrollContentPresenter != null)
            {
                var transform = element.TransformToVisual(scrollContentPresenter);
                if (transform.HasValue)
                {
                    var rowBounds = new Rect(element.Bounds.Size).TransformToAABB(transform.Value);

                    // If row is fully visible within the viewport, do nothing
                    if (rowBounds.Top >= 0 && rowBounds.Bottom <= viewportHeight)
                        return false;
                }
            }

            // Row not visible or not realized - need to scroll
            // First, realize the element to get its size
            var scrollToElement = BringIntoView(rowIndex);
            if (scrollToElement is null)
                return false;

            // Get the element's position within the scrollable content (not relative to viewport)
            // The element's Bounds.Y gives its position within the parent (the presenter)
            var rowTop = scrollToElement.Bounds.Top;
            var rowHeight = scrollToElement.Bounds.Height;

            // To position row at the bottom of viewport:
            // We want: rowTop + rowHeight = scrollOffset + viewportHeight
            // Therefore: scrollOffset = rowTop + rowHeight - viewportHeight
            var targetScrollY = rowTop + rowHeight - viewportHeight;

            // Clamp to valid range
            targetScrollY = Math.Max(0, Math.Min(targetScrollY, scrollViewer.Extent.Height - viewportHeight));

            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, targetScrollY);
            return true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

            if (!CanUserResizeColumnsInRows || Columns is null)
                return;

            if (_resizingColumnIndex >= 0)
            {
                // Currently resizing - update column width
                var currentX = e.GetPosition(this).X;
                var delta = currentX - _resizeStartX;
                var newWidth = Math.Max(0, _resizeStartWidth + delta);

                if (!double.IsNaN(newWidth) && !double.IsInfinity(newWidth))
                {
                    var width = new GridLength(newWidth, GridUnitType.Pixel);
                    Columns.SetColumnWidth(_resizingColumnIndex, width);
                }
                e.Handled = true;
            }
            else
            {
                // Check if we're near a column border
                var columnIndex = GetColumnBorderAtPosition(e.GetPosition(this).X);
                if (columnIndex >= 0 && CanResizeColumn(columnIndex))
                {
                    if (_previousCursor is null)
                    {
                        _previousCursor = Cursor;
                        Cursor = new Cursor(StandardCursorType.SizeWestEast);
                    }
                }
                else
                {
                    RestoreCursor();
                }
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            if (!CanUserResizeColumnsInRows || Columns is null)
                return;

            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                var x = e.GetPosition(this).X;
                var columnIndex = GetColumnBorderAtPosition(x);

                if (columnIndex >= 0 && CanResizeColumn(columnIndex))
                {
                    _resizingColumnIndex = columnIndex;
                    _resizeStartX = x;
                    _resizeStartWidth = Columns[columnIndex].ActualWidth;
                    e.Pointer.Capture(this);
                    e.Handled = true;
                }
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);

            if (_resizingColumnIndex >= 0)
            {
                _resizingColumnIndex = -1;
                e.Pointer.Capture(null);
                RestoreCursor();
                e.Handled = true;
            }
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);

            if (_resizingColumnIndex >= 0)
            {
                _resizingColumnIndex = -1;
                RestoreCursor();
            }
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);

            if (_resizingColumnIndex < 0)
            {
                RestoreCursor();
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            DoubleTapped += OnDoubleTapped;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            DoubleTapped -= OnDoubleTapped;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (!CanUserResizeColumnsInRows || Columns is null)
                return;

            var x = e.GetPosition(this).X;
            var columnIndex = GetColumnBorderAtPosition(x);

            if (columnIndex >= 0 && CanResizeColumn(columnIndex))
            {
                // Double-tap on column border auto-sizes the column
                Columns.SetColumnWidth(columnIndex, GridLength.Auto);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Gets the column index if the position is near the right edge of a column border.
        /// </summary>
        /// <param name="x">The X position relative to this control.</param>
        /// <returns>The column index, or -1 if not near a border.</returns>
        private int GetColumnBorderAtPosition(double x)
        {
            if (Columns is null)
                return -1;

            var columnX = 0.0;

            for (var i = 0; i < Columns.Count; ++i)
            {
                var column = Columns[i];
                var width = column.ActualWidth;

                if (double.IsNaN(width))
                    continue;

                columnX += width;

                // Check if x is within the hit test area at the right edge of this column
                if (x >= columnX - ResizeHitTestWidth && x <= columnX + ResizeHitTestWidth)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Checks if a column can be resized.
        /// </summary>
        private bool CanResizeColumn(int columnIndex)
        {
            if (Columns is null || columnIndex < 0 || columnIndex >= Columns.Count)
                return false;

            var column = Columns[columnIndex];

            // Also check the parent TreeDataGrid's CanUserResizeColumns property
            var treeDataGrid = this.FindAncestorOfType<TreeDataGrid>();
            var gridAllowsResize = treeDataGrid?.CanUserResizeColumns ?? true;

            // Check if the column allows user resizing (column.CanUserResize can be null, meaning defer to grid)
            var columnAllowsResize = column.CanUserResize ?? gridAllowsResize;

            return columnAllowsResize && gridAllowsResize;
        }

        private void RestoreCursor()
        {
            if (_previousCursor is not null)
            {
                Cursor = _previousCursor;
                _previousCursor = null;
            }
            else if (Cursor?.ToString() == "SizeWestEast")
            {
                Cursor = Cursor.Default;
            }
        }

        private void OnColumnLayoutInvalidated(object? sender, EventArgs e)
        {
            InvalidateMeasure();

            foreach (var element in RealizedElements)
            {
                if (element is TreeDataGridRow row)
                    row.CellsPresenter?.InvalidateMeasure();
            }
        }

        private ITreeDataGridSelectionInteraction? GetSelection()
        {
            return this.FindAncestorOfType<TreeDataGrid>()?.SelectionInteraction;
        }

        public int GetChildIndex(ILogical child)
        {
            if (child is TreeDataGridRow row)
            {
                return row.RowIndex;
            }
            return -1;

        }

        public bool TryGetTotalCount(out int count)
        {
            if (Items != null)
            {
                count = Items.Count;
                return true;
            }
            count = 0;
            return false;
        }
    }
}
