namespace HelixReborn.Api.Models;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Where an evaluation is. Serialized by name, camelCase (<c>"queued"</c>), and stored as a string.
/// <see cref="Succeeded"/> and <see cref="Failed"/> are terminal: later events for the evaluation are ignored.
/// </summary>
[JsonConverter(typeof(EvaluationStatusJsonConverter))]
public enum EvaluationStatus
{
  /// <summary>The job is published and waiting for an evaluation worker.</summary>
  Queued,

  /// <summary>A worker has started it.</summary>
  Running,

  /// <summary>Evaluated; the metrics are stored and the files recorded as artifacts.</summary>
  Succeeded,

  /// <summary>The worker reported an error, or the job couldn't be queued.</summary>
  Failed,
}

/// <summary>Serializes <see cref="EvaluationStatus"/> by name, camelCase.</summary>
public sealed class EvaluationStatusJsonConverter() : JsonStringEnumConverter<EvaluationStatus>(JsonNamingPolicy.CamelCase);
