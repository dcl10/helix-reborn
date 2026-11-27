namespace HelixReborn.Api.Models;

using System.Text.Json.Nodes;

/// <summary>The body of a request to evaluate a run's model.</summary>
/// <param name="Options">
/// The evaluation options, snake_case as in <c>EvaluationJob.options</c>
/// (<c>contracts/messages/evaluation-job.schema.json</c>). Missing fields, or a missing object, take the defaults.
/// </param>
public record CreateEvaluationRequest(JsonObject? Options);
