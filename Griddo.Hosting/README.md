# Griddo.Hosting

`Griddo.Hosting` contains reusable field views that combine `Griddo` cells with `Plotto` charts and rich HTML composition for host applications like Quanto.

## What is in this project

- `Griddo.Hosting.Plot`
  - `HostedChromatogramFieldView`
  - `HostedCalibrationFieldView`
  - `HostedSpectrumFieldView`
  - `IPlotFieldLayoutTarget`
- `Griddo.Hosting.Html`
  - `ComposedHtmlFieldView`
  - `IHtmlFieldLayoutTarget`
- `Griddo.Hosting.Abstractions`
  - signal provider contracts (`IChromatogramSignalProvider`, `ICalibrationSignalProvider`, `ISpectrumSignalProvider`)

## Quanto integration steps

1. Reference `Griddo.Hosting` from Quanto UI.
2. Implement adapter classes from Quanto signal/domain models to:
   - `IChromatogramSignalProvider`
   - `ICalibrationSignalProvider`
   - `ISpectrumSignalProvider`
3. Register hosted fields on your grid using the adapters.
4. Use `IPlotFieldLayoutTarget` and `IHtmlFieldLayoutTarget` for persisted layout/config dialogs.

## Enum/Flags fields in Griddo

Use `GriddoEnumFieldView<TEnum>` and `GriddoFlagsFieldView<TEnum>` from `Griddo.Fields`.

Add annotation-driven background colors on enum members with:

```csharp
[GriddoEnumColor("#FFE8F4")]
Pending
```

`Griddo` resolves dynamic cell background from enum annotations unless an explicit field background override is set.
