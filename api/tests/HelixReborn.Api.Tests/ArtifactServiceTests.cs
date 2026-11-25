namespace HelixReborn.Api.Tests;

using Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Models;
using Services.Artifacts;
using Xunit;

public class ArtifactServiceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  [Fact]
  public async Task RecordAsync_ThenGetAsync_RoundTripsArtifact()
  {
    var experimentId = await CreateExperiment();

    var recorded = await Record(
      experimentId,
      ArtifactKind.EdaPlot,
      "Correlation heatmap",
      $"experiments/{experimentId:D}/eda/{Guid.NewGuid():D}.png",
      "image/png",
      1234,
      "eda",
      Guid.NewGuid());

    var fetched = await Get(experimentId, recorded.Id);

    Assert.NotNull(fetched);
    Assert.Equal(recorded.StorageKey, fetched!.StorageKey);
    Assert.Equal("Correlation heatmap", fetched.Title);
    Assert.Equal(ArtifactKind.EdaPlot, fetched.Kind);
  }

  [Fact]
  public async Task GetAsync_ReturnsNull_ForAnotherExperiment()
  {
    var experimentId = await CreateExperiment();
    var otherExperimentId = await CreateExperiment();
    var artifact = await Record(
      experimentId,
      ArtifactKind.Dataset,
      "Dataset: data.csv",
      $"experiments/{experimentId:D}/datasets/{Guid.NewGuid():D}/data.csv",
      "text/csv",
      10,
      "dataset",
      Guid.NewGuid());

    var fetched = await Get(otherExperimentId, artifact.Id);

    Assert.Null(fetched);
  }

  [Fact]
  public async Task ListAsync_FiltersBySourceTypeAndSourceId()
  {
    var experimentId = await CreateExperiment();
    var runId = Guid.NewGuid();
    var model = await Record(
      experimentId, ArtifactKind.Model, "Model", $"experiments/{experimentId:D}/runs/{runId:D}/model.onnx",
      "application/octet-stream", 100, "run", runId);
    await Record(
      experimentId, ArtifactKind.EdaPlot, "Plot", $"experiments/{experimentId:D}/eda/{Guid.NewGuid():D}.png",
      "image/png", 50, "eda", Guid.NewGuid());
    await Record(
      experimentId, ArtifactKind.ModelMetadata, "Metadata", $"experiments/{experimentId:D}/runs/{Guid.NewGuid():D}/metadata.json",
      "application/json", 20, "run", Guid.NewGuid());

    var bySourceType = await List(experimentId, "run", null);
    Assert.Equal(2, bySourceType.Count);

    var bySourceId = await List(experimentId, "run", runId);
    Assert.Equal([model.Id], bySourceId.Select(x => x.Id));
  }

  [Fact]
  public async Task ListAsync_OrdersByCreatedAt()
  {
    var experimentId = await CreateExperiment();
    var first = await Record(
      experimentId, ArtifactKind.Dataset, "First", $"experiments/{experimentId:D}/datasets/{Guid.NewGuid():D}/a.csv",
      "text/csv", 10, "dataset", Guid.NewGuid());
    var second = await Record(
      experimentId, ArtifactKind.EdaPlot, "Second", $"experiments/{experimentId:D}/eda/{Guid.NewGuid():D}.png",
      "image/png", 10, "eda", Guid.NewGuid());

    var listed = await List(experimentId, null, null);

    Assert.Equal([first.Id, second.Id], listed.Select(x => x.Id));
  }

  private async Task<Artifact> Record(
    Guid experimentId,
    ArtifactKind kind,
    string title,
    string storageKey,
    string contentType,
    long sizeBytes,
    string sourceType,
    Guid sourceId)
  {
    using var scope = factory.Services.CreateScope();
    var artifacts = scope.ServiceProvider.GetRequiredService<IArtifactService>();
    return await artifacts.RecordAsync(experimentId, kind, title, storageKey, contentType, sizeBytes, sourceType, sourceId);
  }

  private async Task<Artifact?> Get(Guid experimentId, Guid artifactId)
  {
    using var scope = factory.Services.CreateScope();
    var artifacts = scope.ServiceProvider.GetRequiredService<IArtifactService>();
    return await artifacts.GetAsync(experimentId, artifactId);
  }

  private async Task<IReadOnlyList<Artifact>> List(Guid experimentId, string? sourceType, Guid? sourceId)
  {
    using var scope = factory.Services.CreateScope();
    var artifacts = scope.ServiceProvider.GetRequiredService<IArtifactService>();
    return await artifacts.ListAsync(experimentId, sourceType, sourceId);
  }

  private async Task<Guid> CreateExperiment()
  {
    using var scope = factory.Services.CreateScope();
    var experiments = scope.ServiceProvider.GetRequiredService<Services.Experiments.IExperimentService>();
    var created = await experiments.CreateAsync($"user-{Guid.NewGuid():N}", "Experiment", null);
    return created.Id;
  }
}
