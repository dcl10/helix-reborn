namespace HelixReborn.Api.Tests;

using System.Net.Http.Json;
using Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Models;
using Services.Artifacts;
using Services.Storage;
using Xunit;

public class RunEventHandlerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  private readonly HttpClient _user = factory.CreateUserClient("user-run-events");

  private async Task<(Guid ExperimentId, RunResponse Run, Guid JobId)> QueuedRun()
  {
    var (experiment, _, pipeline) = await RunSetup.CreateAsync(_user);
    var run = await RunSetup.StartAsync(_user, experiment.Id, pipeline.Id);
    return (experiment.Id, run, RunSetup.JobIdOf(factory, run.Id));
  }

  private async Task<RunResponse> Get(Guid experimentId, Guid runId) =>
    (await _user.GetFromJsonAsync<RunResponse>($"/api/experiments/{experimentId}/runs/{runId}"))!;

  private static string Result(Guid experimentId, Guid runId, bool withCv)
  {
    var prefix = StorageKeys.RunPrefix(experimentId, runId);
    var cvKey = withCv ? $"\"{prefix}cv_metrics.json\"" : "null";
    var cv = withCv ? """{"r2": {"mean": 0.9, "std": 0.01}}""" : "null";
    return $$"""
      {"model_key": "{{prefix}}model.onnx", "metadata_key": "{{prefix}}metadata.json", "split_key": "{{prefix}}split.json",
       "cv_metrics_key": {{cvKey}}, "cv_metrics": {{cv}}, "best_params": null, "train_rows": 2, "test_rows": 1}
      """;
  }

  private async Task PutOutputs(Guid experimentId, Guid runId, bool withCv)
  {
    var prefix = StorageKeys.RunPrefix(experimentId, runId);
    string[] names = withCv
      ? ["model.onnx", "metadata.json", "split.json", "cv_metrics.json"]
      : ["model.onnx", "metadata.json", "split.json"];
    foreach (var name in names)
    {
      await factory.ObjectStorage.PutAsync(
        $"{prefix}{name}", new MemoryStream(new byte[name.Length]), "application/octet-stream");
    }
  }

  private async Task<IReadOnlyList<Artifact>> RunArtifacts(Guid experimentId, Guid runId)
  {
    using var scope = factory.Services.CreateScope();
    return await scope.ServiceProvider.GetRequiredService<IArtifactService>()
      .ListAsync(experimentId, "run", runId);
  }

  [Fact]
  public async Task Started_MarksTheRunRunning()
  {
    var (experimentId, run, jobId) = await QueuedRun();

    await RunSetup.ApplyAsync(factory, jobId, run.Id, "started");

    var updated = await Get(experimentId, run.Id);
    Assert.Equal(RunStatus.Running, updated.Status);
    Assert.NotNull(updated.StartedAt);
  }

  [Fact]
  public async Task Progress_UpdatesProgressAndMessage()
  {
    var (experimentId, run, jobId) = await QueuedRun();
    await RunSetup.ApplyAsync(factory, jobId, run.Id, "started");

    await RunSetup.ApplyAsync(factory, jobId, run.Id, "progress", 0.5, "Hyperparameter search done");

    var updated = await Get(experimentId, run.Id);
    Assert.Equal(0.5, updated.Progress);
    Assert.Equal("Hyperparameter search done", updated.Message);
  }

  [Theory]
  [InlineData(true, 4)]
  [InlineData(false, 3)]
  public async Task Succeeded_StoresTheResult_AndRecordsTheModelFiles(bool withCv, int expectedArtifacts)
  {
    var (experimentId, run, jobId) = await QueuedRun();
    await PutOutputs(experimentId, run.Id, withCv);

    await RunSetup.ApplyAsync(factory, jobId, run.Id, "succeeded", 1, result: Result(experimentId, run.Id, withCv));

    var updated = await Get(experimentId, run.Id);
    Assert.Equal(RunStatus.Succeeded, updated.Status);
    Assert.Equal(1, updated.Progress);
    Assert.NotNull(updated.FinishedAt);
    Assert.Equal(2, (int?)updated.Result!["train_rows"]);

    var artifacts = await RunArtifacts(experimentId, run.Id);
    Assert.Equal(expectedArtifacts, artifacts.Count);
    var model = Assert.Single(artifacts, a => a.Kind == ArtifactKind.Model);
    Assert.Equal("Model (ONNX)", model.Title);
    Assert.Equal("application/octet-stream", model.ContentType);
    Assert.Equal("model.onnx".Length, model.SizeBytes);
    Assert.Contains(artifacts, a => a.Kind == ArtifactKind.ModelMetadata && a.ContentType == "application/json");
    Assert.Contains(artifacts, a => a.Kind == ArtifactKind.Split);
    Assert.Equal(withCv, artifacts.Any(a => a.Kind == ArtifactKind.CvMetrics));
  }

  [Fact]
  public async Task Succeeded_Twice_RecordsTheFilesOnce()
  {
    var (experimentId, run, jobId) = await QueuedRun();
    await PutOutputs(experimentId, run.Id, withCv: false);
    var result = Result(experimentId, run.Id, withCv: false);

    await RunSetup.ApplyAsync(factory, jobId, run.Id, "succeeded", result: result);
    await RunSetup.ApplyAsync(factory, jobId, run.Id, "succeeded", result: result);

    Assert.Equal(3, (await RunArtifacts(experimentId, run.Id)).Count);
  }

  [Fact]
  public async Task Failed_StoresTheError()
  {
    var (experimentId, run, jobId) = await QueuedRun();

    await RunSetup.ApplyAsync(
      factory, jobId, run.Id, "failed", error: new("OnnxParityError", "ONNX probabilities differ"));

    var updated = await Get(experimentId, run.Id);
    Assert.Equal(RunStatus.Failed, updated.Status);
    Assert.Equal(new RunError("OnnxParityError", "ONNX probabilities differ"), updated.Error);
    Assert.NotNull(updated.FinishedAt);
  }

  [Fact]
  public async Task EventsAfterATerminalState_AreIgnored()
  {
    var (experimentId, run, jobId) = await QueuedRun();
    await RunSetup.ApplyAsync(factory, jobId, run.Id, "failed", error: new("InvalidDataError", "Too few rows"));

    await RunSetup.ApplyAsync(factory, jobId, run.Id, "progress", 0.5, "late");
    await RunSetup.ApplyAsync(factory, jobId, run.Id, "started");

    var updated = await Get(experimentId, run.Id);
    Assert.Equal(RunStatus.Failed, updated.Status);
    Assert.Null(updated.Progress);
    Assert.Null(updated.Message);
  }

  [Fact]
  public async Task Progress_BeforeStarted_StillMarksTheRunRunning()
  {
    var (experimentId, run, jobId) = await QueuedRun();

    await RunSetup.ApplyAsync(factory, jobId, run.Id, "progress", 0.1, "Split data");

    var updated = await Get(experimentId, run.Id);
    Assert.Equal(RunStatus.Running, updated.Status);
    Assert.NotNull(updated.StartedAt);
  }

  [Fact]
  public async Task AnUnknownRun_IsIgnored()
  {
    await RunSetup.ApplyAsync(factory, Guid.NewGuid(), Guid.NewGuid(), "started");
  }

  [Fact]
  public async Task AnotherJobsEvent_IsIgnored()
  {
    var (experimentId, run, _) = await QueuedRun();

    await RunSetup.ApplyAsync(factory, Guid.NewGuid(), run.Id, "started");

    Assert.Equal(RunStatus.Queued, (await Get(experimentId, run.Id)).Status);
  }
}
