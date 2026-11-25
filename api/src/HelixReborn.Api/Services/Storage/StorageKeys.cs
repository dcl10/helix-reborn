namespace HelixReborn.Api.Services.Storage;

/// <summary>
/// S3 key layout, shared with the Python services (see <c>docs/architecture.md</c>, "Storage layout"). IDs are
/// formatted lowercase with hyphens (<c>"D"</c>). Only the dataset key is built here: EDA, run, evaluation and
/// prediction keys are built by the Python services that write those files and recorded as
/// <see cref="Models.Artifact"/>s through <see cref="Artifacts.IArtifactService.RecordAsync"/>, but follow the
/// same layout.
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
}
