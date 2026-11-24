namespace HelixReborn.Api.Models;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// What kind of file an <see cref="Artifact"/> is. Serialized by name, camelCase (<c>"evaluationPlot"</c>), and
/// stored as a string in the database.
/// </summary>
[JsonConverter(typeof(ArtifactKindJsonConverter))]
public enum ArtifactKind
{
  /// <summary>The experiment's uploaded CSV.</summary>
  Dataset,

  /// <summary>An EDA plot image.</summary>
  EdaPlot,

  /// <summary>An EDA table.</summary>
  EdaTable,

  /// <summary>A trained pipeline, exported to ONNX.</summary>
  Model,

  /// <summary>A run's <c>metadata.json</c>.</summary>
  ModelMetadata,

  /// <summary>A run's <c>split.json</c>.</summary>
  Split,

  /// <summary>A run's <c>cv_metrics.json</c>.</summary>
  CvMetrics,

  /// <summary>An evaluation's <c>metrics.json</c>.</summary>
  Metrics,

  /// <summary>An evaluation plot image.</summary>
  EvaluationPlot,

  /// <summary>An evaluation table.</summary>
  EvaluationTable,

  /// <summary>A prediction run's output CSV.</summary>
  Predictions,
}

/// <summary>Serializes <see cref="ArtifactKind"/> by name, camelCase.</summary>
public sealed class ArtifactKindJsonConverter() : JsonStringEnumConverter<ArtifactKind>(JsonNamingPolicy.CamelCase);
