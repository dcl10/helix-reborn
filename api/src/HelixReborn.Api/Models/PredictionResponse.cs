namespace HelixReborn.Api.Models;

/// <summary>The result of predicting on an uploaded CSV.</summary>
/// <param name="PredictionId">The prediction's ID; the artifact's <c>sourceId</c>.</param>
/// <param name="RunId">The run whose model made the predictions.</param>
/// <param name="RowCount">Number of rows predicted.</param>
/// <param name="ArtifactId">The output CSV's artifact, to download it.</param>
/// <param name="Columns">
/// The output CSV's columns: the ID column (if the model has one and the upload has it), <c>prediction</c>, then for
/// classification <c>probability_&lt;class&gt;</c> per class.
/// </param>
/// <param name="Preview">
/// The first 20 output rows, keyed by column: the ID and a class label as strings; a regression value and the
/// probabilities as numbers (null if not finite).
/// </param>
public record PredictionResponse(
  Guid PredictionId,
  Guid RunId,
  int RowCount,
  Guid ArtifactId,
  IReadOnlyList<string> Columns,
  IReadOnlyList<IReadOnlyDictionary<string, object?>> Preview);
