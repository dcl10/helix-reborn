namespace HelixReborn.Api.Models.Eda;

/// <summary>Body of <c>POST /api/experiments/{id}/eda/tables/normality</c>.</summary>
/// <param name="Columns">The numeric columns to test, or null for every numeric column.</param>
public record NormalityTableRequest(IReadOnlyList<string>? Columns);
