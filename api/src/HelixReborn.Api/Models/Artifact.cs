namespace HelixReborn.Api.Models;

/// <summary>
/// A file produced by or uploaded to an experiment: the dataset, EDA plots and tables, a run's model and metadata,
/// evaluation plots and tables, or predictions. Later issues register these through
/// <c>RecordAsync</c> on <see cref="Services.Artifacts.IArtifactService"/> as they produce files.
/// </summary>
public class Artifact
{
  /// <summary>Unique identifier.</summary>
  public Guid Id { get; set; }

  /// <summary>The owning experiment. Cascade deleted with it.</summary>
  public Guid ExperimentId { get; set; }

  /// <summary>What kind of file this is.</summary>
  public ArtifactKind Kind { get; set; }

  /// <summary>Human readable title, e.g. "Correlation heatmap".</summary>
  public string Title { get; set; } = "";

  /// <summary>
  /// The S3 key the file is stored at, see <see cref="Services.Storage.StorageKeys"/>. Unique.
  /// </summary>
  public string StorageKey { get; set; } = "";

  /// <summary>
  /// The file's content type, e.g. <c>image/png</c>, <c>text/csv</c>, <c>application/json</c> or
  /// <c>application/octet-stream</c>.
  /// </summary>
  public string ContentType { get; set; } = "";

  /// <summary>Size of the file, in bytes.</summary>
  public long SizeBytes { get; set; }

  /// <summary>What produced the file: <c>"dataset"</c>, <c>"eda"</c>, <c>"run"</c>, <c>"evaluation"</c> or <c>"prediction"</c>.</summary>
  public string? SourceType { get; set; }

  /// <summary>The ID of the thing that produced the file: a dataset, run, evaluation or prediction ID.</summary>
  public Guid? SourceId { get; set; }

  /// <summary>When the artifact was recorded.</summary>
  public DateTimeOffset CreatedAt { get; set; }
}
