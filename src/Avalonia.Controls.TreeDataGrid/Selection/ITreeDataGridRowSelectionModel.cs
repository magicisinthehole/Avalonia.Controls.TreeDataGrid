using System;
using System.Collections.Generic;

namespace Avalonia.Controls.Selection
{
    public interface ITreeDataGridRowSelectionModel : ITreeSelectionModel, ITreeDataGridSelection
    {
        void SelectRange(int begin, int end);
    }

    public interface ITreeDataGridRowSelectionModel<T> : ITreeDataGridRowSelectionModel
    {
        new T? SelectedItem { get; }
        new IReadOnlyList<T?> SelectedItems { get; }
        new event EventHandler<TreeSelectionModelSelectionChangedEventArgs<T>>? SelectionChanged;
    }
}
