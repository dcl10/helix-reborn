namespace HelixReborn.Api.Models;

/// <summary>A dataset as the API returns it.</summary>
/// <param name="Id">Dataset ID.</param>
/// <param name="FileName">The sanitised file name.</param>
/// <param name="SizeBytes">Size of the uploaded file, in bytes.</param>
/// <param name="RowCount">Number of data rows.</param>
/// <param name="CreatedAt">When it was uploaded.</param>
/// <param name="Columns">Column names and inferred types, in file order.</param>
public record DatasetResponse(
  Guid Id,
  string FileName,
  long SizeBytes,
  int RowCount,
  DateTimeOffset CreatedAt,
  IReadOnlyList<DatasetColumn> Columns);
