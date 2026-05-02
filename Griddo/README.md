# Griddo

`Griddo` is a .NET 10 WPF control library that provides a fast Excel-like grid built from scratch.

## Features

- Columns backed by view objects (`IGriddoColumnView`)
- Virtualized rendering with scrollbars
- Per-column editor configuration (`IGriddoCellEditor`)

## Example

```csharp
using Griddo;

var grid = new Griddo();
grid.Columns.Add(new GriddoColumnView(
    "Name",
    120,
    row => ((Person)row).Name,
    (row, v) => { ((Person)row).Name = (string)v!; return true; },
    editor: GriddoCellEditors.Text));

grid.Columns.Add(new GriddoColumnView(
    "Age",
    80,
    row => ((Person)row).Age,
    (row, v) => { ((Person)row).Age = (double)v!; return true; },
    editor: GriddoCellEditors.Number));
```
