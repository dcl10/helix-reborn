namespace HelixReborn.Api.Messaging;

using System.Text.Json;
using System.Text.Json.Nodes;

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
/// An evaluation job, API → evaluation worker on <c>evaluation.jobs</c>. Mirrors helix-core's <c>EvaluationJob</c>
/// (<c>contracts/messages/evaluation-job.schema.json</c>); snake_case on the wire via <see cref="JobMessageJson"/>.
/// </summary>
/// <param name="JobId">The job's ID, also the AMQP <c>message_id</c>.</param>
/// <param name="EvaluationId">The evaluation it runs.</param>
/// <param name="RunId">The run whose model is evaluated.</param>
/// <param name="ExperimentId">The experiment.</param>
/// <param name="DatasetKey">The dataset's S3 key.</param>
/// <param name="ModelPrefix">The run's prefix, where the model is: <c>experiments/{e}/runs/{r}/</c>.</param>
/// <param name="OutputPrefix">The S3 prefix the worker writes to: <c>experiments/{e}/evaluations/{v}/</c>.</param>
/// <param name="Options">The evaluation options, defaults filled in.</param>
public record EvaluationJob(
  Guid JobId,
  Guid EvaluationId,
  Guid RunId,
  Guid ExperimentId,
  string DatasetKey,
  string ModelPrefix,
  string OutputPrefix,
  EvaluationJobOptions Options)
{
  /// <summary>Always 1.</summary>
  public int SchemaVersion { get; init; } = 1;
}

/// <summary>
/// What the evaluation worker computes and plots. Mirrors helix-core's <c>EvaluationOptions</c>; every property
/// defaults to the Python default, so deserializing a partial object fills in the rest.
/// </summary>
public sealed record EvaluationJobOptions
{
  /// <summary>Whether to render the performance plots.</summary>
  public bool Plots { get; init; } = true;

  /// <summary>Global importance methods: <c>permutation</c>, <c>shap</c>.</summary>
  public IReadOnlyList<string> GlobalMethods { get; init; } = ["permutation", "shap"];

  /// <summary>Permutation importance repeats, 1–100.</summary>
  public int PermutationRepeats { get; init; } = 10;

  /// <summary>Local importance methods: <c>lime</c>, <c>shap</c>.</summary>
  public IReadOnlyList<string> LocalMethods { get; init; } = [];

  /// <summary>How many test rows local methods explain, 1–1000.</summary>
  public int LocalMaxSamples { get; init; } = 50;

  /// <summary>Ensemble methods: <c>mean</c>, <c>majority_vote</c>.</summary>
  public IReadOnlyList<string> EnsembleMethods { get; init; } = [];

  /// <summary>How many top features importance plots show, 1–50.</summary>
  public int NumFeaturesToPlot { get; init; } = 10;

  /// <summary>Fuzzy interpretation options, or null to skip it.</summary>
  public FuzzyJobOptions? Fuzzy { get; init; }

  /// <summary>Plot styling (helix-core's <c>PlotOptions</c>, passed through as is), or null for the defaults.</summary>
  public JsonObject? PlotOptions { get; init; }
}

/// <summary>Fuzzy interpretation options. Mirrors helix-core's <c>FuzzyOptions</c>, with its defaults.</summary>
public sealed record FuzzyJobOptions
{
  /// <summary>How many top features the rules use.</summary>
  public int NumberFuzzyFeatures { get; init; } = 10;

  /// <summary>Whether to build (and plot) membership functions for the features.</summary>
  public bool GranularFeatures { get; init; } = true;

  /// <summary>How many target clusters (regression), 2–10.</summary>
  public int NumberClusters { get; init; } = 5;

  /// <summary>The clusters' names, lowest first; exactly <see cref="NumberClusters"/> of them.</summary>
  public IReadOnlyList<string> ClusterNames { get; init; } = ["very low", "low", "medium", "high", "very high"];

  /// <summary>How many rules per context.</summary>
  public int NumberRules { get; init; } = 5;
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
