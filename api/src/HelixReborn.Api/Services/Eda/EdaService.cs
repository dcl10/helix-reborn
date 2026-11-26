namespace HelixReborn.Api.Services.Eda;

using System.Text.Json;
using System.Text.Json.Nodes;
using Artifacts;
using Data;
using Experiments;
using Microsoft.EntityFrameworkCore;
using Models;
using Models.Eda;
using Storage;

/// <summary><see cref="IEdaService"/> over the EDA service, the app database and the artifact registry.</summary>
/// <param name="db">The app database, for the dataset's storage key.</param>
/// <param name="experiments">Checks the experiment is visible to, or owned by, the caller.</param>
/// <param name="artifacts">Records the plots and tables EDA writes.</param>
/// <param name="eda">The EDA service client.</param>
public class EdaService(
  AppDbContext db,
  IExperimentService experiments,
  IArtifactService artifacts,
  IEdaClient eda) : IEdaService
{
  /// <summary>The <see cref="Artifact.SourceType"/> of every EDA artifact.</summary>
  public const string SourceType = "eda";

  /// <inheritdoc />
  public async Task<JsonElement> GetSummaryAsync(
    Guid experimentId,
    string userId,
    bool isAdmin,
    CancellationToken cancellationToken = default)
  {
    await experiments.GetAsync(experimentId, userId, isAdmin, cancellationToken);
    var datasetKey = await DatasetKey(experimentId, cancellationToken);

    return await CallEda(experimentId, () => eda.GetSummaryAsync(datasetKey, cancellationToken));
  }

  /// <inheritdoc />
  public async Task<ArtifactResponse> CreatePlotAsync(
    Guid experimentId,
    string userId,
    EdaPlotRequest request,
    CancellationToken cancellationToken = default)
  {
    var type = request.Type ?? throw new ArgumentException("A plot type is required.", nameof(request));

    // Owner only: an admin's write on someone else's experiment is a 404, as elsewhere.
    await experiments.GetAsync(experimentId, userId, isAdmin: false, cancellationToken);
    var datasetKey = await DatasetKey(experimentId, cancellationToken);

    var options = request.Options?.DeepClone().AsObject() ?? [];
    if (request.PlotOptions is not null)
    {
      options["plot_options"] = request.PlotOptions.DeepClone();
    }

    var artifactId = Guid.NewGuid();
    var outputKey = StorageKeys.Eda(experimentId, artifactId, "png");
    var result = await CallEda(
      experimentId,
      () => eda.CreatePlotAsync(type, datasetKey, outputKey, options, cancellationToken));

    var artifact = await artifacts.RecordAsync(
      artifactId,
      experimentId,
      ArtifactKind.EdaPlot,
      result.Title,
      outputKey,
      result.ContentType,
      result.SizeBytes,
      SourceType,
      null,
      cancellationToken);

    return ArtifactResponse.From(artifact);
  }

  /// <inheritdoc />
  public async Task<NormalityResponse> CreateNormalityTableAsync(
    Guid experimentId,
    string userId,
    IReadOnlyList<string>? columns,
    CancellationToken cancellationToken = default)
  {
    await experiments.GetAsync(experimentId, userId, isAdmin: false, cancellationToken);
    var datasetKey = await DatasetKey(experimentId, cancellationToken);

    var artifactId = Guid.NewGuid();
    var outputKey = StorageKeys.Eda(experimentId, artifactId, "csv");
    var result = await CallEda(
      experimentId,
      () => eda.CreateNormalityTableAsync(datasetKey, outputKey, columns, cancellationToken));

    var artifact = await artifacts.RecordAsync(
      artifactId,
      experimentId,
      ArtifactKind.EdaTable,
      result.Title,
      outputKey,
      result.ContentType,
      result.SizeBytes,
      SourceType,
      null,
      cancellationToken);

    return new NormalityResponse(ArtifactResponse.From(artifact), result.Rows);
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<ArtifactResponse>> ListArtifactsAsync(
    Guid experimentId,
    string userId,
    bool isAdmin,
    CancellationToken cancellationToken = default)
  {
    await experiments.GetAsync(experimentId, userId, isAdmin, cancellationToken);
    var list = await artifacts.ListAsync(experimentId, SourceType, null, cancellationToken);

    return [.. list.Select(ArtifactResponse.From)];
  }

  /// <summary>The experiment's dataset's storage key.</summary>
  /// <exception cref="NoDatasetException">The experiment has no dataset.</exception>
  private async Task<string> DatasetKey(Guid experimentId, CancellationToken cancellationToken) =>
    await db.Datasets.AsNoTracking()
      .Where(x => x.ExperimentId == experimentId)
      .Select(x => x.StorageKey)
      .SingleOrDefaultAsync(cancellationToken)
    ?? throw new NoDatasetException(experimentId);

  /// <summary>Calls EDA, turning its "dataset not found" into <see cref="NoDatasetException"/>.</summary>
  private static async Task<T> CallEda<T>(Guid experimentId, Func<Task<T>> call)
  {
    try
    {
      return await call();
    }
    catch (EdaException ex) when (ex.StatusCode == EdaException.DatasetNotFound)
    {
      throw new NoDatasetException(experimentId);
    }
  }
}
