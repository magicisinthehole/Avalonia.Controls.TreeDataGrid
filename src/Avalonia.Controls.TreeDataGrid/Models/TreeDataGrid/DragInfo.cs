using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Avalonia.Input;

namespace Avalonia.Controls.Models.TreeDataGrid
{
    /// <summary>
    /// Holds information about an automatic row drag/drop operation carried out
    /// by <see cref="Avalonia.Controls.TreeDataGrid.AutoDragDropRows"/>.
    /// </summary>
    public class DragInfo
    {
        /// <summary>
        /// Defines the data format for drag/drop operations (stores ID as string).
        /// </summary>
        public static readonly DataFormat<string> DragFormat =
            DataFormat.CreateStringApplicationFormat("TreeDataGridDragInfo");

        // Registry to store DragInfo by ID for the new DataTransfer API
        internal static readonly ConcurrentDictionary<string, DragInfo> _registry = new();

        /// <summary>
        /// Unique ID for this drag operation.
        /// </summary>
        public string Id { get; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Registers this DragInfo and returns its ID.
        /// </summary>
        public string Register()
        {
            _registry[Id] = this;
            return Id;
        }

        /// <summary>
        /// Retrieves a DragInfo by its ID and removes it from the registry.
        /// </summary>
        public static DragInfo? TryGet(string? id)
        {
            if (id != null && _registry.TryRemove(id, out var info))
                return info;
            return null;
        }

        /// <summary>
        /// Cleans up any orphaned drag info (call on drag end if needed).
        /// </summary>
        public static void Cleanup(string? id)
        {
            if (id != null)
                _registry.TryRemove(id, out _);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DragInfo"/> class.
        /// </summary>
        /// <param name="source">The source of the drag operation/</param>
        /// <param name="indexes">The indexes being dragged.</param>
        public DragInfo(ITreeDataGridSource source, IEnumerable<IndexPath> indexes)
        {
            Source = source;
            Indexes = indexes;
        }

        /// <summary>
        /// Gets the <see cref="ITreeDataGridSource"/> that rows are being dragged from.
        /// </summary>
        public ITreeDataGridSource Source { get; }

        /// <summary>
        /// Gets or sets the model indexes of the rows being dragged.
        /// </summary>
        public IEnumerable<IndexPath> Indexes { get; }
    }
}
