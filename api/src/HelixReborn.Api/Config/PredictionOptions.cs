namespace HelixReborn.Api.Config;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Limits for predictions, which run synchronously inside an API request: the whole upload and its feature matrix are
/// held in memory, so these bound one request's memory and time.
/// </summary>
public class PredictionOptions
{
  /// <summary>Configuration section these options bind to.</summary>
  public const string SectionName = "Predictions";

  /// <summary>
  /// The largest upload allowed, in bytes (the whole multipart request). Default 10 MB; larger is a 413.
  /// </summary>
  [Range(1, long.MaxValue)]
  public long MaxUploadBytes { get; init; } = 10 * 1024 * 1024;

  /// <summary>The most data rows one upload may have. Default 100,000; more is a 400.</summary>
  [Range(1, int.MaxValue)]
  public int MaxRows { get; init; } = 100_000;

  /// <summary>How many runs' models are kept loaded; the least recently used is evicted. Default 10.</summary>
  [Range(1, 1000)]
  public int ModelCacheSize { get; init; } = 10;
}
