namespace HelixReborn.Api.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fixtures;
using Messaging;
using Microsoft.AspNetCore.Mvc;
using Models;
using Services.Storage;
using Xunit;

public class EvaluationsControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  private readonly HttpClient _user = factory.CreateUserClient("user-evaluations");

  [Fact]
  public async Task Start_Returns202_AndPublishesTheJob_WithDefaultsFilledIn()
  {
    var (experiment, dataset, run) = await EvaluationSetup.SucceededRunAsync(factory, _user);

    var response = await EvaluationSetup.PostAsync(
      _user, experiment.Id, run.Id, """{"local_methods": ["shap"], "local_max_samples": 20}""");

    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    var evaluation = (await response.Content.ReadFromJsonAsync<EvaluationResponse>())!;
    Assert.Equal(EvaluationStatus.Queued, evaluation.Status);
    Assert.Equal(run.Id, evaluation.RunId);
    Assert.Equal("Ridge", evaluation.PipelineName);
    Assert.Null(evaluation.Metrics);
    Assert.Equal(
      $"/api/experiments/{experiment.Id}/evaluations/{evaluation.Id}", response.Headers.Location?.OriginalString);

    var job = Assert.Single(factory.Jobs.EvaluationJobsFor(experiment.Id));
    Assert.Equal(evaluation.Id, job.EvaluationId);
    Assert.Equal(run.Id, job.RunId);
    Assert.Equal(StorageKeys.Dataset(experiment.Id, dataset.Id, dataset.FileName), job.DatasetKey);
    Assert.Equal($"experiments/{experiment.Id:D}/runs/{run.Id:D}/", job.ModelPrefix);
    Assert.Equal($"experiments/{experiment.Id:D}/evaluations/{evaluation.Id:D}/", job.OutputPrefix);

    var options = JsonNode.Parse(JsonSerializer.Serialize(job.Options, JobMessageJson.Options))!.AsObject();
    var expected = JsonNode.Parse("""
      {"plots": true, "global_methods": ["permutation", "shap"], "permutation_repeats": 10,
       "local_methods": ["shap"], "local_max_samples": 20, "ensemble_methods": [], "num_features_to_plot": 10,
       "fuzzy": null, "plot_options": null}
      """);
    Assert.True(JsonNode.DeepEquals(expected, options), options.ToJsonString());
    Assert.True(JsonNode.DeepEquals(expected, evaluation.Options), evaluation.Options.ToJsonString());
  }

  [Fact]
  public async Task Start_UsesAllDefaults_WhenNoOptionsAreSent()
  {
    var (experiment, _, run) = await EvaluationSetup.SucceededRunAsync(factory, _user);

    var evaluation = await EvaluationSetup.StartAsync(_user, experiment.Id, run.Id);

    Assert.True((bool)evaluation.Options["plots"]!);
    Assert.Equal(10, (int)evaluation.Options["permutation_repeats"]!);
    Assert.Null(evaluation.Options["fuzzy"]);
  }

  [Fact]
  public async Task Start_Returns409_WhenTheRunHasntSucceeded()
  {
    var (experiment, _, run) = await EvaluationSetup.QueuedRunAsync(_user);

    var response = await EvaluationSetup.PostAsync(_user, experiment.Id, run.Id);

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    Assert.Empty(factory.Jobs.EvaluationJobsFor(experiment.Id));
  }

  [Theory]
  [InlineData("""{"ensemble_methods": ["mean"], "fuzzy": {}}""", "fuzzy: needs at least one local method")]
  [InlineData("""{"global_methods": [], "ensemble_methods": ["mean"]}""", "ensemble_methods: needs at least one global method")]
  [InlineData(
    """{"local_methods": ["lime"], "ensemble_methods": ["mean"], "fuzzy": {"number_clusters": 2, "cluster_names": ["a"]}}""",
    "fuzzy.cluster_names: has 1 names but number_clusters is 2")]
  [InlineData("""{"permutation_repeats": 500}""", "permutation_repeats")]
  public async Task Start_Returns400_ForInvalidOptions(string options, string error)
  {
    var (experiment, _, run) = await EvaluationSetup.SucceededRunAsync(factory, _user);

    var response = await EvaluationSetup.PostAsync(_user, experiment.Id, run.Id, options);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;
    Assert.Contains(problem.Errors["options"], e => e.StartsWith(error, StringComparison.Ordinal));
    Assert.Empty(factory.Jobs.EvaluationJobsFor(experiment.Id));
  }

  [Fact]
  public async Task Start_Returns503_AndFailsTheEvaluation_WhenPublishingFails()
  {
    var (experiment, _, run) = await EvaluationSetup.SucceededRunAsync(factory, _user);
    factory.Jobs.FailFor(experiment.Id);

    var response = await EvaluationSetup.PostAsync(_user, experiment.Id, run.Id);

    Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    var evaluation = Assert.Single(
      (await _user.GetFromJsonAsync<List<EvaluationResponse>>($"/api/experiments/{experiment.Id}/evaluations"))!);
    Assert.Equal(EvaluationStatus.Failed, evaluation.Status);
    Assert.Equal("QueueUnavailable", evaluation.Error?.Type);
    Assert.NotNull(evaluation.FinishedAt);
  }

  [Fact]
  public async Task AnotherUsersExperiment_Returns404()
  {
    var other = factory.CreateUserClient("user-evaluations-other");
    var (experiment, _, run) = await EvaluationSetup.SucceededRunAsync(factory, other);
    var evaluation = await EvaluationSetup.StartAsync(other, experiment.Id, run.Id);
    var evaluations = $"/api/experiments/{experiment.Id}/evaluations";

    Assert.Equal(HttpStatusCode.NotFound, (await EvaluationSetup.PostAsync(_user, experiment.Id, run.Id)).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await _user.GetAsync(evaluations)).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await _user.GetAsync($"{evaluations}/{evaluation.Id}")).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await _user.GetAsync($"{evaluations}/{evaluation.Id}/events")).StatusCode);
  }

  [Fact]
  public async Task Admin_CanReadEvaluations_ButNotStartThem()
  {
    var (experiment, _, run) = await EvaluationSetup.SucceededRunAsync(factory, _user);
    var evaluation = await EvaluationSetup.StartAsync(_user, experiment.Id, run.Id);
    var admin = factory.CreateAdminClient();

    Assert.Equal(
      HttpStatusCode.OK,
      (await admin.GetAsync($"/api/experiments/{experiment.Id}/evaluations/{evaluation.Id}")).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await EvaluationSetup.PostAsync(admin, experiment.Id, run.Id)).StatusCode);
  }

  [Fact]
  public async Task Get_Returns404_ForAnUnknownEvaluation()
  {
    var (experiment, _, _) = await EvaluationSetup.QueuedRunAsync(_user);

    var response = await _user.GetAsync($"/api/experiments/{experiment.Id}/evaluations/{Guid.NewGuid()}");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task List_ReturnsNewestFirst_AndFiltersByRun()
  {
    var (experiment, _, pipeline) = await RunSetup.CreateAsync(_user);
    var runA = await RunSetup.StartAsync(_user, experiment.Id, pipeline.Id);
    var runB = await RunSetup.StartAsync(_user, experiment.Id, pipeline.Id);
    foreach (var run in new[] { runA, runB })
    {
      await RunSetup.ApplyAsync(factory, RunSetup.JobIdOf(factory, run.Id), run.Id, "succeeded", result: "{}");
    }

    var first = await EvaluationSetup.StartAsync(_user, experiment.Id, runA.Id);
    var second = await EvaluationSetup.StartAsync(_user, experiment.Id, runB.Id);
    var third = await EvaluationSetup.StartAsync(_user, experiment.Id, runA.Id);
    var url = $"/api/experiments/{experiment.Id}/evaluations";

    var all = await _user.GetFromJsonAsync<List<EvaluationResponse>>(url);
    var forA = await _user.GetFromJsonAsync<List<EvaluationResponse>>($"{url}?runId={runA.Id}");

    Assert.Equal([third.Id, second.Id, first.Id], all!.Select(e => e.Id));
    Assert.Equal([third.Id, first.Id], forA!.Select(e => e.Id));
  }

  [Fact]
  public async Task Events_StreamsOneStatusThenEnd_ForATerminalEvaluation()
  {
    var (experiment, _, run) = await EvaluationSetup.SucceededRunAsync(factory, _user);
    var evaluation = await EvaluationSetup.StartAsync(_user, experiment.Id, run.Id);
    await EvaluationSetup.ApplyAsync(
      factory,
      EvaluationSetup.JobOf(factory, evaluation.Id).JobId,
      evaluation.Id,
      "failed",
      error: new("InvalidSpecError", "Ensemble importance needs at least one global method"));

    var response = await _user.GetAsync($"/api/experiments/{experiment.Id}/evaluations/{evaluation.Id}/events");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    var events = Sse.Parse(await response.Content.ReadAsStringAsync());
    Assert.Equal(["status", "end"], events.Select(e => e.Type));
    Assert.Equal("failed", (string?)events[0].Data["status"]);
    Assert.Equal("InvalidSpecError", (string?)events[0].Data["error"]!["type"]);
    Assert.Equal("Ridge", (string?)events[0].Data["pipelineName"]);
    Assert.Equal("failed", (string?)events[1].Data["status"]);
  }

  [Fact]
  public async Task Events_StreamsChanges_UntilTheEvaluationEnds()
  {
    var (experiment, _, run) = await EvaluationSetup.SucceededRunAsync(factory, _user);
    var evaluation = await EvaluationSetup.StartAsync(_user, experiment.Id, run.Id);
    var jobId = EvaluationSetup.JobOf(factory, evaluation.Id).JobId;

    using var response = await _user.GetAsync(
      $"/api/experiments/{experiment.Id}/evaluations/{evaluation.Id}/events", HttpCompletionOption.ResponseHeadersRead);
    var body = response.Content.ReadAsStringAsync();
    await EvaluationSetup.ApplyAsync(factory, jobId, evaluation.Id, "progress", 0.3, "plots");
    await Task.Delay(1500);
    await EvaluationSetup.ApplyAsync(factory, jobId, evaluation.Id, "succeeded", result: """{"metrics": {"r2": 0.9}}""");

    var events = Sse.Parse(await body.WaitAsync(TimeSpan.FromSeconds(10)));

    Assert.Equal("queued", (string?)events[0].Data["status"]);
    Assert.Contains(events, e => (string?)e.Data["status"] == "running");
    Assert.Equal("succeeded", (string?)events[^2].Data["status"]);
    Assert.Equal(0.9, (double?)events[^2].Data["metrics"]!["r2"]);
    Assert.Equal("end", events[^1].Type);
  }
}
