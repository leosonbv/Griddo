# Griddo / Plotto Layered Architecture

This repository now uses a layered, multi-project structure to support integration into larger solutions.

## Project Boundaries

- `Griddo.Abstractions`: field/editor contracts.
- `Griddo.Core`: grid core services and non-UI orchestration logic.
- `Griddo`: WPF control implementation.
- `Griddo.Hosting`: host adapters and fluent integration helpers.
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
