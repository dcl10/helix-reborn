namespace HelixReborn.Api.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Fixtures;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Models;
using Services.Storage;
using Xunit;

public class RunsControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  private readonly HttpClient _user = factory.CreateUserClient("user-runs");

  [Fact]
  public async Task Start_Returns202_PublishesTheJob_AndStoresAQueuedRun()
  {
    var (experiment, dataset, pipeline) = await RunSetup.CreateAsync(_user);

    var response = await _user.PostAsync($"/api/experiments/{experiment.Id}/pipelines/{pipeline.Id}/runs", null);

    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    var run = (await response.Content.ReadFromJsonAsync<RunResponse>())!;
    Assert.Equal(RunStatus.Queued, run.Status);
    Assert.Equal(pipeline.Id, run.PipelineId);
    Assert.Equal("Ridge", run.PipelineName);
    Assert.Equal($"/api/experiments/{experiment.Id}/runs/{run.Id}", response.Headers.Location?.OriginalString);

    var job = Assert.Single(factory.Jobs.JobsFor(experiment.Id));
    Assert.Equal(run.Id, job.RunId);
    Assert.Equal(experiment.Id, job.ExperimentId);
    Assert.Equal(StorageKeys.Dataset(experiment.Id, dataset.Id, dataset.FileName), job.DatasetKey);
    Assert.Equal($"experiments/{experiment.Id:D}/runs/{run.Id:D}/", job.OutputPrefix);
    Assert.True(JsonNode.DeepEquals(JsonNode.Parse(job.Pipeline.GetRawText()), pipeline.Spec), job.Pipeline.GetRawText());
    Assert.True(JsonNode.DeepEquals(run.Spec, pipeline.Spec));

    var stored = await _user.GetFromJsonAsync<RunResponse>($"/api/experiments/{experiment.Id}/runs/{run.Id}");
    Assert.Equal(RunStatus.Queued, stored!.Status);
  }

  [Fact]
  public async Task Start_Returns400_WhenTheSpecNoLongerFitsTheCatalog()
  {
    var (experiment, _, pipeline) = await RunSetup.CreateAsync(_user);
    await using (var scope = factory.Services.CreateAsyncScope())
    {
      // Simulate a catalog change that makes the stored spec invalid.
      var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
      var stored = await db.Pipelines.FindAsync(pipeline.Id);
      stored!.SpecJson = stored.SpecJson.Replace("\"ridge\"", "\"retired_model\"", StringComparison.Ordinal);
      await db.SaveChangesAsync();
    }

    var response = await _user.PostAsync($"/api/experiments/{experiment.Id}/pipelines/{pipeline.Id}/runs", null);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
    Assert.Contains("model.step: unknown step 'retired_model'", problem!.Errors["spec"]);
    Assert.Empty(factory.Jobs.JobsFor(experiment.Id));
  }

  [Fact]
  public async Task Start_Returns503_AndFailsTheRun_WhenPublishingFails()
  {
    var (experiment, _, pipeline) = await RunSetup.CreateAsync(_user);
    factory.Jobs.FailFor(experiment.Id);

    var response = await _user.PostAsync($"/api/experiments/{experiment.Id}/pipelines/{pipeline.Id}/runs", null);

    Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    var run = Assert.Single((await _user.GetFromJsonAsync<List<RunResponse>>($"/api/experiments/{experiment.Id}/runs"))!);
    Assert.Equal(RunStatus.Failed, run.Status);
    Assert.Equal("QueueUnavailable", run.Error?.Type);
    Assert.NotNull(run.FinishedAt);
  }

  [Fact]
  public async Task AnotherUsersExperiment_Returns404()
  {
    var other = factory.CreateUserClient("user-runs-other");
    var (experiment, _, pipeline) = await RunSetup.CreateAsync(other);
    var run = await RunSetup.StartAsync(other, experiment.Id, pipeline.Id);
    var runs = $"/api/experiments/{experiment.Id}/runs";

    Assert.Equal(
      HttpStatusCode.NotFound,
      (await _user.PostAsync($"/api/experiments/{experiment.Id}/pipelines/{pipeline.Id}/runs", null)).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await _user.GetAsync(runs)).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await _user.GetAsync($"{runs}/{run.Id}")).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await _user.GetAsync($"{runs}/{run.Id}/events")).StatusCode);
  }

  [Fact]
  public async Task Admin_CanReadRuns_ButNotStartThem()
  {
    var (experiment, _, pipeline) = await RunSetup.CreateAsync(_user);
    var run = await RunSetup.StartAsync(_user, experiment.Id, pipeline.Id);
    var admin = factory.CreateAdminClient();

    Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/experiments/{experiment.Id}/runs/{run.Id}")).StatusCode);
    Assert.Equal(
      HttpStatusCode.NotFound,
      (await admin.PostAsync($"/api/experiments/{experiment.Id}/pipelines/{pipeline.Id}/runs", null)).StatusCode);
  }

  [Fact]
  public async Task List_ReturnsNewestFirst()
  {
    var (experiment, _, pipeline) = await RunSetup.CreateAsync(_user);
    var first = await RunSetup.StartAsync(_user, experiment.Id, pipeline.Id);
    var second = await RunSetup.StartAsync(_user, experiment.Id, pipeline.Id);

    var runs = await _user.GetFromJsonAsync<List<RunResponse>>($"/api/experiments/{experiment.Id}/runs");

    Assert.Equal([second.Id, first.Id], runs!.Select(r => r.Id));
  }

  [Fact]
  public async Task DeletingThePipeline_KeepsTheRunWithItsSnapshot()
  {
    var (experiment, _, pipeline) = await RunSetup.CreateAsync(_user);
    var run = await RunSetup.StartAsync(_user, experiment.Id, pipeline.Id);

    (await _user.DeleteAsync($"/api/experiments/{experiment.Id}/pipelines/{pipeline.Id}")).EnsureSuccessStatusCode();

    var kept = await _user.GetFromJsonAsync<RunResponse>($"/api/experiments/{experiment.Id}/runs/{run.Id}");
    Assert.Null(kept!.PipelineId);
    Assert.Equal("Ridge", kept.PipelineName);
    Assert.Equal("ridge", (string?)kept.Spec["model"]!["step"]);
  }

  [Fact]
  public async Task Events_StreamsOneStatusThenEnd_ForATerminalRun()
  {
    var (experiment, _, pipeline) = await RunSetup.CreateAsync(_user);
    var run = await RunSetup.StartAsync(_user, experiment.Id, pipeline.Id);
    await RunSetup.ApplyAsync(
      factory, RunSetup.JobIdOf(factory, run.Id), run.Id, "failed", error: new("InvalidDataError", "Too few rows"));

    var response = await _user.GetAsync($"/api/experiments/{experiment.Id}/runs/{run.Id}/events");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    var events = Sse.Parse(await response.Content.ReadAsStringAsync());
    Assert.Equal(["status", "end"], events.Select(e => e.Type));
    Assert.Equal("failed", (string?)events[0].Data["status"]);
    Assert.Equal("InvalidDataError", (string?)events[0].Data["error"]!["type"]);
    Assert.Equal(run.Id.ToString(), (string?)events[0].Data["id"]);
    Assert.Equal("failed", (string?)events[1].Data["status"]);
  }

  [Fact]
  public async Task Events_StreamsChanges_UntilTheRunEnds()
  {
    var (experiment, _, pipeline) = await RunSetup.CreateAsync(_user);
    var run = await RunSetup.StartAsync(_user, experiment.Id, pipeline.Id);
    var jobId = RunSetup.JobIdOf(factory, run.Id);

    using var response = await _user.GetAsync(
      $"/api/experiments/{experiment.Id}/runs/{run.Id}/events", HttpCompletionOption.ResponseHeadersRead);
    var body = response.Content.ReadAsStringAsync();
    await RunSetup.ApplyAsync(factory, jobId, run.Id, "started");
    await Task.Delay(1500);
    await RunSetup.ApplyAsync(factory, jobId, run.Id, "failed", error: new("InvalidDataError", "Too few rows"));

    var events = Sse.Parse(await body.WaitAsync(TimeSpan.FromSeconds(10)));

    Assert.Equal("queued", (string?)events[0].Data["status"]);
    Assert.Equal("end", events[^1].Type);
    Assert.Equal("failed", (string?)events[^2].Data["status"]);
    Assert.Contains(events, e => (string?)e.Data["status"] == "running");
  }
}
