namespace HelixReborn.Api.Tests.Fixtures;

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Messaging;
using Microsoft.Extensions.DependencyInjection;
using Models;

/// <summary>Builds what an evaluation needs: a succeeded run, and events to apply to an evaluation.</summary>
public static class EvaluationSetup
{
  /// <summary>Creates an experiment with a dataset and pipeline, runs it, and marks the run succeeded.</summary>
  /// <param name="factory">The test host.</param>
  /// <param name="client">The owner's client.</param>
  /// <returns>The experiment, its dataset and the succeeded run.</returns>
  public static async Task<(ExperimentResponse Experiment, DatasetResponse Dataset, RunResponse Run)> SucceededRunAsync(
    ApiFactory factory,
    HttpClient client)
  {
    var (experiment, dataset, run) = await QueuedRunAsync(client);
    await RunSetup.ApplyAsync(factory, RunSetup.JobIdOf(factory, run.Id), run.Id, "succeeded", result: "{}");
    return (experiment, dataset, run);
  }

  /// <summary>Creates an experiment with a dataset and pipeline, and starts a run (left queued).</summary>
  /// <param name="client">The owner's client.</param>
  /// <returns>The experiment, its dataset and the queued run.</returns>
  public static async Task<(ExperimentResponse Experiment, DatasetResponse Dataset, RunResponse Run)> QueuedRunAsync(
    HttpClient client)
  {
    var (experiment, dataset, pipeline) = await RunSetup.CreateAsync(client);
    var run = await RunSetup.StartAsync(client, experiment.Id, pipeline.Id);
    return (experiment, dataset, run);
  }

  /// <summary>Starts an evaluation of a run and returns it.</summary>
  /// <param name="client">The owner's client.</param>
  /// <param name="experimentId">The experiment.</param>
  /// <param name="runId">The run.</param>
  /// <param name="options">The options JSON; none by default.</param>
  /// <returns>The queued evaluation.</returns>
  public static async Task<EvaluationResponse> StartAsync(
    HttpClient client,
    Guid experimentId,
    Guid runId,
    string? options = null)
  {
    var response = await PostAsync(client, experimentId, runId, options);
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<EvaluationResponse>())!;
  }

  /// <summary>Posts a start request.</summary>
  /// <param name="client">The caller's client.</param>
  /// <param name="experimentId">The experiment.</param>
  /// <param name="runId">The run.</param>
  /// <param name="options">The options JSON, or null to send no options.</param>
  /// <returns>The response.</returns>
  public static Task<HttpResponseMessage> PostAsync(
    HttpClient client,
    Guid experimentId,
    Guid runId,
    string? options = null) =>
    client.PostAsJsonAsync(
      $"/api/experiments/{experimentId}/runs/{runId}/evaluations",
      new { options = options is null ? null : JsonNode.Parse(options) });

  /// <summary>Applies an evaluation job event, as the consumer would.</summary>
  /// <param name="factory">The test host.</param>
  /// <param name="jobId">The evaluation's job ID.</param>
  /// <param name="evaluationId">The evaluation.</param>
  /// <param name="status">The event status.</param>
  /// <param name="progress">The progress, if any.</param>
  /// <param name="message">The message, if any.</param>
  /// <param name="result">The result JSON, for <c>succeeded</c>.</param>
  /// <param name="error">The error, for <c>failed</c>.</param>
  /// <returns>A task that completes once the event is applied.</returns>
  public static async Task ApplyAsync(
    ApiFactory factory,
    Guid jobId,
    Guid evaluationId,
    string status,
    double? progress = null,
    string? message = null,
    string? result = null,
    JobError? error = null)
  {
    using var scope = factory.Services.CreateScope();
    var handler = scope.ServiceProvider.GetServices<IJobEventHandler>().Single(h => h.JobType == "evaluation");
    var jobEvent = new JobEvent(
      1,
      jobId,
      "evaluation",
      evaluationId,
      status,
      progress,
      message,
      result is null ? null : JsonDocument.Parse(result).RootElement.Clone(),
      error,
      DateTimeOffset.UtcNow);
    await handler.HandleAsync(jobEvent, CancellationToken.None);
  }

  /// <summary>Gets an evaluation's job from what the fake publisher recorded.</summary>
  /// <param name="factory">The test host.</param>
  /// <param name="evaluationId">The evaluation.</param>
  /// <returns>The evaluation's job.</returns>
  public static EvaluationJob JobOf(ApiFactory factory, Guid evaluationId) =>
    factory.Jobs.EvaluationJobs.Single(j => j.EvaluationId == evaluationId);
}
