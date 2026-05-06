# Griddo

`Griddo` is a .NET 10 WPF control library that provides a fast Excel-like grid built from scratch.

## Features

- Fields backed by view objects (`IGriddoFieldView`)
- Virtualized rendering with scrollbars
- Per-field editor configuration (`IGriddoCellEditor`)

## Example

```csharp
using Griddo;

var grid = new Griddo();
grid.Fields.Add(new GriddoFieldView(
    "Name",
    120,
    record => ((Person)record).Name,
    (record, v) => { ((Person)record).Name = (string)v!; return true; },
    editor: GriddoCellEditors.Text));

grid.Fields.Add(new GriddoFieldView(
    "Age",
    80,
    record => ((Person)record).Age,
    (record, v) => { ((Person)record).Age = (double)v!; return true; },
    editor: GriddoCellEditors.Number));
```
