namespace HelixReborn.Api.Messaging;

using System.Text.Json;

/// <summary>
/// A training job, API → training worker on <c>training.jobs</c>. Mirrors helix-core's <c>TrainingJob</c>
/// (<c>contracts/messages/training-job.schema.json</c>); snake_case on the wire via <see cref="JobMessageJson"/>.
/// </summary>
/// <param name="JobId">The job's ID, also the AMQP <c>message_id</c>.</param>
/// <param name="RunId">The run it trains.</param>
/// <param name="ExperimentId">The experiment.</param>
/// <param name="DatasetKey">The dataset's S3 key.</param>
/// <param name="OutputPrefix">The S3 prefix the worker writes to: <c>experiments/{e}/runs/{r}/</c>.</param>
/// <param name="Pipeline">The run's <c>PipelineSpec</c>, the stored snake_case JSON as is.</param>
public record TrainingJob(
  Guid JobId,
  Guid RunId,
  Guid ExperimentId,
  string DatasetKey,
  string OutputPrefix,
  JsonElement Pipeline)
{
  /// <summary>Always 1.</summary>
  public int SchemaVersion { get; init; } = 1;
}

/// <summary>
/// An evaluation job, API → evaluation worker on <c>evaluation.jobs</c>. Shape only, for #34; mirrors helix-core's
/// <c>EvaluationJob</c>.
/// </summary>
/// <param name="JobId">The job's ID.</param>
/// <param name="EvaluationId">The evaluation it runs.</param>
/// <param name="RunId">The run whose model is evaluated.</param>
/// <param name="ExperimentId">The experiment.</param>
/// <param name="DatasetKey">The dataset's S3 key.</param>
/// <param name="ModelPrefix">The run's prefix, where the model is.</param>
/// <param name="OutputPrefix">The S3 prefix the worker writes to.</param>
/// <param name="Options">The evaluation options.</param>
public record EvaluationJob(
  Guid JobId,
  Guid EvaluationId,
  Guid RunId,
  Guid ExperimentId,
  string DatasetKey,
  string ModelPrefix,
  string OutputPrefix,
  JsonElement Options)
{
  /// <summary>Always 1.</summary>
  public int SchemaVersion { get; init; } = 1;
}

/// <summary>
/// An event from a worker on <c>jobs.events</c>. Mirrors helix-core's <c>JobEvent</c>
/// (<c>contracts/messages/job-event.schema.json</c>).
/// </summary>
/// <param name="SchemaVersion">Always 1.</param>
/// <param name="JobId">The job it's about.</param>
/// <param name="JobType"><c>training</c> or <c>evaluation</c>.</param>
/// <param name="EntityId">The run ID (training) or evaluation ID (evaluation).</param>
/// <param name="Status"><c>started</c>, <c>progress</c>, <c>succeeded</c> or <c>failed</c>.</param>
/// <param name="Progress">0–1, or null.</param>
/// <param name="Message">A short status message, or null.</param>
/// <param name="Result">The job's result, only on <c>succeeded</c>.</param>
/// <param name="Error">The error, only on <c>failed</c>.</param>
/// <param name="Timestamp">When the worker published it.</param>
public record JobEvent(
  int SchemaVersion,
  Guid JobId,
  string JobType,
  Guid EntityId,
  string Status,
  double? Progress,
  string? Message,
  JsonElement? Result,
  JobError? Error,
  DateTimeOffset Timestamp);

/// <summary>A failed job's error.</summary>
/// <param name="Type">The exception type, e.g. <c>InvalidDataError</c>.</param>
/// <param name="Message">What went wrong.</param>
public record JobError(string Type, string Message);

/// <summary>Serialization of job messages: snake_case, as the Python side reads and writes them.</summary>
public static class JobMessageJson
{
  /// <summary>snake_case property names.</summary>
  public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
}
