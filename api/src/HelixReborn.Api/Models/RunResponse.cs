namespace HelixReborn.Api.Models;

using System.Text.Json.Nodes;

/// <summary>A run as the API returns it. <see cref="Spec"/> and <see cref="Result"/> are snake_case, as stored.</summary>
/// <param name="Id">Run ID.</param>
/// <param name="PipelineId">The pipeline it ran, or null once that pipeline is deleted.</param>
/// <param name="PipelineName">The pipeline's name when the run was created.</param>
/// <param name="Status">Where the run is.</param>
/// <param name="Progress">The latest progress, 0–1.</param>
/// <param name="Message">The latest progress message.</param>
/// <param name="Result">The training result once it succeeds: model and file keys, CV metrics, best params, row counts.</param>
/// <param name="Error">The error once it fails.</param>
/// <param name="Spec">The pipeline spec it ran.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="StartedAt">When a worker started it.</param>
/// <param name="FinishedAt">When it succeeded or failed.</param>
public record RunResponse(
  Guid Id,
  Guid? PipelineId,
  string PipelineName,
  RunStatus Status,
  double? Progress,
  string? Message,
  JsonObject? Result,
  RunError? Error,
  JsonObject Spec,
  DateTimeOffset CreatedAt,
  DateTimeOffset? StartedAt,
  DateTimeOffset? FinishedAt)
{
  /// <summary>Builds the response for a run.</summary>
  /// <param name="run">The run.</param>
  /// <returns>The response.</returns>
  public static RunResponse From(Run run) => new(
    run.Id,
    run.PipelineId,
    run.PipelineName,
    run.Status,
    run.Progress,
    run.Message,
    run.ResultJson is null ? null : JsonNode.Parse(run.ResultJson)?.AsObject(),
    run.ErrorType is null ? null : new RunError(run.ErrorType, run.ErrorMessage ?? ""),
    JsonNode.Parse(run.SpecJson)!.AsObject(),
    run.CreatedAt,
    run.StartedAt,
    run.FinishedAt);
}

/// <summary>Why a run failed.</summary>
/// <param name="Type">The error's type, e.g. <c>InvalidDataError</c>, <c>OnnxParityError</c> or <c>QueueUnavailable</c>.</param>
/// <param name="Message">What went wrong.</param>
public record RunError(string Type, string Message);
