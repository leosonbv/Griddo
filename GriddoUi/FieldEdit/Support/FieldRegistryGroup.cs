using Griddo.Abstractions.Fields;
using Griddo.Fields;

namespace GriddoUi.FieldEdit.Support;

/// <summary>
/// A named group of fields that share a common source object (e.g. "Sample", "Compound", "Quantification").
/// Pass a list of these to the multi-source overloads of <see cref="FieldMetadataBuilder"/> and
/// <see cref="FieldChooserGridApplier"/> so that fields from each source can be edited and
/// previewed with metadata derived from the correct source object rather than from a single flat record.
/// </summary>
/// <param name="SourceName">
/// The source-object name that matches <see cref="IGriddoFieldSourceObject.SourceObjectName"/> on
/// every field in <paramref name="Fields"/>.  Use an empty string for fields that are not bound to a
/// named source object.
/// </param>
/// <param name="Fields">All fields (visible or hidden) that belong to this source.</param>
public sealed record FieldRegistryGroup(
    string SourceName,
    IReadOnlyList<IGriddoFieldView> Fields);
