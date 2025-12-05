# TreeDataGrid Group Headers Implementation Plan

**Goal**: Add native row group header support to TreeDataGrid that works with data virtualization.

**Core Principle**: Group headers ARE rows in the `IRows` collection. They appear inline at exact positions with exact heights. The visual row index and model index are distinct - headers have no model index.

---

## Architecture

```
Source Items (from ModelFlow):
  [0] Track A1 (AlbumId: A)
  [1] Track A2 (AlbumId: A)
  [2] Track A3 (AlbumId: A)
  [3] Track B1 (AlbumId: B)
  [4] Track B2 (AlbumId: B)

IRows Collection (what TreeDataGrid renders):
  [0] GroupHeaderRow (Album A)   → ModelIndex: invalid
  [1] DataRow (Track A1)         → ModelIndex: 0
  [2] DataRow (Track A2)         → ModelIndex: 1
  [3] DataRow (Track A3)         → ModelIndex: 2
  [4] GroupHeaderRow (Album B)   → ModelIndex: invalid
  [5] DataRow (Track B1)         → ModelIndex: 3
  [6] DataRow (Track B2)         → ModelIndex: 4

IRows.Count = 7 (visual rows including headers)
Source.Count = 5 (data items only)

Index Mapping:
  ModelIndexToRowIndex(0) → 1
  ModelIndexToRowIndex(3) → 5
  RowIndexToModelIndex(0) → invalid (header)
  RowIndexToModelIndex(1) → 0
  RowIndexToModelIndex(5) → 3
```

---

## Phase 1: Row Model Extensions

### Task 1.1: Create IGroupHeaderRow Interface

**File**: `src/Avalonia.Controls.TreeDataGrid/Models/TreeDataGrid/IGroupHeaderRow.cs` (NEW)

**Changes**:
```csharp
namespace Avalonia.Controls.Models.TreeDataGrid
{
    /// <summary>
    /// Represents a group header row in an <see cref="ITreeDataGridSource"/>.
    /// Group headers are visual separators and have no associated model index.
    /// </summary>
    public interface IGroupHeaderRow : IRow
    {
        /// <summary>
        /// Gets the group key that identifies this group.
        /// </summary>
        object? GroupKey { get; }
    }
}
```

**Implementation Log**:
```
[Date] [Status]
-
```

---

### Task 1.2: Create GroupHeaderRow Class

**File**: `src/Avalonia.Controls.TreeDataGrid/Models/TreeDataGrid/GroupHeaderRow.cs` (NEW)

**Changes**:
```csharp
namespace Avalonia.Controls.Models.TreeDataGrid
{
    /// <summary>
    /// A reusable group header row.
    /// </summary>
    internal class GroupHeaderRow<TModel> : IGroupHeaderRow
    {
        public object? GroupKey { get; private set; }
        public object? Header => GroupKey;
        public object? Model => null;  // Headers have no model
        public GridLength Height { get; set; } = GridLength.Auto;

        public GroupHeaderRow<TModel> Update(object? groupKey)
        {
            GroupKey = groupKey;
            return this;
        }
    }
}
```

**Implementation Log**:
```
[Date] [Status]
-
```

---

## Phase 2: Grouped Rows Collection

### Task 2.1: Create GroupedRows Class

**File**: `src/Avalonia.Controls.TreeDataGrid/Models/TreeDataGrid/GroupedRows.cs` (NEW)

This is the core class that wraps the source items and inserts header rows.

**Changes**:
```csharp
namespace Avalonia.Controls.Models.TreeDataGrid
{
    /// <summary>
    /// Wraps a collection of models and inserts group header rows at group boundaries.
    /// </summary>
    /// <typeparam name="TModel">The model type.</typeparam>
    public class GroupedRows<TModel> : ReadOnlyListBase<IRow>, IRows, IDisposable
    {
        private readonly TreeDataGridItemsSourceView<TModel> _items;
        private readonly Func<TModel, object?> _groupKeySelector;
        private readonly IComparer<TModel>? _comparer;

        // Reusable row objects
        private readonly AnonymousRow<TModel> _dataRow;
        private readonly GroupHeaderRow<TModel> _headerRow;

        // Index mapping: visual index → (isHeader, modelIndex)
        // Built lazily when items are accessed
        private List<(bool isHeader, int modelIndex, object? groupKey)>? _indexMap;

        public GroupedRows(
            TreeDataGridItemsSourceView<TModel> items,
            Func<TModel, object?> groupKeySelector,
            IComparer<TModel>? comparer);

        public override int Count { get; }  // Visual row count including headers
        public override IRow this[int index] { get; }

        public int ModelIndexToRowIndex(IndexPath modelIndex);
        public IndexPath RowIndexToModelIndex(int rowIndex);
        public ICell RealizeCell(IColumn column, int columnIndex, int rowIndex);
        public void UnrealizeCell(ICell cell, int columnIndex, int rowIndex);
        public (int index, double y) GetRowAt(double y);

        private void BuildIndexMap();
        private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e);
    }
}
```

**Key Implementation Details**:

1. **BuildIndexMap()**: Iterates through sorted items, detects group boundaries by comparing adjacent group keys, builds mapping array.

2. **Count**: Returns `_indexMap.Count` (items + headers)

3. **this[index]**:
   - If `_indexMap[index].isHeader` → return `_headerRow.Update(groupKey)`
   - Else → return `_dataRow.Update(modelIndex, _items[modelIndex])`

4. **ModelIndexToRowIndex**: Binary search or linear scan through `_indexMap` to find visual index for model index.

5. **RowIndexToModelIndex**: Direct lookup - if header, return empty IndexPath.

6. **RealizeCell**: For headers, return a special header cell. For data rows, delegate to column.

**Implementation Log**:
```
[Date] [Status]
-
```

---

### Task 2.2: Implement Index Map Building

**File**: `src/Avalonia.Controls.TreeDataGrid/Models/TreeDataGrid/GroupedRows.cs`

**Changes**:
```csharp
private void BuildIndexMap()
{
    _indexMap = new List<(bool, int, object?)>();

    if (_items.Count == 0)
        return;

    object? prevGroupKey = null;
    bool isFirst = true;

    // If sorted, iterate in sort order
    // If not sorted, iterate in natural order
    for (int modelIndex = 0; modelIndex < _items.Count; modelIndex++)
    {
        var actualIndex = _comparer != null ? _sortedIndexes[modelIndex] : modelIndex;
        var model = _items[actualIndex];
        var groupKey = _groupKeySelector(model);

        // Insert header if group changed
        if (isFirst || !Equals(groupKey, prevGroupKey))
        {
            _indexMap.Add((isHeader: true, modelIndex: -1, groupKey));
            prevGroupKey = groupKey;
            isFirst = false;
        }

        // Insert data row
        _indexMap.Add((isHeader: false, modelIndex: actualIndex, groupKey: null));
    }
}
```

**Implementation Log**:
```
[Date] [Status]
-
```

---

### Task 2.3: Handle Collection Changes - Add

**File**: `src/Avalonia.Controls.TreeDataGrid/Models/TreeDataGrid/GroupedRows.cs`

**Scenario**: Items added at model index X (e.g., ModelFlow loads a new page)

**Changes**:
```csharp
private void OnItemsAdded(int modelStartIndex, IList newItems)
{
    if (_indexMap == null)
    {
        BuildIndexMap();
        CollectionChanged?.Invoke(this, CollectionExtensions.ResetEvent);
        return;
    }

    // Find the visual index where new items will be inserted
    // This is after the last data row with modelIndex < modelStartIndex
    int visualInsertIndex = FindVisualIndexForModelIndex(modelStartIndex);

    // Track what we're inserting for CollectionChanged events
    var insertedRows = new List<IRow>();
    int insertCount = 0;

    // Get group key of item before insertion point (if any)
    object? prevGroupKey = GetGroupKeyAtVisualIndex(visualInsertIndex - 1);

    for (int i = 0; i < newItems.Count; i++)
    {
        var model = (TModel)newItems[i]!;
        var groupKey = _groupKeySelector(model);
        int modelIndex = modelStartIndex + i;

        // Check if we need a new header
        bool needsHeader = !Equals(groupKey, prevGroupKey);

        if (needsHeader)
        {
            // Insert header row
            _indexMap.Insert(visualInsertIndex + insertCount,
                (isHeader: true, modelIndex: -1, groupKey: groupKey));
            insertedRows.Add(_headerRow.Update(groupKey));
            insertCount++;
            prevGroupKey = groupKey;
        }

        // Insert data row
        _indexMap.Insert(visualInsertIndex + insertCount,
            (isHeader: false, modelIndex: modelIndex, groupKey: null));
        insertedRows.Add(_dataRow.Update(modelIndex, model));
        insertCount++;
    }

    // Update model indices for all data rows after insertion point
    UpdateModelIndicesAfter(visualInsertIndex + insertCount, newItems.Count);

    // Check if we need to remove a header that's no longer needed
    // (if the item after our insertion now has the same group key as our last item)
    RemoveOrphanedHeaderAfter(visualInsertIndex + insertCount, prevGroupKey);

    // Fire collection changed
    CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
        NotifyCollectionChangedAction.Add,
        insertedRows,
        visualInsertIndex));
}
```

**Implementation Log**:
```
[Date] [Status]
-
```

---

### Task 2.4: Handle Collection Changes - Remove

**File**: `src/Avalonia.Controls.TreeDataGrid/Models/TreeDataGrid/GroupedRows.cs`

**Scenario**: Items removed at model index X

**Changes**:
```csharp
private void OnItemsRemoved(int modelStartIndex, IList oldItems)
{
    if (_indexMap == null)
        return;

    // Find visual indices of removed items
    var visualIndicesToRemove = new List<int>();

    for (int i = 0; i < oldItems.Count; i++)
    {
        int modelIndex = modelStartIndex + i;
        int visualIndex = ModelIndexToRowIndex(new IndexPath(modelIndex));
        if (visualIndex >= 0)
            visualIndicesToRemove.Add(visualIndex);
    }

    // Also identify headers that become orphaned (no items left in group)
    var headersToRemove = FindOrphanedHeaders(visualIndicesToRemove);
    visualIndicesToRemove.AddRange(headersToRemove);
    visualIndicesToRemove.Sort();

    // Remove from end to start to preserve indices
    var removedRows = new List<IRow>();
    for (int i = visualIndicesToRemove.Count - 1; i >= 0; i--)
    {
        int visualIndex = visualIndicesToRemove[i];
        removedRows.Insert(0, this[visualIndex]);
        _indexMap.RemoveAt(visualIndex);
    }

    // Update model indices for remaining data rows
    UpdateModelIndicesAfter(visualIndicesToRemove[0], -oldItems.Count);

    // Fire collection changed
    CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
        NotifyCollectionChangedAction.Remove,
        removedRows,
        visualIndicesToRemove[0]));
}

private List<int> FindOrphanedHeaders(List<int> removedDataIndices)
{
    var orphanedHeaders = new List<int>();

    foreach (var visualIndex in removedDataIndices)
    {
        // Check if there's a header immediately before this item
        if (visualIndex > 0 && _indexMap[visualIndex - 1].isHeader)
        {
            int headerIndex = visualIndex - 1;

            // Check if this header has any remaining data rows
            bool hasRemainingItems = false;
            var headerGroupKey = _indexMap[headerIndex].groupKey;

            for (int i = headerIndex + 1; i < _indexMap.Count; i++)
            {
                if (_indexMap[i].isHeader)
                    break; // Hit next group
                if (!removedDataIndices.Contains(i))
                {
                    hasRemainingItems = true;
                    break;
                }
            }

            if (!hasRemainingItems && !orphanedHeaders.Contains(headerIndex))
                orphanedHeaders.Add(headerIndex);
        }
    }

    return orphanedHeaders;
}
```

**Implementation Log**:
```
[Date] [Status]
-
```

---

### Task 2.5: Handle Collection Changes - Replace

**File**: `src/Avalonia.Controls.TreeDataGrid/Models/TreeDataGrid/GroupedRows.cs`

**Scenario**: Items replaced at model index X (common with ModelFlow placeholder → real data)

**Changes**:
```csharp
private void OnItemsReplaced(int modelStartIndex, IList oldItems, IList newItems)
{
    if (_indexMap == null)
    {
        BuildIndexMap();
        CollectionChanged?.Invoke(this, CollectionExtensions.ResetEvent);
        return;
    }

    // For each replaced item, check if group key changed
    for (int i = 0; i < newItems.Count; i++)
    {
        int modelIndex = modelStartIndex + i;
        int visualIndex = ModelIndexToRowIndex(new IndexPath(modelIndex));

        if (visualIndex < 0)
            continue;

        var oldModel = (TModel)oldItems[i]!;
        var newModel = (TModel)newItems[i]!;

        var oldGroupKey = _groupKeySelector(oldModel);
        var newGroupKey = _groupKeySelector(newModel);

        if (!Equals(oldGroupKey, newGroupKey))
        {
            // Group key changed - need to potentially move this item
            // and update headers
            HandleGroupKeyChange(visualIndex, modelIndex, oldGroupKey, newGroupKey);
        }
        else
        {
            // Same group key - just fire replace event
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Replace,
                _dataRow.Update(modelIndex, newModel),
                _dataRow.Update(modelIndex, oldModel),
                visualIndex));
        }
    }
}

private void HandleGroupKeyChange(int visualIndex, int modelIndex, object? oldKey, object? newKey)
{
    // Get keys of adjacent items
    object? prevKey = GetGroupKeyAtVisualIndex(visualIndex - 1);
    object? nextKey = GetGroupKeyAtVisualIndex(visualIndex + 1);

    bool hadHeaderBefore = visualIndex > 0 && _indexMap[visualIndex - 1].isHeader;
    bool needsHeaderNow = !Equals(newKey, prevKey);

    // Case 1: Needed header, still needs header (but possibly different key)
    if (hadHeaderBefore && needsHeaderNow)
    {
        // Update header's group key
        _indexMap[visualIndex - 1] = (true, -1, newKey);
        // Fire replace for header
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Replace,
            _headerRow.Update(newKey),
            _headerRow.Update(oldKey),
            visualIndex - 1));
    }
    // Case 2: Had header, no longer needs one
    else if (hadHeaderBefore && !needsHeaderNow)
    {
        // Remove the header
        _indexMap.RemoveAt(visualIndex - 1);
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Remove,
            _headerRow.Update(oldKey),
            visualIndex - 1));
    }
    // Case 3: Didn't have header, now needs one
    else if (!hadHeaderBefore && needsHeaderNow)
    {
        // Insert header
        _indexMap.Insert(visualIndex, (true, -1, newKey));
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add,
            _headerRow.Update(newKey),
            visualIndex));
    }

    // Check if next item now needs/doesn't need a header
    int nextVisualIndex = needsHeaderNow ? visualIndex + 2 : visualIndex + 1;
    if (nextVisualIndex < _indexMap.Count && !_indexMap[nextVisualIndex].isHeader)
    {
        object? nextItemKey = GetGroupKeyAtVisualIndex(nextVisualIndex);
        bool nextHadHeader = _indexMap[nextVisualIndex - 1].isHeader;
        bool nextNeedsHeader = !Equals(nextItemKey, newKey);

        if (nextHadHeader && !nextNeedsHeader)
        {
            // Remove orphaned header
            _indexMap.RemoveAt(nextVisualIndex - 1);
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Remove,
                _headerRow.Update(nextItemKey),
                nextVisualIndex - 1));
        }
        else if (!nextHadHeader && nextNeedsHeader)
        {
            // Insert needed header
            _indexMap.Insert(nextVisualIndex, (true, -1, nextItemKey));
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Add,
                _headerRow.Update(nextItemKey),
                nextVisualIndex));
        }
    }
}
```

**Implementation Log**:
```
[Date] [Status]
-
```

---

### Task 2.6: Handle Collection Changes - Reset

**File**: `src/Avalonia.Controls.TreeDataGrid/Models/TreeDataGrid/GroupedRows.cs`

**Scenario**: Full collection reset

**Changes**:
```csharp
private void OnItemsReset()
{
    _indexMap = null; // Will rebuild lazily on next access
    CollectionChanged?.Invoke(this, CollectionExtensions.ResetEvent);
}
```

**Implementation Log**:
```
[Date] [Status]
-
```

---

### Task 2.7: Index Map Helper Methods

**File**: `src/Avalonia.Controls.TreeDataGrid/Models/TreeDataGrid/GroupedRows.cs`

**Changes**:
```csharp
/// <summary>
/// Finds the visual index where a model index would be inserted.
/// </summary>
private int FindVisualIndexForModelIndex(int modelIndex)
{
    EnsureIndexMap();

    for (int i = 0; i < _indexMap!.Count; i++)
    {
        var entry = _indexMap[i];
        if (!entry.isHeader && entry.modelIndex >= modelIndex)
            return i;
    }

    return _indexMap.Count; // Append at end
}

/// <summary>
/// Gets the group key for the item at a visual index.
/// Returns null for headers or invalid indices.
/// </summary>
private object? GetGroupKeyAtVisualIndex(int visualIndex)
{
    if (visualIndex < 0 || _indexMap == null || visualIndex >= _indexMap.Count)
        return null;

    var entry = _indexMap[visualIndex];

    if (entry.isHeader)
        return entry.groupKey;

    // For data rows, compute the group key
    var model = _items[entry.modelIndex];
    return _groupKeySelector(model);
}

/// <summary>
/// Updates model indices in the index map after a given visual index.
/// Used after insertions/deletions to keep indices accurate.
/// </summary>
private void UpdateModelIndicesAfter(int afterVisualIndex, int delta)
{
    if (_indexMap == null)
        return;

    for (int i = afterVisualIndex; i < _indexMap.Count; i++)
    {
        var entry = _indexMap[i];
        if (!entry.isHeader)
        {
            _indexMap[i] = (false, entry.modelIndex + delta, null);
        }
    }
}

/// <summary>
/// Ensures index map is built before access.
/// </summary>
private void EnsureIndexMap()
{
    if (_indexMap == null)
        BuildIndexMap();
}
```

**Implementation Log**:
```
[Date] [Status]
-
```

---

## Phase 3: FlatTreeDataGridSource Integration

### Task 3.1: Add GroupKeySelector Property

**File**: `src/Avalonia.Controls.TreeDataGrid/FlatTreeDataGridSource.cs`

**Changes**:
```csharp
private Func<TModel, object?>? _groupKeySelector;

public Func<TModel, object?>? GroupKeySelector
{
    get => _groupKeySelector;
    set
    {
        if (_groupKeySelector != value)
        {
            _groupKeySelector = value;
            _rows?.Dispose();
            _rows = null;
            RaisePropertyChanged(nameof(Rows));
        }
    }
}
```

**Implementation Log**:
```
[Date] [Status]
-
```

---

### Task 3.2: Modify CreateRows to Use GroupedRows

**File**: `src/Avalonia.Controls.TreeDataGrid/FlatTreeDataGridSource.cs`

**Changes**:
```csharp
private IRows CreateRows()
{
    if (_groupKeySelector != null)
    {
        return new GroupedRows<TModel>(_itemsView, _groupKeySelector, _comparer);
    }
    else
    {
        return new AnonymousSortableRows<TModel>(_itemsView, _comparer);
    }
}
```

**Implementation Log**:
```
[Date] [Status]
-
```

---

### Task 3.3: Update Sort Method for Grouped Rows

**File**: `src/Avalonia.Controls.TreeDataGrid/FlatTreeDataGridSource.cs`

**Changes**:
- When sort changes, if using GroupedRows, call its Sort method
- GroupedRows.Sort() should invalidate index map and rebuild

**Implementation Log**:
```
[Date] [Status]
-
```

---

## Phase 4: Visual Row Handling

### Task 4.1: Create TreeDataGridGroupHeaderCell

**File**: `src/Avalonia.Controls.TreeDataGrid/Primitives/TreeDataGridGroupHeaderCell.cs` (NEW)

**Changes**:
- A cell type that spans all columns for group headers
- Or: a special ICell implementation for headers

**Implementation Log**:
```
[Date] [Status]
-
```

---

### Task 4.2: Modify TreeDataGridRow for Header Support

**File**: `src/Avalonia.Controls.TreeDataGrid/Primitives/TreeDataGridRow.cs`

**Changes**:
- Add `IsGroupHeader` property (read from IRow)
- Add `:groupheader` pseudo-class when IsGroupHeader is true
- Cells presenter may render differently for headers

**Implementation Log**:
```
[Date] [Status]
-
```

---

### Task 4.3: Modify RealizeCell for Headers

**File**: `src/Avalonia.Controls.TreeDataGrid/Models/TreeDataGrid/GroupedRows.cs`

**Changes**:
```csharp
public ICell RealizeCell(IColumn column, int columnIndex, int rowIndex)
{
    var mapping = _indexMap![rowIndex];

    if (mapping.isHeader)
    {
        // Return header cell - could be a TextCell with group name
        // Or a special GroupHeaderCell
        return new GroupHeaderCell(mapping.groupKey);
    }

    // Normal data row
    if (column is IColumn<TModel> c)
        return c.CreateCell(this[rowIndex] as IRow<TModel>);

    throw new InvalidOperationException("Invalid column.");
}
```

**Implementation Log**:
```
[Date] [Status]
-
```

---

### Task 4.4: Create GroupHeaderCell

**File**: `src/Avalonia.Controls.TreeDataGrid/Models/TreeDataGrid/Cells/GroupHeaderCell.cs` (NEW)

**Changes**:
```csharp
public class GroupHeaderCell : ICell, IDisposable
{
    public GroupHeaderCell(object? groupKey)
    {
        Value = groupKey;
    }

    public object? Value { get; }
    public bool CanEdit => false;

    public Control CreateControl() => new TextBlock { Text = Value?.ToString() };

    public void Dispose() { }
}
```

**Implementation Log**:
```
[Date] [Status]
-
```

---

## Phase 5: Selection Handling

### Task 5.1: Update Selection to Skip Headers

**File**: Selection-related files

**Changes**:
- Selection should use model indices, not row indices
- When selecting by row index, skip if it's a header (RowIndexToModelIndex returns invalid)
- Range selection should skip headers
- Keyboard navigation should skip headers

**Implementation Log**:
```
[Date] [Status]
-
```

---

### Task 5.2: Update TreeDataGridRow Selection State

**File**: `src/Avalonia.Controls.TreeDataGrid/Primitives/TreeDataGridRow.cs`

**Changes**:
- Headers should never show as selected
- In `Realize()`, check if row is header and skip selection state

**Implementation Log**:
```
[Date] [Status]
-
```

---

## Phase 6: Styling

### Task 6.1: Add Default Group Header Style

**File**: `src/Avalonia.Controls.TreeDataGrid/Themes/Fluent/TreeDataGrid.axaml`

**Changes**:
```xml
<Style Selector="TreeDataGridRow:groupheader">
    <Setter Property="Background" Value="{DynamicResource TreeDataGridGroupHeaderBackground}" />
    <Setter Property="FontWeight" Value="SemiBold" />
    <Setter Property="MinHeight" Value="32" />
</Style>
```

**Implementation Log**:
```
[Date] [Status]
-
```

---

## Phase 7: Xune Integration

### Task 7.1: Enable Grouping in SongListView

**File**: `src/Xune.App/Controls/SongListView.axaml.cs`

**Changes**:
```csharp
private FlatTreeDataGridSource<DataItem<AudioTrack>> CreateTreeDataGridSource(MusicLibraryViewModel vm)
{
    var source = new FlatTreeDataGridSource<DataItem<AudioTrack>>(vm.SongList.DataSource.Collection);

    // Enable grouping when in "By Album" mode
    if (vm.SongList.SelectedSort == TrackSortOption.ByAlbum)
    {
        source.GroupKeySelector = item => item.Item?.AlbumId;
    }

    ConfigureColumns(source, vm);
    return source;
}
```

**Implementation Log**:
```
[Date] [Status]
-
```

---

### Task 7.2: Custom Group Header Styling for Xune

**File**: Xune theme files

**Changes**:
- Style group headers to match Zune aesthetic
- Display album name prominently
- Consider showing album art, artist name, track count

**Implementation Log**:
```
[Date] [Status]
-
```

---

### Task 7.3: Update GroupKeySelector on Sort Change

**File**: `src/Xune.App/Controls/SongListView.axaml.cs`

**Changes**:
- Subscribe to sort changes
- Update `GroupKeySelector` (set or clear) based on sort mode
- Setting to null disables grouping

**Implementation Log**:
```
[Date] [Status]
-
```

---

## Phase 8: Testing

### Task 8.1: Unit Tests for GroupedRows

**Changes**:
- Test Count includes headers
- Test index mapping correctness
- Test group boundary detection
- Test with empty collection
- Test with single item
- Test with all same group key
- Test with all different group keys

**Implementation Log**:
```
[Date] [Status]
-
```

---

### Task 8.2: Integration Tests

**Changes**:
- Test scroll position accuracy
- Test selection with headers present
- Test keyboard navigation
- Test with ModelFlow virtualization
- Test rapid scrolling
- Test sort changes

**Implementation Log**:
```
[Date] [Status]
-
```

---

## Summary

### Files to Create:
1. `Models/TreeDataGrid/IGroupHeaderRow.cs`
2. `Models/TreeDataGrid/GroupHeaderRow.cs`
3. `Models/TreeDataGrid/GroupedRows.cs`
4. `Models/TreeDataGrid/Cells/GroupHeaderCell.cs`

### Files to Modify:
1. `FlatTreeDataGridSource.cs` - Add GroupKeySelector, use GroupedRows
2. `TreeDataGridRow.cs` - Add IsGroupHeader, :groupheader pseudo-class
3. `Themes/Fluent/TreeDataGrid.axaml` - Group header styles
4. Selection files - Skip headers in selection logic

### Key Points:
- Headers ARE rows in IRows (not overlays, not estimated)
- Headers have exact positions and heights
- Visual index ≠ model index when grouping enabled
- ModelIndexToRowIndex / RowIndexToModelIndex handle mapping
- Selection operates on model indices
- Fully compatible with ModelFlow virtualization
