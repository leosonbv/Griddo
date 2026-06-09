# GriddoModelView

ModelView laag voor consistente en eenvoudige configuratie van grids in chemische quantificatie software.

## Bestanden
- `PropertyViewConfiguration.cs` → Globale eigenschappen per veld
- `GridConfiguration.cs` + `FieldConfiguration.cs` → Per-grid lay-out (`FieldConfiguration.SuppressCellEdit` locks scalar in-place edit for that column; hosted plots ignore this in Griddo). `OrderNumber` > 0 = visible column order.
- `PropertyViewStore.cs` + `GridConfigurationStore.cs` → JSON opslag

Gebruik deze voor eenvoudige, herbruikbare kolomdefinities in HPLC, GC-MS, etc.
