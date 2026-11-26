namespace HelixReborn.Api.Models;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Where a training run is. Serialized by name, camelCase (<c>"queued"</c>), and stored as a string. <see cref="Succeeded"/>
/// and <see cref="Failed"/> are terminal: later events for the run are ignored.
/// </summary>
[JsonConverter(typeof(RunStatusJsonConverter))]
public enum RunStatus
{
  /// <summary>The job is published and waiting for a training worker.</summary>
  Queued,

  /// <summary>A worker has started it.</summary>
  Running,

  /// <summary>Trained; the model and its files are recorded as artifacts.</summary>
  Succeeded,

  /// <summary>The worker reported an error, or the job couldn't be queued.</summary>
  Failed,
}

/// <summary>Serializes <see cref="RunStatus"/> by name, camelCase.</summary>
public sealed class RunStatusJsonConverter() : JsonStringEnumConverter<RunStatus>(JsonNamingPolicy.CamelCase);
