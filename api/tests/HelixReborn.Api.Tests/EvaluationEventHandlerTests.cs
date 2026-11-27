namespace HelixReborn.Api.Tests;

using System.Net.Http.Json;
using Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Models;
using Services.Artifacts;
using Services.Evaluations;
using Services.Storage;
using Xunit;

public class EvaluationEventHandlerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  private readonly HttpClient _user = factory.CreateUserClient("user-evaluation-events");

  private async Task<(Guid ExperimentId, EvaluationResponse Evaluation, Guid JobId)> QueuedEvaluation()
  {
    var (experiment, _, run) = await EvaluationSetup.SucceededRunAsync(factory, _user);
    var evaluation = await EvaluationSetup.StartAsync(_user, experiment.Id, run.Id);
    return (experiment.Id, evaluation, EvaluationSetup.JobOf(factory, evaluation.Id).JobId);
  }

  private async Task<EvaluationResponse> Get(Guid experimentId, Guid evaluationId) =>
    (await _user.GetFromJsonAsync<EvaluationResponse>($"/api/experiments/{experimentId}/evaluations/{evaluationId}"))!;

  private static string Result(string prefix) => $$"""
    {"metrics": {"r2": 0.91, "mae": 0.2},
     "artifacts": [
       {"key": "{{prefix}}metrics.json", "kind": "json", "title": "Metrics"},
       {"key": "{{prefix}}plots/predicted_vs_actual.png", "kind": "plot", "title": "Predicted vs actual"},
       {"key": "{{prefix}}tables/global_shap.csv", "kind": "table", "title": "SHAP importance (mean |SHAP|)"}
     ]}
    """;

  private async Task PutOutputs(string prefix)
  {
    foreach (var name in new[] { "metrics.json", "plots/predicted_vs_actual.png", "tables/global_shap.csv" })
    {
      await factory.ObjectStorage.PutAsync($"{prefix}{name}", new MemoryStream(new byte[name.Length]), "application/octet-stream");
    }
  }

  private async Task<IReadOnlyList<Artifact>> EvaluationArtifacts(Guid experimentId, Guid evaluationId)
  {
    using var scope = factory.Services.CreateScope();
    return await scope.ServiceProvider.GetRequiredService<IArtifactService>()
      .ListAsync(experimentId, "evaluation", evaluationId);
  }

  [Fact]
  public async Task Progress_MarksItRunning_AndUpdatesProgress()
  {
    var (experimentId, evaluation, jobId) = await QueuedEvaluation();

    await EvaluationSetup.ApplyAsync(factory, jobId, evaluation.Id, "progress", 0.4, "global_importance");

    var updated = await Get(experimentId, evaluation.Id);
    Assert.Equal(EvaluationStatus.Running, updated.Status);
    Assert.Equal(0.4, updated.Progress);
    Assert.Equal("global_importance", updated.Message);
    Assert.NotNull(updated.StartedAt);
  }

  [Fact]
  public async Task Succeeded_StoresTheMetrics_AndRecordsTheArtifactsWithTheirKinds()
  {
    var (experimentId, evaluation, jobId) = await QueuedEvaluation();
    var prefix = StorageKeys.EvaluationPrefix(experimentId, evaluation.Id);
    await PutOutputs(prefix);

    await EvaluationSetup.ApplyAsync(factory, jobId, evaluation.Id, "succeeded", result: Result(prefix));

    var updated = await Get(experimentId, evaluation.Id);
    Assert.Equal(EvaluationStatus.Succeeded, updated.Status);
    Assert.Equal(1, updated.Progress);
    Assert.Equal(0.91, (double?)updated.Metrics!["r2"]);
    Assert.NotNull(updated.FinishedAt);

    var artifacts = (await EvaluationArtifacts(experimentId, evaluation.Id)).ToDictionary(a => a.StorageKey);
    Assert.Equal(3, artifacts.Count);
    Assert.Equal(
      (ArtifactKind.Metrics, "Metrics", "application/json", "metrics.json".Length),
      Describe(artifacts[$"{prefix}metrics.json"]));
    Assert.Equal(
      (ArtifactKind.EvaluationPlot, "Predicted vs actual", "image/png", "plots/predicted_vs_actual.png".Length),
      Describe(artifacts[$"{prefix}plots/predicted_vs_actual.png"]));
    Assert.Equal(
      (ArtifactKind.EvaluationTable, "SHAP importance (mean |SHAP|)", "text/csv", "tables/global_shap.csv".Length),
      Describe(artifacts[$"{prefix}tables/global_shap.csv"]));
  }

  [Fact]
  public async Task Succeeded_Redelivered_DoesntDuplicateArtifacts()
  {
    var (experimentId, evaluation, jobId) = await QueuedEvaluation();
    var prefix = StorageKeys.EvaluationPrefix(experimentId, evaluation.Id);
    await PutOutputs(prefix);

    await EvaluationSetup.ApplyAsync(factory, jobId, evaluation.Id, "succeeded", result: Result(prefix));
    await EvaluationSetup.ApplyAsync(factory, jobId, evaluation.Id, "succeeded", result: Result(prefix));

    Assert.Equal(3, (await EvaluationArtifacts(experimentId, evaluation.Id)).Count);
  }

  [Fact]
  public async Task Succeeded_SkipsFilesThatArentInStorage()
  {
    var (experimentId, evaluation, jobId) = await QueuedEvaluation();
    var prefix = StorageKeys.EvaluationPrefix(experimentId, evaluation.Id);

    await EvaluationSetup.ApplyAsync(factory, jobId, evaluation.Id, "succeeded", result: Result(prefix));

    Assert.Empty(await EvaluationArtifacts(experimentId, evaluation.Id));
    Assert.Equal(EvaluationStatus.Succeeded, (await Get(experimentId, evaluation.Id)).Status);
  }

  [Fact]
  public async Task Failed_StoresTheError()
  {
    var (experimentId, evaluation, jobId) = await QueuedEvaluation();

    await EvaluationSetup.ApplyAsync(
      factory, jobId, evaluation.Id, "failed", error: new("InvalidDataError", "Target value 'x' isn't a class"));

    var updated = await Get(experimentId, evaluation.Id);
    Assert.Equal(EvaluationStatus.Failed, updated.Status);
    Assert.Equal(new EvaluationError("InvalidDataError", "Target value 'x' isn't a class"), updated.Error);
    Assert.NotNull(updated.FinishedAt);
  }

  [Fact]
  public async Task StaleEvents_AfterATerminalState_AreIgnored()
  {
    var (experimentId, evaluation, jobId) = await QueuedEvaluation();
    await EvaluationSetup.ApplyAsync(factory, jobId, evaluation.Id, "failed", error: new("InvalidSpecError", "No"));

    await EvaluationSetup.ApplyAsync(factory, jobId, evaluation.Id, "progress", 0.5, "late");
    await EvaluationSetup.ApplyAsync(factory, jobId, evaluation.Id, "succeeded", result: """{"metrics": {}}""");

    var updated = await Get(experimentId, evaluation.Id);
    Assert.Equal(EvaluationStatus.Failed, updated.Status);
    Assert.Null(updated.Progress);
    Assert.Null(updated.Metrics);
  }

  [Fact]
  public async Task EventsForAnotherJob_AreIgnored()
  {
    var (experimentId, evaluation, _) = await QueuedEvaluation();

    await EvaluationSetup.ApplyAsync(factory, Guid.NewGuid(), evaluation.Id, "failed", error: new("X", "Y"));

    Assert.Equal(EvaluationStatus.Queued, (await Get(experimentId, evaluation.Id)).Status);
  }

  [Theory]
  [InlineData("plot", ArtifactKind.EvaluationPlot)]
  [InlineData("table", ArtifactKind.EvaluationTable)]
  [InlineData("json", ArtifactKind.Metrics)]
  [InlineData("video", null)]
  public void KindOf_MapsResultKindsToArtifactKinds(string kind, ArtifactKind? expected) =>
    Assert.Equal(expected, EvaluationEventHandler.KindOf(kind));

  private static (ArtifactKind, string, string, long) Describe(Artifact artifact) =>
    (artifact.Kind, artifact.Title, artifact.ContentType, artifact.SizeBytes);
}
