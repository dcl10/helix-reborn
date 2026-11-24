namespace HelixReborn.Api.Models;

/// <summary>One column of an uploaded dataset, as inferred by <see cref="Services.Datasets.CsvInspector"/>.</summary>
/// <param name="Name">The header name.</param>
/// <param name="InferredType"><c>"numeric"</c>, <c>"text"</c> or <c>"empty"</c>.</param>
/// <param name="MissingCount">Number of blank values in the column.</param>
public record DatasetColumn(string Name, string InferredType, int MissingCount);
