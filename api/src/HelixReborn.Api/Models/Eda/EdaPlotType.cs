namespace HelixReborn.Api.Models.Eda;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// The EDA plots the API can ask for, each an EDA service route (<c>Pca</c> is <c>/plots/pca</c>). Serialized by
/// name, camelCase (<c>"correlationHeatmap"</c>).
/// </summary>
[JsonConverter(typeof(EdaPlotTypeJsonConverter))]
public enum EdaPlotType
{
  /// <summary>A histogram of the target column, <c>/plots/target-distribution</c>.</summary>
  TargetDistribution,

  /// <summary>A correlation heatmap of numeric columns, <c>/plots/correlation-heatmap</c>.</summary>
  CorrelationHeatmap,

  /// <summary>A seaborn pairplot of numeric columns, <c>/plots/pairplot</c>.</summary>
  Pairplot,

  /// <summary>The first two principal components, <c>/plots/pca</c>.</summary>
  Pca,

  /// <summary>A 2D t-SNE embedding, <c>/plots/tsne</c>.</summary>
  Tsne,
}

/// <summary>Serializes <see cref="EdaPlotType"/> by name, camelCase.</summary>
public sealed class EdaPlotTypeJsonConverter() : JsonStringEnumConverter<EdaPlotType>(JsonNamingPolicy.CamelCase);
