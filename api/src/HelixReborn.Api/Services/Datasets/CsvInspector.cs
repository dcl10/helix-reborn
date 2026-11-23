namespace HelixReborn.Api.Services.Datasets;

using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Models;

/// <summary>
/// Validates an uploaded CSV and infers each column's type. Reads the stream once, so callers that also need to keep
/// the bytes (to upload them) should read from a stream that tees into storage as this reads it.
/// </summary>
public static class CsvInspector
{
  /// <summary>Minimum number of header columns allowed.</summary>
  public const int MinColumns = 2;

  /// <summary>Maximum number of header columns allowed.</summary>
  public const int MaxColumns = 1000;

  /// <summary>Minimum number of data rows (excluding the header) allowed.</summary>
  public const int MinDataRows = 10;

  /// <summary>Content types accepted for an upload, alongside its <c>.csv</c> extension.</summary>
  public static readonly IReadOnlyList<string> AllowedContentTypes =
    ["text/csv", "application/vnd.ms-excel", "application/octet-stream"];

  /// <summary>Validates a CSV stream and infers its columns' types.</summary>
  /// <param name="content">The file content. Read to the end.</param>
  /// <param name="fileName">The uploaded file's original name, checked for a <c>.csv</c> extension.</param>
  /// <param name="contentType">The uploaded file's content type.</param>
  /// <returns>The row count and inferred columns.</returns>
  /// <exception cref="DatasetValidationException">The file fails one or more of the CSV rules.</exception>
  public static CsvInspectionResult Inspect(Stream content, string fileName, string? contentType)
  {
    List<string> problems = [];

    if (!fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
    {
      problems.Add("The file must have a .csv extension.");
    }

    if (contentType is null || !AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
    {
      problems.Add(
        $"The file's content type must be one of {string.Join(", ", AllowedContentTypes)}, not '{contentType}'.");
    }

    var config = new CsvConfiguration(CultureInfo.InvariantCulture)
    {
      HasHeaderRecord = true,
      BadDataFound = null,
      MissingFieldFound = null,
      HeaderValidated = null,
    };

    using var reader = new StreamReader(content, leaveOpen: true);
    using var csv = new CsvReader(reader, config);

    string[]? header;
    try
    {
      header = csv.Read() && csv.ReadHeader() ? csv.HeaderRecord : null;
    }
    catch (CsvHelperException ex)
    {
      problems.Add($"The file couldn't be parsed as CSV: {ex.Message}");
      throw new DatasetValidationException(problems);
    }

    if (header is null)
    {
      problems.Add("The file has no header row.");
      throw new DatasetValidationException(problems);
    }

    ValidateHeader(header, problems);

    var columns = header.Select(name => new ColumnAccumulator(name)).ToArray();
    var rowCount = 0;

    try
    {
      while (csv.Read())
      {
        rowCount++;
        for (var i = 0; i < columns.Length; i++)
        {
          columns[i].Add(csv.TryGetField<string?>(i, out var value) ? value : null);
        }
      }
    }
    catch (CsvHelperException ex)
    {
      problems.Add($"The file couldn't be parsed as CSV: {ex.Message}");
      throw new DatasetValidationException(problems);
    }

    if (rowCount < MinDataRows)
    {
      problems.Add($"The file must have at least {MinDataRows} data rows; it has {rowCount}.");
    }

    if (problems.Count > 0)
    {
      throw new DatasetValidationException(problems);
    }

    return new CsvInspectionResult(rowCount, [.. columns.Select(x => x.ToColumn())]);
  }

  private static void ValidateHeader(IReadOnlyList<string> header, List<string> problems)
  {
    if (header.Count is < MinColumns or > MaxColumns)
    {
      problems.Add($"The file must have between {MinColumns} and {MaxColumns} columns; it has {header.Count}.");
    }

    if (header.Any(string.IsNullOrWhiteSpace))
    {
      problems.Add("Every column must have a non-empty name.");
    }

    if (header.Distinct(StringComparer.Ordinal).Count() != header.Count)
    {
      problems.Add("Column names must be unique.");
    }
  }

  /// <summary>Tracks one column's values while scanning the file, to infer its type.</summary>
  /// <param name="name">The column's header name.</param>
  private sealed class ColumnAccumulator(string name)
  {
    private int _missingCount;
    private int _nonBlankCount;
    private bool _allNumeric = true;

    public void Add(string? value)
    {
      if (string.IsNullOrWhiteSpace(value))
      {
        _missingCount++;
        return;
      }

      _nonBlankCount++;
      _allNumeric &= double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    public DatasetColumn ToColumn() =>
      new(name, _nonBlankCount == 0 ? "empty" : _allNumeric ? "numeric" : "text", _missingCount);
  }
}

/// <summary>The result of inspecting a CSV file.</summary>
/// <param name="RowCount">Number of data rows, excluding the header.</param>
/// <param name="Columns">Column names and inferred types, in file order.</param>
public record CsvInspectionResult(int RowCount, IReadOnlyList<DatasetColumn> Columns);
