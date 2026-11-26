namespace HelixReborn.Api.Tests.Fixtures;

using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Messaging;
using Microsoft.Extensions.DependencyInjection;
using Models;

/// <summary>Builds what a run needs: an experiment with a dataset and a pipeline, and events to apply to it.</summary>
public static class RunSetup
{
  /// <summary>A valid regression spec for <see cref="Csv"/>'s columns.</summary>
  public static JsonObject Spec() => JsonNode.Parse("""
    {"problem_type": "regression", "target_column": "y", "feature_columns": ["a", "b"],
     "split": {"test_size": 0.25, "random_seed": 7},
     "steps": [{"step": "standard_scaler"}], "model": {"step": "ridge", "params": {"alpha": 0.5}}}
    """)!.AsObject();

  /// <summary>The dataset uploaded for runs: numeric a, b and y.</summary>
  public const string Csv = "a,b,y\n1,2,3.5\n4,5,6.5\n7,8,9.5\n";

  /// <summary>Creates an experiment, uploads <see cref="Csv"/> and saves a pipeline.</summary>
  /// <param name="client">The owner's client.</param>
  /// <param name="spec">The pipeline spec; <see cref="Spec"/> by default.</param>
  /// <returns>The experiment, its dataset and the pipeline.</returns>
  public static async Task<(ExperimentResponse Experiment, DatasetResponse Dataset, PipelineResponse Pipeline)> CreateAsync(
    HttpClient client,
    JsonObject? spec = null)
  {
    var created = await client.PostAsJsonAsync("/api/experiments", new { name = "Runs", description = (string?)null });
    created.EnsureSuccessStatusCode();
    var experiment = (await created.Content.ReadFromJsonAsync<ExperimentResponse>())!;

    using var content = new MultipartFormDataContent();
    var file = new ByteArrayContent(Encoding.UTF8.GetBytes(Csv));
    file.Headers.ContentType = new("text/csv");
    content.Add(file, "file", "data.csv");
    var uploaded = await client.PostAsync($"/api/experiments/{experiment.Id}/dataset", content);
    uploaded.EnsureSuccessStatusCode();
    var dataset = (await uploaded.Content.ReadFromJsonAsync<DatasetResponse>())!;

    var saved = await client.PostAsJsonAsync(
      $"/api/experiments/{experiment.Id}/pipelines", new { name = "Ridge", spec = spec ?? Spec() });
    saved.EnsureSuccessStatusCode();
    var pipeline = (await saved.Content.ReadFromJsonAsync<PipelineResponse>())!;

    return (experiment, dataset, pipeline);
  }

  /// <summary>Starts a run of the pipeline and returns it.</summary>
  /// <param name="client">The owner's client.</param>
  /// <param name="experimentId">The experiment.</param>
  /// <param name="pipelineId">The pipeline.</param>
  /// <returns>The queued run.</returns>
  public static async Task<RunResponse> StartAsync(HttpClient client, Guid experimentId, Guid pipelineId)
  {
    var response = await client.PostAsync($"/api/experiments/{experimentId}/pipelines/{pipelineId}/runs", null);
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<RunResponse>())!;
  }

  /// <summary>Applies a training job event to a run, as the consumer would.</summary>
  /// <param name="factory">The test host.</param>
  /// <param name="jobId">The run's job ID.</param>
  /// <param name="runId">The run.</param>
  /// <param name="status">The event status.</param>
  /// <param name="progress">The progress, if any.</param>
  /// <param name="message">The message, if any.</param>
  /// <param name="result">The result JSON, for <c>succeeded</c>.</param>
  /// <param name="error">The error, for <c>failed</c>.</param>
  /// <returns>A task that completes once the event is applied.</returns>
  public static async Task ApplyAsync(
    ApiFactory factory,
    Guid jobId,
    Guid runId,
    string status,
    double? progress = null,
    string? message = null,
    string? result = null,
    JobError? error = null)
  {
    using var scope = factory.Services.CreateScope();
    var handler = scope.ServiceProvider.GetServices<IJobEventHandler>().Single(h => h.JobType == "training");
    var jobEvent = new JobEvent(
      1,
      jobId,
      "training",
      runId,
      status,
      progress,
      message,
      result is null ? null : JsonDocument.Parse(result).RootElement.Clone(),
      error,
      DateTimeOffset.UtcNow);
    await handler.HandleAsync(jobEvent, CancellationToken.None);
  }

  /// <summary>Gets a job's ID from what the fake publisher recorded.</summary>
  /// <param name="factory">The test host.</param>
  /// <param name="runId">The run.</param>
  /// <returns>The run's job ID.</returns>
  public static Guid JobIdOf(ApiFactory factory, Guid runId) =>
    factory.Jobs.TrainingJobs.Single(j => j.RunId == runId).JobId;
}
