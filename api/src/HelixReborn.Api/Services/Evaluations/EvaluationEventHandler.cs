namespace HelixReborn.Api.Services.Evaluations;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Artifacts;
using Data;
using Messaging;
using Microsoft.EntityFrameworkCore;
using Models;
using Storage;

/// <summary>
/// Applies evaluation job events to their evaluation. Idempotent: events for an evaluation already <c>Succeeded</c>
/// or <c>Failed</c>, for an unknown evaluation, or for another job are ignored, and on <c>succeeded</c> each file in
/// the result's <c>artifacts</c> is recorded only if it isn't already, so a redelivered event changes nothing.
/// </summary>
/// <param name="db">The app database.</param>
/// <param name="artifacts">Records the evaluation's files.</param>
/// <param name="storage">Object storage, for the files' sizes.</param>
/// <param name="time">The clock.</param>
/// <param name="logger">Logs ignored events and files.</param>
[SuppressMessage(
  "Naming",
  "CA1711:Identifiers should not have incorrect suffix",
  Justification = "Handles job events from RabbitMQ, not a .NET event delegate; the name is the one issue #34 uses.")]
public partial class EvaluationEventHandler(
  AppDbContext db,
  IArtifactService artifacts,
  IObjectStorage storage,
  TimeProvider time,
  ILogger<EvaluationEventHandler> logger) : IJobEventHandler
{
  /// <summary>The <see cref="Artifact.SourceType"/> of an evaluation's files.</summary>
  public const string SourceType = "evaluation";

  /// <inheritdoc />
  public string JobType => "evaluation";

  /// <inheritdoc />
  public async Task HandleAsync(JobEvent jobEvent, CancellationToken cancellationToken)
  {
    var evaluation = await db.Evaluations.SingleOrDefaultAsync(x => x.Id == jobEvent.EntityId, cancellationToken);
    if (evaluation is null)
    {
      LogUnknownEvaluation(logger, jobEvent.Status, jobEvent.EntityId);
      return;
    }

    if (evaluation.JobId != jobEvent.JobId)
    {
      LogOtherJob(logger, jobEvent.Status, jobEvent.JobId, evaluation.Id);
      return;
    }

    if (evaluation.IsTerminal)
    {
      LogStale(logger, jobEvent.Status, evaluation.Id, evaluation.Status);
      return;
    }

    var now = time.GetUtcNow();
    switch (jobEvent.Status)
    {
      case "started":
        evaluation.Status = EvaluationStatus.Running;
        evaluation.StartedAt ??= now;
        break;

      case "progress":
        // A progress event that overtook "started" still means it's running.
        evaluation.Status = EvaluationStatus.Running;
        evaluation.StartedAt ??= now;
        evaluation.Progress = jobEvent.Progress ?? evaluation.Progress;
        evaluation.Message = jobEvent.Message ?? evaluation.Message;
        break;

      case "succeeded":
        // Record the files before it turns terminal: if this fails, the redelivered event finishes the job.
        if (jobEvent.Result is { ValueKind: JsonValueKind.Object } result)
        {
          await RecordArtifactsAsync(evaluation, result, cancellationToken);
          if (result.TryGetProperty("metrics", out var metrics) && metrics.ValueKind == JsonValueKind.Object)
          {
            evaluation.MetricsJson = metrics.GetRawText();
          }
        }

        evaluation.Status = EvaluationStatus.Succeeded;
        evaluation.Progress = 1;
        evaluation.Message = jobEvent.Message ?? evaluation.Message;
        evaluation.StartedAt ??= now;
        evaluation.FinishedAt = now;
        break;

      case "failed":
        evaluation.Status = EvaluationStatus.Failed;
        evaluation.ErrorType = jobEvent.Error?.Type ?? "Unknown";
        evaluation.ErrorMessage =
          jobEvent.Error?.Message ?? "The evaluation worker reported a failure without details.";
        evaluation.FinishedAt = now;
        break;

      default:
        LogUnknownStatus(logger, jobEvent.Status, evaluation.Id);
        return;
    }

    await db.SaveChangesAsync(cancellationToken);
  }

  /// <summary>The artifact kind for a result entry's <c>kind</c>, or null if it isn't one.</summary>
  /// <param name="kind">The entry's kind: <c>plot</c>, <c>table</c> or <c>json</c>.</param>
  /// <returns>The artifact kind.</returns>
  public static ArtifactKind? KindOf(string? kind) => kind switch
  {
    "plot" => ArtifactKind.EvaluationPlot,
    "table" => ArtifactKind.EvaluationTable,
    "json" => ArtifactKind.Metrics,
    _ => null,
  };

  /// <summary>A file's content type, by its key's extension.</summary>
  /// <param name="key">The S3 key.</param>
  /// <returns><c>image/png</c>, <c>text/csv</c>, <c>application/json</c> or <c>application/octet-stream</c>.</returns>
  public static string ContentTypeOf(string key) => Path.GetExtension(key).ToLowerInvariant() switch
  {
    ".png" => "image/png",
    ".csv" => "text/csv",
    ".json" => "application/json",
    _ => "application/octet-stream",
  };

  private async Task RecordArtifactsAsync(Evaluation evaluation, JsonElement result, CancellationToken cancellationToken)
  {
    if (!result.TryGetProperty("artifacts", out var entries) || entries.ValueKind != JsonValueKind.Array)
    {
      return;
    }

    var prefix = StorageKeys.EvaluationPrefix(evaluation.ExperimentId, evaluation.Id);
    var sizes = (await storage.ListAsync(prefix, cancellationToken)).ToDictionary(x => x.Key, x => x.Size);
    var recorded = (await artifacts.ListAsync(evaluation.ExperimentId, SourceType, evaluation.Id, cancellationToken))
      .Select(x => x.StorageKey)
      .ToHashSet(StringComparer.Ordinal);

    foreach (var entry in entries.EnumerateArray())
    {
      var key = StringProperty(entry, "key");
      var kind = KindOf(StringProperty(entry, "kind"));
      if (key is null || kind is null)
      {
        LogBadEntry(logger, evaluation.Id);
        continue;
      }

      if (!recorded.Add(key))
      {
        continue;
      }

      if (!sizes.TryGetValue(key, out var size))
      {
        LogMissingFile(logger, key, evaluation.Id);
        continue;
      }

      var title = StringProperty(entry, "title") ?? Path.GetFileNameWithoutExtension(key);
      await artifacts.RecordAsync(
        evaluation.ExperimentId,
        kind.Value,
        title,
        key,
        ContentTypeOf(key),
        size,
        SourceType,
        evaluation.Id,
        cancellationToken);
    }
  }

  private static string? StringProperty(JsonElement element, string name) =>
    element.ValueKind == JsonValueKind.Object
    && element.TryGetProperty(name, out var value)
    && value.ValueKind == JsonValueKind.String
      ? value.GetString()
      : null;

  [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring a {Status} event for unknown evaluation {EvaluationId}")]
  private static partial void LogUnknownEvaluation(ILogger logger, string status, Guid evaluationId);

  [LoggerMessage(
    Level = LogLevel.Warning,
    Message = "Ignoring a {Status} event for job {JobId}, which isn't evaluation {EvaluationId}'s job")]
  private static partial void LogOtherJob(ILogger logger, string status, Guid jobId, Guid evaluationId);

  [LoggerMessage(
    Level = LogLevel.Information,
    Message = "Ignoring a stale {Status} event for evaluation {EvaluationId}, already {EvaluationStatus}")]
  private static partial void LogStale(ILogger logger, string status, Guid evaluationId, EvaluationStatus evaluationStatus);

  [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring an event with unknown status {Status} for evaluation {EvaluationId}")]
  private static partial void LogUnknownStatus(ILogger logger, string status, Guid evaluationId);

  [LoggerMessage(Level = LogLevel.Warning, Message = "Evaluation {EvaluationId} reported an artifact without a key or known kind; skipping it")]
  private static partial void LogBadEntry(ILogger logger, Guid evaluationId);

  [LoggerMessage(Level = LogLevel.Warning, Message = "Evaluation {EvaluationId} succeeded but {Key} isn't in storage; not recording it")]
  private static partial void LogMissingFile(ILogger logger, string key, Guid evaluationId);
}
