using System;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Selection;
using Avalonia.Layout;
using Avalonia.LogicalTree;
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

        private IColumns? _columns;

        public event EventHandler<ChildIndexChangedEventArgs>? ChildIndexChanged;

        public IColumns? Columns
        {
            get => _columns;
            set => SetAndRaise(ColumnsProperty, ref _columns, value);
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
