using System;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using Avalonia.VisualTree;

namespace Avalonia.Controls.Primitives
{
    /// <summary>
    /// A control that renders vertical column separator lines over a TreeDataGrid's rows area.
    /// Also handles column resize in the empty space below rows where the RowsPresenter
    /// doesn't extend. Only hit-testable near column borders via ICustomHitTest.
    /// </summary>
    public class ColumnSeparatorOverlay : Control, ICustomHitTest
    {
        public static readonly StyledProperty<IColumns?> ColumnsProperty =
            AvaloniaProperty.Register<ColumnSeparatorOverlay, IColumns?>(nameof(Columns));

        public static readonly StyledProperty<bool> ShowColumnSeparatorsProperty =
            AvaloniaProperty.Register<ColumnSeparatorOverlay, bool>(nameof(ShowColumnSeparators), false);

        public static readonly StyledProperty<IBrush?> ColumnSeparatorBrushProperty =
            AvaloniaProperty.Register<ColumnSeparatorOverlay, IBrush?>(nameof(ColumnSeparatorBrush));

        public static readonly StyledProperty<double> ColumnSeparatorWidthProperty =
            AvaloniaProperty.Register<ColumnSeparatorOverlay, double>(nameof(ColumnSeparatorWidth), 1.25);

        private const double ResizeHitTestWidth = 5.0;
        private int _resizingColumnIndex = -1;
        private double _resizeStartX;
        private double _resizeStartWidth;
        private Cursor? _previousCursor;

        static ColumnSeparatorOverlay()
        {
            AffectsRender<ColumnSeparatorOverlay>(
                ColumnsProperty,
                ShowColumnSeparatorsProperty,
                ColumnSeparatorBrushProperty,
                ColumnSeparatorWidthProperty);
        }

        /// <summary>
        /// Gets or sets the columns to render separators for.
        /// </summary>
        public IColumns? Columns
        {
            get => GetValue(ColumnsProperty);
            set => SetValue(ColumnsProperty, value);
        }

        /// <summary>
        /// Gets or sets whether to show vertical separator lines between columns.
        /// </summary>
        public bool ShowColumnSeparators
        {
            get => GetValue(ShowColumnSeparatorsProperty);
            set => SetValue(ShowColumnSeparatorsProperty, value);
        }

        /// <summary>
        /// Gets or sets the brush used to draw column separator lines.
        /// </summary>
        public IBrush? ColumnSeparatorBrush
        {
            get => GetValue(ColumnSeparatorBrushProperty);
            set => SetValue(ColumnSeparatorBrushProperty, value);
        }

        public double ColumnSeparatorWidth
        {
            get => GetValue(ColumnSeparatorWidthProperty);
            set => SetValue(ColumnSeparatorWidthProperty, value);
        }

        public bool HitTest(Point point)
        {
            // Only hit-testable near column borders when resize is enabled
            if (_resizingColumnIndex >= 0)
                return true;

            if (!ShowColumnSeparators || Columns is null)
                return false;

            var treeDataGrid = this.FindAncestorOfType<TreeDataGrid>();
            if (treeDataGrid?.CanUserResizeColumnsInRows != true)
                return false;

            return GetColumnBorderAtPosition(point.X) >= 0;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            if (!ShowColumnSeparators || Columns is null || ColumnSeparatorBrush is null)
                return;

            var treeDataGrid = this.FindAncestorOfType<TreeDataGrid>();
            var height = Bounds.Height;
            var separatorWidth = ColumnSeparatorWidth;
            var pen = new Pen(ColumnSeparatorBrush, separatorWidth);
            var x = 0.0;

            for (var i = 0; i < Columns.Count; ++i)
            {
                var column = Columns[i];
                var width = column.ActualWidth;

                if (double.IsNaN(width))
                    continue;

                x += width;

                // Check if this column should show a separator
                // Column-level setting takes precedence, then fall back to grid-level
                var showSeparator = column.ShowSeparator ?? treeDataGrid?.ShowColumnSeparators ?? ShowColumnSeparators;

                if (showSeparator && i < Columns.Count - 1) // Don't draw after last column
                {
                    // Draw line centered on the column boundary
                    var lineX = x - (separatorWidth / 2);
                    context.DrawLine(pen, new Point(lineX, 0), new Point(lineX, height));
                }
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

            if (Columns is null)
                return;

            if (_resizingColumnIndex >= 0)
            {
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

            if (Columns is null)
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

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == ColumnsProperty)
            {
                var oldValue = change.GetOldValue<IColumns>();
                var newValue = change.GetNewValue<IColumns>();

                if (oldValue is not null)
                    oldValue.LayoutInvalidated -= OnColumnLayoutInvalidated;
                if (newValue is not null)
                    newValue.LayoutInvalidated += OnColumnLayoutInvalidated;
            }
        }

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

                if (x >= columnX - ResizeHitTestWidth && x <= columnX + ResizeHitTestWidth)
                {
                    return i;
                }
            }

            return -1;
        }

        private bool CanResizeColumn(int columnIndex)
        {
            if (Columns is null || columnIndex < 0 || columnIndex >= Columns.Count)
                return false;

            var column = Columns[columnIndex];
            var treeDataGrid = this.FindAncestorOfType<TreeDataGrid>();
            var gridAllowsResize = treeDataGrid?.CanUserResizeColumns ?? true;
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

        private void OnColumnLayoutInvalidated(object? sender, System.EventArgs e)
        {
            InvalidateVisual();
        }
    }
}
