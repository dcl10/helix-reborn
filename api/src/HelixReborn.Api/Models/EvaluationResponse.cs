namespace HelixReborn.Api.Models;

using System.Text.Json.Nodes;

/// <summary>
/// An evaluation as the API returns it. <see cref="Options"/> is snake_case, as sent to the worker; <see cref="Metrics"/>
/// is the worker's test-set metrics as reported.
/// </summary>
/// <param name="Id">Evaluation ID.</param>
/// <param name="RunId">The run whose model it evaluates.</param>
/// <param name="PipelineName">The run's pipeline name.</param>
/// <param name="Status">Where the evaluation is.</param>
/// <param name="Progress">The latest progress, 0–1.</param>
/// <param name="Message">The latest progress message.</param>
/// <param name="Options">The options it ran with, defaults filled in.</param>
/// <param name="Metrics">The test-set metrics once it succeeds.</param>
/// <param name="Error">The error once it fails.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="StartedAt">When a worker started it.</param>
/// <param name="FinishedAt">When it succeeded or failed.</param>
public record EvaluationResponse(
  Guid Id,
  Guid RunId,
  string PipelineName,
  EvaluationStatus Status,
  double? Progress,
  string? Message,
  JsonObject Options,
  JsonObject? Metrics,
  EvaluationError? Error,
  DateTimeOffset CreatedAt,
  DateTimeOffset? StartedAt,
  DateTimeOffset? FinishedAt)
{
  /// <summary>Builds the response for an evaluation.</summary>
  /// <param name="evaluation">The evaluation.</param>
  /// <param name="pipelineName">Its run's pipeline name.</param>
  /// <returns>The response.</returns>
  public static EvaluationResponse From(Evaluation evaluation, string pipelineName) => new(
    evaluation.Id,
    evaluation.RunId,
    pipelineName,
    evaluation.Status,
    evaluation.Progress,
    evaluation.Message,
    JsonNode.Parse(evaluation.OptionsJson)!.AsObject(),
    evaluation.MetricsJson is null ? null : JsonNode.Parse(evaluation.MetricsJson)?.AsObject(),
    evaluation.ErrorType is null ? null : new EvaluationError(evaluation.ErrorType, evaluation.ErrorMessage ?? ""),
    evaluation.CreatedAt,
    evaluation.StartedAt,
    evaluation.FinishedAt);
}

/// <summary>Why an evaluation failed.</summary>
/// <param name="Type">The error's type, e.g. <c>InvalidSpecError</c> or <c>QueueUnavailable</c>.</param>
/// <param name="Message">What went wrong.</param>
public record EvaluationError(string Type, string Message);
