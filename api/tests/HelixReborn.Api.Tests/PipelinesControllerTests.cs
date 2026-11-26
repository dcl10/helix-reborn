namespace HelixReborn.Api.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fixtures;
using Microsoft.AspNetCore.Mvc;
using Models;
using Xunit;

public class PipelinesControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  private const string ExperimentsRoute = "/api/experiments";

  private readonly HttpClient _user = factory.CreateUserClient("user-pipelines");

  private static JsonObject Spec(string model = "random_forest_regressor", int nEstimators = 50)
  {
    var spec = JsonNode.Parse("""
      {"problem_type": "regression", "target_column": "y", "feature_columns": ["a", "b"],
       "steps": [{"step": "simple_imputer", "params": {"strategy": "mean"}}]}
      """)!.AsObject();
    spec["model"] = new JsonObject { ["step"] = model, ["params"] = new JsonObject { ["n_estimators"] = nEstimators } };
    return spec;
  }

  [Fact]
  public async Task Catalog_ReturnsTheCatalogFileAsIs()
  {
    var response = await _user.GetAsync("/api/pipeline-catalog");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    var catalog = PipelineCatalogTests.RepoCatalog();
    Assert.Equal(catalog.RawJson, await response.Content.ReadAsStringAsync());
  }

  [Fact]
  public async Task Catalog_RequiresASignedInUser()
  {
    var response = await factory.CreateAnonymousClient().GetAsync("/api/pipeline-catalog");

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task Crud_HappyPath()
  {
    var experiment = await CreateExperimentWithDataset(_user);
    var route = $"{ExperimentsRoute}/{experiment.Id}/pipelines";

    var created = await _user.PostAsJsonAsync(route, new { name = "RF baseline", spec = Spec() });
    Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    var pipeline = (await created.Content.ReadFromJsonAsync<PipelineResponse>())!;
    Assert.Equal("RF baseline", pipeline.Name);
    Assert.Equal($"{route}/{pipeline.Id}", created.Headers.Location?.OriginalString);

    // The stored spec is the snake_case contract, with the defaults filled in.
    Assert.Equal("random_forest_regressor", (string?)pipeline.Spec["model"]!["step"]);
    Assert.Equal(0.2, (double)pipeline.Spec["split"]!["test_size"]!);
    Assert.True(pipeline.Spec.ContainsKey("feature_columns"));

    var list = await _user.GetFromJsonAsync<List<PipelineResponse>>(route);
    Assert.Equal(pipeline.Id, Assert.Single(list!).Id);

    var updated = await _user.PutAsJsonAsync($"{route}/{pipeline.Id}", new { name = "Ridge", spec = Spec("ridge", 1) });
    Assert.Equal(HttpStatusCode.BadRequest, updated.StatusCode); // ridge has no n_estimators

    var ridge = JsonNode.Parse("""
      {"problem_type": "regression", "target_column": "y", "feature_columns": ["a"], "model": {"step": "ridge"}}
      """)!.AsObject();
    updated = await _user.PutAsJsonAsync($"{route}/{pipeline.Id}", new { name = "Ridge", spec = ridge });
    Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
    var fetched = await _user.GetFromJsonAsync<PipelineResponse>($"{route}/{pipeline.Id}");
    Assert.Equal("Ridge", fetched!.Name);
    Assert.Equal("ridge", (string?)fetched.Spec["model"]!["step"]);
    Assert.True(fetched.UpdatedAt >= fetched.CreatedAt);

    var deleted = await _user.DeleteAsync($"{route}/{pipeline.Id}");
    Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await _user.GetAsync($"{route}/{pipeline.Id}")).StatusCode);
  }

  [Fact]
  public async Task Create_Returns400WithEveryProblemUnderSpec()
  {
    var experiment = await CreateExperimentWithDataset(_user);
    var spec = JsonNode.Parse("""
      {"problem_type": "regression", "target_column": "y", "feature_columns": ["a", "label"],
       "model": {"step": "random_forest_regressor", "params": {"n_estimators": 0}}}
      """)!.AsObject();

    var response = await _user.PostAsJsonAsync(
      $"{ExperimentsRoute}/{experiment.Id}/pipelines", new { name = "Bad", spec });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
    var errors = problem!.Errors["spec"];
    Assert.Contains("feature_columns: 'label' is not numeric", errors);
    Assert.Contains(errors, e => e.StartsWith("model.params.n_estimators: ", StringComparison.Ordinal));
  }

  [Fact]
  public async Task Create_Returns400_ForAnUnknownSpecField()
  {
    var experiment = await CreateExperimentWithDataset(_user);
    var spec = Spec();
    spec["extra"] = 1;

    var response = await _user.PostAsJsonAsync($"{ExperimentsRoute}/{experiment.Id}/pipelines", new { name = "X", spec });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
    Assert.Contains(problem!.Errors["spec"], e => e.Contains("extra", StringComparison.Ordinal));
  }

  [Fact]
  public async Task Create_Returns400_WhenNameOrSpecIsMissing()
  {
    var experiment = await CreateExperimentWithDataset(_user);
    var route = $"{ExperimentsRoute}/{experiment.Id}/pipelines";

    var noName = await _user.PostAsJsonAsync(route, new { name = "", spec = Spec() });
    var noSpec = await _user.PostAsJsonAsync(route, new { name = "X" });

    Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, noSpec.StatusCode);
  }

  [Fact]
  public async Task Create_Returns409_WhenTheExperimentHasNoDataset()
  {
    var experiment = await CreateExperiment(_user);

    var response = await _user.PostAsJsonAsync(
      $"{ExperimentsRoute}/{experiment.Id}/pipelines", new { name = "RF", spec = Spec() });

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
  }

  [Fact]
  public async Task Create_Returns409_ForADuplicateName()
  {
    var experiment = await CreateExperimentWithDataset(_user);
    var route = $"{ExperimentsRoute}/{experiment.Id}/pipelines";
    (await _user.PostAsJsonAsync(route, new { name = "RF", spec = Spec() })).EnsureSuccessStatusCode();

    var duplicate = await _user.PostAsJsonAsync(route, new { name = "RF", spec = Spec() });

    Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
  }

  [Fact]
  public async Task Update_Returns409_WhenRenamedToAnotherPipelinesName()
  {
    var experiment = await CreateExperimentWithDataset(_user);
    var route = $"{ExperimentsRoute}/{experiment.Id}/pipelines";
    (await _user.PostAsJsonAsync(route, new { name = "A", spec = Spec() })).EnsureSuccessStatusCode();
    var b = await (await _user.PostAsJsonAsync(route, new { name = "B", spec = Spec() }))
      .Content.ReadFromJsonAsync<PipelineResponse>();

    var renamed = await _user.PutAsJsonAsync($"{route}/{b!.Id}", new { name = "A", spec = Spec() });
    var unchanged = await _user.PutAsJsonAsync($"{route}/{b.Id}", new { name = "B", spec = Spec(nEstimators: 70) });

    Assert.Equal(HttpStatusCode.Conflict, renamed.StatusCode);
    Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
  }

  [Fact]
  public async Task AnotherUsersExperiment_Returns404()
  {
    var other = factory.CreateUserClient("user-pipelines-other");
    var experiment = await CreateExperimentWithDataset(other);
    var route = $"{ExperimentsRoute}/{experiment.Id}/pipelines";
    var pipeline = await (await other.PostAsJsonAsync(route, new { name = "RF", spec = Spec() }))
      .Content.ReadFromJsonAsync<PipelineResponse>();

    Assert.Equal(HttpStatusCode.NotFound, (await _user.GetAsync(route)).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await _user.GetAsync($"{route}/{pipeline!.Id}")).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await _user.PostAsJsonAsync(route, new { name = "X", spec = Spec() })).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await _user.DeleteAsync($"{route}/{pipeline.Id}")).StatusCode);
  }

  [Fact]
  public async Task Admin_CanReadButNotWrite()
  {
    var experiment = await CreateExperimentWithDataset(_user);
    var route = $"{ExperimentsRoute}/{experiment.Id}/pipelines";
    var pipeline = await (await _user.PostAsJsonAsync(route, new { name = "RF", spec = Spec() }))
      .Content.ReadFromJsonAsync<PipelineResponse>();
    var admin = factory.CreateAdminClient();

    Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"{route}/{pipeline!.Id}")).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"{route}/{pipeline.Id}", new { name = "X", spec = Spec() })).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"{route}/{pipeline.Id}")).StatusCode);
  }

  [Fact]
  public async Task Get_Returns404_ForAPipelineInAnotherExperiment()
  {
    var first = await CreateExperimentWithDataset(_user);
    var second = await CreateExperimentWithDataset(_user);
    var pipeline = await (await _user.PostAsJsonAsync(
        $"{ExperimentsRoute}/{first.Id}/pipelines", new { name = "RF", spec = Spec() }))
      .Content.ReadFromJsonAsync<PipelineResponse>();

    var response = await _user.GetAsync($"{ExperimentsRoute}/{second.Id}/pipelines/{pipeline!.Id}");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  private static async Task<ExperimentResponse> CreateExperiment(HttpClient client)
  {
    var response = await client.PostAsJsonAsync(ExperimentsRoute, new { name = "Pipelines", description = (string?)null });
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<ExperimentResponse>())!;
  }

  private static async Task<ExperimentResponse> CreateExperimentWithDataset(HttpClient client)
  {
    var experiment = await CreateExperiment(client);
    using var content = new MultipartFormDataContent();
    var csv = "a,b,y,label\n1,2,3.5,x\n4,5,6.5,y\n7,8,9.5,x\n";
    var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
    file.Headers.ContentType = new("text/csv");
    content.Add(file, "file", "data.csv");
    (await client.PostAsync($"{ExperimentsRoute}/{experiment.Id}/dataset", content)).EnsureSuccessStatusCode();
    return experiment;
  }
}
