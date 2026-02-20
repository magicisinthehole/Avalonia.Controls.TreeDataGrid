using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Avalonia.Controls.Primitives
{
    /// <summary>
    /// A control that renders vertical column separator lines over a TreeDataGrid's rows area.
    /// This is used because TreeDataGridPresenterBase extends Border, which has a sealed Render method.
    /// </summary>
    public class ColumnSeparatorOverlay : Control
    {
        public static readonly StyledProperty<IColumns?> ColumnsProperty =
            AvaloniaProperty.Register<ColumnSeparatorOverlay, IColumns?>(nameof(Columns));

        public static readonly StyledProperty<bool> ShowColumnSeparatorsProperty =
            AvaloniaProperty.Register<ColumnSeparatorOverlay, bool>(nameof(ShowColumnSeparators), false);

        public static readonly StyledProperty<IBrush?> ColumnSeparatorBrushProperty =
            AvaloniaProperty.Register<ColumnSeparatorOverlay, IBrush?>(nameof(ColumnSeparatorBrush));

        public static readonly StyledProperty<double> ColumnSeparatorWidthProperty =
            AvaloniaProperty.Register<ColumnSeparatorOverlay, double>(nameof(ColumnSeparatorWidth), 1.25);

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

        private void OnColumnLayoutInvalidated(object? sender, System.EventArgs e)
        {
            InvalidateVisual();
        }
    }
}
