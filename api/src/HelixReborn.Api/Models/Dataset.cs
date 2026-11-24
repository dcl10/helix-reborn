namespace HelixReborn.Api.Models;

/// <summary>The one CSV dataset uploaded to an experiment.</summary>
public class Dataset
{
  /// <summary>Unique identifier.</summary>
  public Guid Id { get; set; }

  /// <summary>The owning experiment. One dataset per experiment.</summary>
  public Guid ExperimentId { get; set; }

  /// <summary>
  /// The uploaded file's name, sanitised: only <c>[A-Za-z0-9._-]</c>, other characters replaced with <c>_</c>, up to
  /// 100 characters.
  /// </summary>
  public required string FileName { get; set; }

  /// <summary>Size of the uploaded file, in bytes.</summary>
  public long SizeBytes { get; set; }

  /// <summary>The S3 key the file is stored at, see <see cref="Services.Storage.StorageKeys.Dataset(Guid, Guid, string)"/>.</summary>
  public required string StorageKey { get; set; }

  /// <summary>Number of data rows (excluding the header).</summary>
  public int RowCount { get; set; }

  /// <summary>Column names and inferred types, in file order. Stored as <c>jsonb</c>.</summary>
  public List<DatasetColumn> Columns { get; set; } = [];

  /// <summary>When the dataset was uploaded.</summary>
  public DateTimeOffset CreatedAt { get; set; }
}
