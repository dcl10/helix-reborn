namespace HelixReborn.Api.Services.Storage;

/// <summary>
/// S3 key layout, shared with the Python services (see <c>docs/architecture.md</c>, "Storage layout"). IDs are
/// formatted lowercase with hyphens (<c>"D"</c>). The dataset, EDA and prediction keys are built here (the API tells
/// EDA where to write, and writes predictions itself); the files under a run's or evaluation's prefix are named by the
/// Python workers that write them and recorded as <see cref="Models.Artifact"/>s, but follow the same layout.
/// </summary>
public static class StorageKeys
{
  /// <summary>The prefix holding every object under one experiment.</summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <returns><c>experiments/{experimentId}/</c></returns>
  public static string Experiment(Guid experimentId) => $"experiments/{experimentId:D}/";

  /// <summary>The key an experiment's dataset file is stored at.</summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="datasetId">The dataset's ID.</param>
  /// <param name="fileName">The sanitised file name.</param>
  /// <returns><c>experiments/{experimentId}/datasets/{datasetId}/{fileName}</c></returns>
  public static string Dataset(Guid experimentId, Guid datasetId, string fileName) =>
    $"{Experiment(experimentId)}datasets/{datasetId:D}/{fileName}";

  /// <summary>The key an EDA plot or table is stored at.</summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="artifactId">The artifact's ID.</param>
  /// <param name="extension">The file extension, without the dot: <c>png</c> or <c>csv</c>.</param>
  /// <returns><c>experiments/{experimentId}/eda/{artifactId}.{extension}</c></returns>
  public static string Eda(Guid experimentId, Guid artifactId, string extension) =>
    $"{Experiment(experimentId)}eda/{artifactId:D}.{extension}";

  /// <summary>The prefix a training run's worker writes its model and files under.</summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="runId">The run's ID.</param>
  /// <returns><c>experiments/{experimentId}/runs/{runId}/</c></returns>
  public static string RunPrefix(Guid experimentId, Guid runId) => $"{Experiment(experimentId)}runs/{runId:D}/";

  /// <summary>The prefix an evaluation's worker writes its metrics, plots and tables under.</summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="evaluationId">The evaluation's ID.</param>
  /// <returns><c>experiments/{experimentId}/evaluations/{evaluationId}/</c></returns>
  public static string EvaluationPrefix(Guid experimentId, Guid evaluationId) =>
    $"{Experiment(experimentId)}evaluations/{evaluationId:D}/";

  /// <summary>The key a prediction's output CSV is stored at. The API writes it (predictions run in the API).</summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="predictionId">The prediction's ID.</param>
  /// <returns><c>experiments/{experimentId}/predictions/{predictionId}/predictions.csv</c></returns>
  public static string Prediction(Guid experimentId, Guid predictionId) =>
    $"{Experiment(experimentId)}predictions/{predictionId:D}/predictions.csv";
}
