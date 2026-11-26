namespace HelixReborn.Api.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Fixtures;
using Microsoft.AspNetCore.Mvc;
using Models;
using Models.Eda;
using Services.Eda;
using Services.Storage;
using Xunit;

public class EdaControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  private const string ExperimentsRoute = "/api/experiments";

  private static readonly string[] AgeAndId = ["age", "id"];
  private static readonly string[] Age = ["age"];
  private static readonly string[] Name = ["name"];

  private readonly HttpClient _user = factory.CreateUserClient("user-eda");

  [Fact]
  public async Task Summary_PassesEdaJsonThrough_WhenDatasetExists()
  {
    var experiment = await CreateExperiment(_user);
    var dataset = await UploadDataset(_user, experiment.Id);

    var response = await _user.GetAsync($"{ExperimentsRoute}/{experiment.Id}/eda/summary");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    Assert.True(JsonElement.DeepEquals(FakeEdaClient.Summary, body), body.GetRawText());
    var call = Assert.Single(factory.Eda.CallsFor(experiment.Id));
    Assert.Equal("summary", call.Operation);
    Assert.Equal(StorageKeys.Dataset(experiment.Id, dataset.Id, dataset.FileName), call.DatasetKey);
  }

  [Fact]
  public async Task Summary_ReturnsConflict_WhenExperimentHasNoDataset()
  {
    var experiment = await CreateExperiment(_user);

    var response = await _user.GetAsync($"{ExperimentsRoute}/{experiment.Id}/eda/summary");

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    Assert.Empty(factory.Eda.CallsFor(experiment.Id));
  }

  [Fact]
  public async Task CreatePlot_ReturnsCreatedArtifact_AndSendsOutputKeyToEda()
  {
    var experiment = await CreateExperiment(_user);
    var dataset = await UploadDataset(_user, experiment.Id);

    var response = await _user.PostAsJsonAsync($"{ExperimentsRoute}/{experiment.Id}/eda/plots", new
    {
      type = "correlationHeatmap",
      options = new { method = "spearman", columns = AgeAndId },
      plotOptions = new { dpi = 100 },
    });

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    var artifact = (await response.Content.ReadFromJsonAsync<ArtifactResponse>())!;
    Assert.Equal(ArtifactKind.EdaPlot, artifact.Kind);
    Assert.Equal("eda", artifact.SourceType);
    Assert.Null(artifact.SourceId);
    Assert.Equal("image/png", artifact.ContentType);
    Assert.Equal(1234, artifact.SizeBytes);
    Assert.Equal("Fake CorrelationHeatmap", artifact.Title);
    Assert.Equal($"{artifact.Id:D}.png", artifact.FileName);
    Assert.Equal(
      $"/api/experiments/{experiment.Id}/artifacts/{artifact.Id}/content",
      response.Headers.Location?.OriginalString);

    var call = Assert.Single(factory.Eda.CallsFor(experiment.Id));
    Assert.Equal(EdaPlotType.CorrelationHeatmap, call.PlotType);
    Assert.Equal(StorageKeys.Dataset(experiment.Id, dataset.Id, dataset.FileName), call.DatasetKey);
    Assert.Equal(StorageKeys.Eda(experiment.Id, artifact.Id, "png"), call.OutputKey);
    Assert.Equal("spearman", (string?)call.Options!["method"]);
    Assert.Equal(100, (int?)call.Options["plot_options"]!["dpi"]);
  }

  [Fact]
  public async Task CreatePlot_IsListedInEdaArtifacts_WithoutTheDataset()
  {
    var experiment = await CreateExperiment(_user);
    await UploadDataset(_user, experiment.Id);
    var created = await CreatePlot(_user, experiment.Id);

    var list = await _user.GetFromJsonAsync<List<ArtifactResponse>>($"{ExperimentsRoute}/{experiment.Id}/eda/artifacts");

    var artifact = Assert.Single(list!);
    Assert.Equal(created.Id, artifact.Id);
  }

  [Fact]
  public async Task CreatePlot_ReturnsBadRequestWithEdaDetail_WhenEdaRejects()
  {
    var experiment = await CreateExperiment(_user);
    await UploadDataset(_user, experiment.Id);
    factory.Eda.FailFor(experiment.Id, new EdaException(EdaException.Rejected, "Non-numeric column(s): name"));

    var response = await _user.PostAsJsonAsync(
      $"{ExperimentsRoute}/{experiment.Id}/eda/plots",
      new { type = "pca", options = new { columns = Name } });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
    Assert.Equal("Non-numeric column(s): name", problem!.Detail);
    await AssertNoEdaArtifacts(experiment.Id);
  }

  [Fact]
  public async Task CreatePlot_ReturnsConflict_WhenEdaCantFindDataset()
  {
    var experiment = await CreateExperiment(_user);
    await UploadDataset(_user, experiment.Id);
    factory.Eda.FailFor(experiment.Id, new EdaException(EdaException.DatasetNotFound, "No such key"));

    var response = await _user.PostAsJsonAsync($"{ExperimentsRoute}/{experiment.Id}/eda/plots", new { type = "pca" });

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
  }

  [Fact]
  public async Task CreatePlot_ReturnsBadGateway_WhenEdaFails()
  {
    var experiment = await CreateExperiment(_user);
    await UploadDataset(_user, experiment.Id);
    factory.Eda.FailFor(experiment.Id, new EdaException(EdaException.Failed, "Couldn't reach the EDA service."));

    var response = await _user.PostAsJsonAsync($"{ExperimentsRoute}/{experiment.Id}/eda/plots", new { type = "pca" });

    Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    await AssertNoEdaArtifacts(experiment.Id);
  }

  [Fact]
  public async Task CreatePlot_ReturnsConflict_WhenExperimentHasNoDataset()
  {
    var experiment = await CreateExperiment(_user);

    var response = await _user.PostAsJsonAsync($"{ExperimentsRoute}/{experiment.Id}/eda/plots", new { type = "pca" });

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
  }

  [Theory]
  [InlineData("{}")]
  [InlineData("""{"type": "histogram"}""")]
  public async Task CreatePlot_ReturnsBadRequest_WhenTypeIsMissingOrUnknown(string body)
  {
    var experiment = await CreateExperiment(_user);
    await UploadDataset(_user, experiment.Id);

    var response = await _user.PostAsync(
      $"{ExperimentsRoute}/{experiment.Id}/eda/plots",
      new StringContent(body, Encoding.UTF8, "application/json"));

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    Assert.Empty(factory.Eda.CallsFor(experiment.Id));
  }

  [Fact]
  public async Task CreatePlot_ReturnsNotFound_ForAnotherUsersExperiment()
  {
    var other = factory.CreateUserClient("user-eda-other");
    var experiment = await CreateExperiment(other);
    await UploadDataset(other, experiment.Id);

    var post = await _user.PostAsJsonAsync($"{ExperimentsRoute}/{experiment.Id}/eda/plots", new { type = "pca" });
    var summary = await _user.GetAsync($"{ExperimentsRoute}/{experiment.Id}/eda/summary");
    var list = await _user.GetAsync($"{ExperimentsRoute}/{experiment.Id}/eda/artifacts");

    Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, summary.StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
    Assert.Empty(factory.Eda.CallsFor(experiment.Id));
  }

  [Fact]
  public async Task Admin_CanReadEda_ButPostsGetNotFound()
  {
    var experiment = await CreateExperiment(_user);
    await UploadDataset(_user, experiment.Id);
    await CreatePlot(_user, experiment.Id);
    var admin = factory.CreateAdminClient();

    var list = await admin.GetFromJsonAsync<List<ArtifactResponse>>($"{ExperimentsRoute}/{experiment.Id}/eda/artifacts");
    var summary = await admin.GetAsync($"{ExperimentsRoute}/{experiment.Id}/eda/summary");
    var plot = await admin.PostAsJsonAsync($"{ExperimentsRoute}/{experiment.Id}/eda/plots", new { type = "pca" });
    var table = await admin.PostAsJsonAsync(
      $"{ExperimentsRoute}/{experiment.Id}/eda/tables/normality",
      new { columns = (string[]?)null });

    Assert.Single(list!);
    Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, plot.StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, table.StatusCode);
  }

  [Fact]
  public async Task CreateNormalityTable_ReturnsCreatedArtifactAndRows()
  {
    var experiment = await CreateExperiment(_user);
    await UploadDataset(_user, experiment.Id);

    var response = await _user.PostAsJsonAsync(
      $"{ExperimentsRoute}/{experiment.Id}/eda/tables/normality",
      new { columns = Age });

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    var artifact = body.GetProperty("artifact").Deserialize<ArtifactResponse>(JsonSerializerOptions.Web)!;
    Assert.Equal(ArtifactKind.EdaTable, artifact.Kind);
    Assert.Equal("text/csv", artifact.ContentType);
    Assert.Equal("Normality tests", artifact.Title);
    Assert.Equal($"{artifact.Id:D}.csv", artifact.FileName);
    Assert.True(JsonElement.DeepEquals(FakeEdaClient.NormalityRows, body.GetProperty("rows")), body.GetRawText());

    var call = Assert.Single(factory.Eda.CallsFor(experiment.Id));
    Assert.Equal("normality", call.Operation);
    Assert.Equal(StorageKeys.Eda(experiment.Id, artifact.Id, "csv"), call.OutputKey);
    Assert.Equal(["age"], call.Columns!);
  }

  [Fact]
  public async Task CreateNormalityTable_SendsNullColumns_WhenNoneGiven()
  {
    var experiment = await CreateExperiment(_user);
    await UploadDataset(_user, experiment.Id);

    var response = await _user.PostAsJsonAsync(
      $"{ExperimentsRoute}/{experiment.Id}/eda/tables/normality",
      new { columns = (string[]?)null });

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    Assert.Null(Assert.Single(factory.Eda.CallsFor(experiment.Id)).Columns);
  }

  private async Task AssertNoEdaArtifacts(Guid experimentId)
  {
    var list = await _user.GetFromJsonAsync<List<ArtifactResponse>>($"{ExperimentsRoute}/{experimentId}/eda/artifacts");
    Assert.Empty(list!);
  }

  private static async Task<ArtifactResponse> CreatePlot(HttpClient client, Guid experimentId)
  {
    var response = await client.PostAsJsonAsync($"{ExperimentsRoute}/{experimentId}/eda/plots", new { type = "pca" });
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<ArtifactResponse>())!;
  }

  private static async Task<ExperimentResponse> CreateExperiment(HttpClient client)
  {
    var response = await client.PostAsJsonAsync(ExperimentsRoute, new { name = "EDA", description = (string?)null });
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<ExperimentResponse>())!;
  }

  private static async Task<DatasetResponse> UploadDataset(HttpClient client, Guid experimentId)
  {
    using var content = new MultipartFormDataContent();
    var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("id,age\n1,20\n2,30\n"));
    fileContent.Headers.ContentType = new("text/csv");
    content.Add(fileContent, "file", "data.csv");

    var response = await client.PostAsync($"{ExperimentsRoute}/{experimentId}/dataset", content);
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<DatasetResponse>())!;
  }
}
