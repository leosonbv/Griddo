# Griddo / Plotto Layered Architecture

This repository now uses a layered, multi-project structure to support integration into larger solutions.

## Project Boundaries

- `Griddo.Abstractions`: field/editor contracts.
- `Griddo.Core`: grid core services and non-UI orchestration logic.
- `Griddo`: WPF control implementation.
- `Griddo.Hosting`: host adapters and fluent integration helpers.
  - `Griddo.Hosting.Contracts`: signal provider and series contracts (was Abstractions; internal rename for clarity).
- `GriddoUi`: configurator and UI tooling.
- `Plotto.Abstractions`: chart domain contracts.
- `Plotto.Core`: chart policies and reusable interaction logic.
- `Plotto`: WPF/Skia rendering controls.

## Dependency Rules

- Core and Abstractions are lower-level than WPF control projects.
- UI projects must not become the source of shared contracts.
- Persisted layout must carry stable field identifiers (`SourceFieldKey`) in addition to index fallback.

## Fluent Integration Entry Points

- `Griddo.Hosting.Fluent.GriddoBuilder`
- `Griddo.Hosting.Fluent.HostedFieldBuilder`
- `Plotto.Fluent.PlottoChartBuilder`

## Internal SOLID & Clarity Improvements (applied)

- ISP split on `IPlotFieldLayoutTarget` (segregated `IPlotLayoutTarget` + plot-type specifics + aggregate for compat; public surface unchanged).
- Hosting/Abstractions folder+namespace renamed to Contracts.
- Redundant file prefixes dropped across Griddo (e.g. partials, internals) and GriddoUi.FieldEdit.Support (shorter names).
- Light SRP extracts (e.g. NumericFormatClassifier collaborator).
- All changes verified with full Debug + Release builds (0 new errors).
