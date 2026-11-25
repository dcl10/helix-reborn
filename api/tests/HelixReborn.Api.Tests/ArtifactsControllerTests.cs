namespace HelixReborn.Api.Tests;

using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Models;
using Services.Artifacts;
using Services.Storage;
using Xunit;

public class ArtifactsControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  private const string ExperimentsRoute = "/api/experiments";

  private readonly HttpClient _user = factory.CreateUserClient();

  [Fact]
  public async Task UploadingDataset_CreatesDatasetArtifact()
  {
    var experiment = await CreateExperiment(_user);
    var dataset = await UploadDataset(_user, experiment.Id);

    var artifacts = await _user.GetFromJsonAsync<List<ArtifactResponse>>($"{ExperimentsRoute}/{experiment.Id}/artifacts");

    var artifact = Assert.Single(artifacts!);
    Assert.Equal(ArtifactKind.Dataset, artifact.Kind);
    Assert.Equal($"Dataset: {dataset.FileName}", artifact.Title);
    Assert.Equal("text/csv", artifact.ContentType);
    Assert.Equal("dataset", artifact.SourceType);
    Assert.Equal(dataset.Id, artifact.SourceId);
    Assert.Equal(dataset.FileName, artifact.FileName);
  }

  [Fact]
  public async Task List_FiltersBySourceTypeAndSourceId()
  {
    var experiment = await CreateExperiment(_user);
    await UploadDataset(_user, experiment.Id);
    var runId = Guid.NewGuid();
    await RecordArtifact(
      experiment.Id, ArtifactKind.Model, "Model", $"experiments/{experiment.Id:D}/runs/{runId:D}/model.onnx",
      "application/octet-stream", 42, "run", runId);

    var all = await _user.GetFromJsonAsync<List<ArtifactResponse>>($"{ExperimentsRoute}/{experiment.Id}/artifacts");
    Assert.Equal(2, all!.Count);

    var byType = await _user.GetFromJsonAsync<List<ArtifactResponse>>(
      $"{ExperimentsRoute}/{experiment.Id}/artifacts?sourceType=run");
    Assert.Equal([ArtifactKind.Model], byType!.Select(x => x.Kind));

    var bySourceId = await _user.GetFromJsonAsync<List<ArtifactResponse>>(
      $"{ExperimentsRoute}/{experiment.Id}/artifacts?sourceType=run&sourceId={runId}");
    Assert.Single(bySourceId!);
  }

  [Fact]
  public async Task Content_ReturnsBytesAndContentType()
  {
    var experiment = await CreateExperiment(_user);
    var dataset = await UploadDataset(_user, experiment.Id);
    var artifact = await SingleArtifact(_user, experiment.Id);

    var response = await _user.GetAsync($"{ExperimentsRoute}/{experiment.Id}/artifacts/{artifact.Id}/content");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
    Assert.Equal("inline", response.Content.Headers.ContentDisposition?.DispositionType);
    var body = await response.Content.ReadAsStringAsync();
    Assert.Contains("age", body);
    Assert.Equal(dataset.FileName, response.Content.Headers.ContentDisposition?.FileName);
  }

  [Fact]
  public async Task Content_SetsAttachmentDisposition_WhenDownloadIsTrue()
  {
    var experiment = await CreateExperiment(_user);
    await UploadDataset(_user, experiment.Id);
    var artifact = await SingleArtifact(_user, experiment.Id);

    var response = await _user.GetAsync(
      $"{ExperimentsRoute}/{experiment.Id}/artifacts/{artifact.Id}/content?download=true");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
  }

  [Fact]
  public async Task Content_ReturnsNotFound_ForAnotherUsersExperiment()
  {
    var other = factory.CreateUserClient("user-artifact-other");
    var experiment = await CreateExperiment(other);
    await UploadDataset(other, experiment.Id);
    var artifact = await SingleArtifact(other, experiment.Id);

    var response = await _user.GetAsync($"{ExperimentsRoute}/{experiment.Id}/artifacts/{artifact.Id}/content");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Content_ReturnsOk_ForAdmin()
  {
    var experiment = await CreateExperiment(_user);
    await UploadDataset(_user, experiment.Id);
    var artifact = await SingleArtifact(_user, experiment.Id);
    var admin = factory.CreateAdminClient();

    var response = await admin.GetAsync($"{ExperimentsRoute}/{experiment.Id}/artifacts/{artifact.Id}/content");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Content_ReturnsNotFound_WhenObjectIsMissingFromStorage()
  {
    var experiment = await CreateExperiment(_user);
    var dataset = await UploadDataset(_user, experiment.Id);
    var artifact = await SingleArtifact(_user, experiment.Id);
    await factory.ObjectStorage.DeletePrefixAsync(
      StorageKeys.Dataset(experiment.Id, dataset.Id, dataset.FileName), CancellationToken.None);

    var response = await _user.GetAsync($"{ExperimentsRoute}/{experiment.Id}/artifacts/{artifact.Id}/content");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Archive_ContainsEveryArtifactAsARelativelyNamedEntry()
  {
    var experiment = await CreateExperiment(_user);
    var dataset = await UploadDataset(_user, experiment.Id);
    var runId = Guid.NewGuid();
    var modelKey = $"experiments/{experiment.Id:D}/runs/{runId:D}/model.onnx";
    await factory.ObjectStorage.PutAsync(modelKey, new MemoryStream([9, 9, 9]), "application/octet-stream", CancellationToken.None);
    await RecordArtifact(experiment.Id, ArtifactKind.Model, "Model", modelKey, "application/octet-stream", 3, "run", runId);

    var response = await _user.GetAsync($"{ExperimentsRoute}/{experiment.Id}/archive");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
    Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);

    await using var zipStream = await response.Content.ReadAsStreamAsync();
    using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

    var datasetEntry = archive.GetEntry($"datasets/{dataset.Id:D}/{dataset.FileName}");
    Assert.NotNull(datasetEntry);
    using (var entryStream = datasetEntry!.Open())
    using (var reader = new StreamReader(entryStream))
    {
      Assert.Contains("age", await reader.ReadToEndAsync());
    }

    var modelEntry = archive.GetEntry($"runs/{runId:D}/model.onnx");
    Assert.NotNull(modelEntry);
    using (var entryStream = modelEntry!.Open())
    using (var ms = new MemoryStream())
    {
      await entryStream.CopyToAsync(ms);
      Assert.Equal([9, 9, 9], ms.ToArray());
    }
  }

  private static async Task<ExperimentResponse> CreateExperiment(HttpClient client)
  {
    var response = await client.PostAsJsonAsync(ExperimentsRoute, new { name = "Artifact owner", description = (string?)null });
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<ExperimentResponse>())!;
  }

  private static async Task<DatasetResponse> UploadDataset(HttpClient client, Guid experimentId)
  {
    using var content = new MultipartFormDataContent();
    var csv = "id,age\n1,20\n2,30\n";
    var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
    fileContent.Headers.ContentType = new("text/csv");
    content.Add(fileContent, "file", "data.csv");

    var response = await client.PostAsync($"{ExperimentsRoute}/{experimentId}/dataset", content);
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<DatasetResponse>())!;
  }

  private static async Task<ArtifactResponse> SingleArtifact(HttpClient client, Guid experimentId)
  {
    var artifacts = await client.GetFromJsonAsync<List<ArtifactResponse>>($"{ExperimentsRoute}/{experimentId}/artifacts");
    return Assert.Single(artifacts!);
  }

  private async Task RecordArtifact(
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
    await artifacts.RecordAsync(experimentId, kind, title, storageKey, contentType, sizeBytes, sourceType, sourceId);
  }

}
