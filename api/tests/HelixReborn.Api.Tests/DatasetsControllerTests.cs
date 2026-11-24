namespace HelixReborn.Api.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using Fixtures;
using Microsoft.AspNetCore.Mvc;
using Models;
using Xunit;

public class DatasetsControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  private const string ExperimentsRoute = "/api/experiments";

  private readonly HttpClient _user = factory.CreateUserClient();

  [Fact]
  public async Task Upload_StoresObjectAndReturnsColumns_ForValidCsv()
  {
    var experiment = await CreateExperiment(_user);

    var response = await Upload(_user, experiment.Id, ValidCsv(), "bone density.csv");

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    var dataset = await response.Content.ReadFromJsonAsync<DatasetResponse>();
    Assert.Equal("bone_density.csv", dataset!.FileName);
    Assert.Equal(10, dataset.RowCount);
    var age = Assert.Single(dataset.Columns, x => x.Name == "age");
    Assert.Equal("numeric", age.InferredType);
    var label = Assert.Single(dataset.Columns, x => x.Name == "label");
    Assert.Equal("text", label.InferredType);

    var expectedKey = $"experiments/{experiment.Id:D}/datasets/{dataset.Id:D}/bone_density.csv";
    Assert.True(await factory.ObjectStorage.ExistsAsync(expectedKey, CancellationToken.None));

    var fetched = await _user.GetFromJsonAsync<DatasetResponse>($"{ExperimentsRoute}/{experiment.Id}/dataset");
    Assert.Equal(dataset.Id, fetched!.Id);
  }

  [Fact]
  public async Task Upload_ReturnsConflict_WhenDatasetAlreadyExists()
  {
    var experiment = await CreateExperiment(_user);
    (await Upload(_user, experiment.Id, ValidCsv(), "first.csv")).EnsureSuccessStatusCode();

    var response = await Upload(_user, experiment.Id, ValidCsv(), "second.csv");

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
  }

  [Fact]
  public async Task Upload_ReturnsBadRequest_ForNonCsvExtension()
  {
    var experiment = await CreateExperiment(_user);

    var response = await Upload(_user, experiment.Id, ValidCsv(), "data.txt");

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Upload_ReturnsBadRequest_ForDuplicateHeaders()
  {
    var experiment = await CreateExperiment(_user);
    var csv = Csv(["id", "id"], Enumerable.Range(0, 10).Select(i => new[] { $"{i}", $"{i}" }));

    var response = await Upload(_user, experiment.Id, csv, "data.csv");

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Upload_ReturnsBadRequest_WhenFewerThanTenRows()
  {
    var experiment = await CreateExperiment(_user);
    var csv = Csv(["id", "age"], Enumerable.Range(0, 5).Select(i => new[] { $"{i}", "20" }));

    var response = await Upload(_user, experiment.Id, csv, "data.csv");

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
    Assert.Contains("file", problem!.Errors.Keys);
  }

  [Fact]
  public async Task Upload_ReturnsRequestEntityTooLarge_WhenOverLimit()
  {
    var experiment = await CreateExperiment(_user);
    var oversized = new byte[52_428_801];

    using var content = new MultipartFormDataContent();
    var fileContent = new ByteArrayContent(oversized);
    fileContent.Headers.ContentType = new("text/csv");
    content.Add(fileContent, "file", "huge.csv");

    var response = await _user.PostAsync($"{ExperimentsRoute}/{experiment.Id}/dataset", content);
    var body = await response.Content.ReadAsStringAsync();

    throw new Exception($"{response.StatusCode}:{body}");
  }

  [Fact]
  public async Task Upload_ReturnsNotFound_ForAnotherUsersExperiment()
  {
    var other = factory.CreateUserClient("user-dataset-other");
    var experiment = await CreateExperiment(other);

    var response = await Upload(_user, experiment.Id, ValidCsv(), "data.csv");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Get_ReturnsNotFound_WhenNoDatasetUploaded()
  {
    var experiment = await CreateExperiment(_user);

    var response = await _user.GetAsync($"{ExperimentsRoute}/{experiment.Id}/dataset");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  private static async Task<ExperimentResponse> CreateExperiment(HttpClient client)
  {
    var response = await client.PostAsJsonAsync(ExperimentsRoute, new { name = "Dataset owner", description = (string?)null });
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<ExperimentResponse>())!;
  }

  private static async Task<HttpResponseMessage> Upload(
    HttpClient client,
    Guid experimentId,
    string csv,
    string fileName)
  {
    using var content = new MultipartFormDataContent();
    var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
    fileContent.Headers.ContentType = new("text/csv");
    content.Add(fileContent, "file", fileName);

    return await client.PostAsync($"{ExperimentsRoute}/{experimentId}/dataset", content);
  }

  private static string ValidCsv() =>
    Csv(["id", "age", "label"], Enumerable.Range(0, 10).Select(i => new[] { $"{i}", $"{20 + i}", i % 2 == 0 ? "a" : "b" }));

  private static string Csv(string[] header, IEnumerable<string[]> rows)
  {
    var lines = new List<string> { string.Join(",", header) };
    lines.AddRange(rows.Select(row => string.Join(",", row)));
    return string.Join("\n", lines);
  }
}
