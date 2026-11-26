namespace HelixReborn.Api.Services.Runs;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Artifacts;
using Data;
using Messaging;
using Microsoft.EntityFrameworkCore;
using Models;
using Storage;

/// <summary>
/// Applies training job events to their run. Idempotent: events for a run already <c>Succeeded</c> or <c>Failed</c>,
/// for an unknown run, or for another job of the run are ignored, and on <c>succeeded</c> the model files are recorded
/// as artifacts only if they aren't already, so a redelivered event changes nothing.
/// </summary>
/// <param name="db">The app database.</param>
/// <param name="artifacts">Records the run's model files.</param>
/// <param name="storage">Object storage, for the files' sizes.</param>
/// <param name="time">The clock.</param>
/// <param name="logger">Logs ignored events.</param>
[SuppressMessage(
  "Naming",
  "CA1711:Identifiers should not have incorrect suffix",
  Justification = "Handles job events from RabbitMQ, not a .NET event delegate; the name is the one issue #25 uses.")]
public partial class RunEventHandler(
  AppDbContext db,
  IArtifactService artifacts,
  IObjectStorage storage,
  TimeProvider time,
  ILogger<RunEventHandler> logger) : IJobEventHandler
{
  /// <summary>The <see cref="Artifact.SourceType"/> of a run's files.</summary>
  public const string SourceType = "run";

  /// <summary>The run's files, by the <c>succeeded</c> result's key: kind, title, content type.</summary>
  private static readonly (string ResultKey, ArtifactKind Kind, string Title, string ContentType)[] Outputs =
  [
    ("model_key", ArtifactKind.Model, "Model (ONNX)", "application/octet-stream"),
    ("metadata_key", ArtifactKind.ModelMetadata, "Model metadata", "application/json"),
    ("split_key", ArtifactKind.Split, "Train/test split", "application/json"),
    ("cv_metrics_key", ArtifactKind.CvMetrics, "Cross-validation metrics", "application/json"),
  ];

  /// <inheritdoc />
  public string JobType => "training";

  /// <inheritdoc />
  public async Task HandleAsync(JobEvent jobEvent, CancellationToken cancellationToken)
  {
    var run = await db.Runs.SingleOrDefaultAsync(x => x.Id == jobEvent.EntityId, cancellationToken);
    if (run is null)
    {
      LogUnknownRun(logger, jobEvent.Status, jobEvent.EntityId);
      return;
    }

    if (run.JobId != jobEvent.JobId)
    {
      LogOtherJob(logger, jobEvent.Status, jobEvent.JobId, run.Id);
      return;
    }

    if (run.IsTerminal)
    {
      LogStale(logger, jobEvent.Status, run.Id, run.Status);
      return;
    }

    var now = time.GetUtcNow();
    switch (jobEvent.Status)
    {
      case "started":
        run.Status = RunStatus.Running;
        run.StartedAt ??= now;
        break;

      case "progress":
        // A progress event that overtook "started" still means the run is running.
        run.Status = RunStatus.Running;
        run.StartedAt ??= now;
        run.Progress = jobEvent.Progress ?? run.Progress;
        run.Message = jobEvent.Message ?? run.Message;
        break;

      case "succeeded":
        // Record the files before the run turns terminal: if this fails, the redelivered event finishes the job.
        if (jobEvent.Result is { ValueKind: JsonValueKind.Object } result)
        {
          await RecordArtifactsAsync(run, result, cancellationToken);
          run.ResultJson = result.GetRawText();
        }

        run.Status = RunStatus.Succeeded;
        run.Progress = 1;
        run.Message = jobEvent.Message ?? run.Message;
        run.StartedAt ??= now;
        run.FinishedAt = now;
        break;

      case "failed":
        run.Status = RunStatus.Failed;
        run.ErrorType = jobEvent.Error?.Type ?? "Unknown";
        run.ErrorMessage = jobEvent.Error?.Message ?? "The training worker reported a failure without details.";
        run.FinishedAt = now;
        break;

      default:
        LogUnknownStatus(logger, jobEvent.Status, run.Id);
        return;
    }

    await db.SaveChangesAsync(cancellationToken);
  }

  private async Task RecordArtifactsAsync(Run run, JsonElement result, CancellationToken cancellationToken)
  {
    var prefix = StorageKeys.RunPrefix(run.ExperimentId, run.Id);
    var sizes = (await storage.ListAsync(prefix, cancellationToken)).ToDictionary(x => x.Key, x => x.Size);
    var recorded = (await artifacts.ListAsync(run.ExperimentId, SourceType, run.Id, cancellationToken))
      .Select(x => x.StorageKey)
      .ToHashSet(StringComparer.Ordinal);

    foreach (var (resultKey, kind, title, contentType) in Outputs)
    {
      if (!result.TryGetProperty(resultKey, out var keyElement) || keyElement.ValueKind != JsonValueKind.String)
      {
        continue;
      }

      var key = keyElement.GetString()!;
      if (recorded.Contains(key))
      {
        continue;
      }

      if (!sizes.TryGetValue(key, out var size))
      {
        LogMissingFile(logger, key, run.Id);
        continue;
      }

      await artifacts.RecordAsync(run.ExperimentId, kind, title, key, contentType, size, SourceType, run.Id, cancellationToken);
    }
  }

  [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring a {Status} event for unknown run {RunId}")]
  private static partial void LogUnknownRun(ILogger logger, string status, Guid runId);

  [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring a {Status} event for job {JobId}, which isn't run {RunId}'s job")]
  private static partial void LogOtherJob(ILogger logger, string status, Guid jobId, Guid runId);

  [LoggerMessage(Level = LogLevel.Information, Message = "Ignoring a stale {Status} event for run {RunId}, already {RunStatus}")]
  private static partial void LogStale(ILogger logger, string status, Guid runId, RunStatus runStatus);

  [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring an event with unknown status {Status} for run {RunId}")]
  private static partial void LogUnknownStatus(ILogger logger, string status, Guid runId);

  [LoggerMessage(Level = LogLevel.Warning, Message = "Run {RunId} succeeded but {Key} isn't in storage; not recording it")]
  private static partial void LogMissingFile(ILogger logger, string key, Guid runId);
}
