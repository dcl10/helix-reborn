namespace HelixReborn.Api.Services.Runs;

using System.Text.Json;
using System.Text.Json.Nodes;
using Data;
using Experiments;
using Jobs;
using Messaging;
using Microsoft.EntityFrameworkCore;
using Models;
using Pipelines;
using Storage;

/// <summary><see cref="IRunService"/> on the app database and RabbitMQ.</summary>
/// <param name="db">The app database.</param>
/// <param name="experiments">Checks the experiment is visible to, or owned by, the caller.</param>
/// <param name="validator">Re-validates the spec before running it.</param>
/// <param name="publisher">Publishes the training job.</param>
/// <param name="scopes">A fresh scope per poll while following a run.</param>
/// <param name="time">The clock.</param>
public class RunService(
  AppDbContext db,
  IExperimentService experiments,
  PipelineSpecValidator validator,
  IJobPublisher publisher,
  IServiceScopeFactory scopes,
  TimeProvider time) : IRunService
{
  /// <summary><see cref="Run.ErrorType"/> of a run whose job couldn't be published.</summary>
  public const string QueueUnavailable = "QueueUnavailable";

  /// <inheritdoc />
  public async Task<RunResponse> StartAsync(
    Guid experimentId,
    Guid pipelineId,
    string userId,
    CancellationToken cancellationToken = default)
  {
    await experiments.GetAsync(experimentId, userId, isAdmin: false, cancellationToken);
    var pipeline = await db.Pipelines.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == pipelineId && x.ExperimentId == experimentId, cancellationToken)
      ?? throw new PipelineNotFoundException(pipelineId);
    var dataset = await db.Datasets.AsNoTracking()
      .SingleOrDefaultAsync(x => x.ExperimentId == experimentId, cancellationToken)
      ?? throw new PipelineConflictException("Upload a dataset before running a pipeline.");

    // The dataset or catalog may have changed since the pipeline was saved.
    var errors = validator.Validate(PipelineSpecJson.Read(JsonNode.Parse(pipeline.SpecJson)!.AsObject()), dataset);
    if (errors.Count > 0)
    {
      throw new PipelineValidationException(errors);
    }

    var run = new Run
    {
      Id = Guid.NewGuid(),
      ExperimentId = experimentId,
      PipelineId = pipeline.Id,
      PipelineName = pipeline.Name,
      SpecJson = pipeline.SpecJson,
      JobId = Guid.NewGuid(),
      Status = RunStatus.Queued,
      CreatedAt = time.GetUtcNow(),
    };
    db.Runs.Add(run);
    await db.SaveChangesAsync(cancellationToken);

    using var spec = JsonDocument.Parse(run.SpecJson);
    var job = new TrainingJob(
      run.JobId,
      run.Id,
      experimentId,
      dataset.StorageKey,
      StorageKeys.RunPrefix(experimentId, run.Id),
      spec.RootElement.Clone());
    try
    {
      await publisher.PublishTrainingJobAsync(job, cancellationToken);
    }
    catch (JobPublishException ex)
    {
      run.Status = RunStatus.Failed;
      run.ErrorType = QueueUnavailable;
      run.ErrorMessage = ex.Message;
      run.FinishedAt = time.GetUtcNow();
      await db.SaveChangesAsync(CancellationToken.None);
      throw new RunQueueUnavailableException("The training queue is unavailable; try again shortly.", ex);
    }

    return RunResponse.From(run);
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<RunResponse>> ListAsync(
    Guid experimentId,
    string userId,
    bool isAdmin,
    CancellationToken cancellationToken = default)
  {
    await experiments.GetAsync(experimentId, userId, isAdmin, cancellationToken);
    var runs = await db.Runs.AsNoTracking()
      .Where(x => x.ExperimentId == experimentId)
      .OrderByDescending(x => x.CreatedAt)
      .ToListAsync(cancellationToken);

    return [.. runs.Select(RunResponse.From)];
  }

  /// <inheritdoc />
  public async Task<RunResponse> GetAsync(
    Guid experimentId,
    Guid runId,
    string userId,
    bool isAdmin,
    CancellationToken cancellationToken = default)
  {
    await experiments.GetAsync(experimentId, userId, isAdmin, cancellationToken);
    var run = await Find(db, experimentId, runId, cancellationToken) ?? throw new RunNotFoundException(runId);
    return RunResponse.From(run);
  }

  /// <inheritdoc />
  public async Task<IAsyncEnumerable<RunResponse>> FollowAsync(
    Guid experimentId,
    Guid runId,
    string userId,
    bool isAdmin,
    CancellationToken cancellationToken = default)
  {
    var first = await GetAsync(experimentId, runId, userId, isAdmin, cancellationToken);
    return JobStatusFeed.Poll(
      first,
      async (services, ct) =>
        await Find(services.GetRequiredService<AppDbContext>(), experimentId, runId, ct) is { } run
          ? RunResponse.From(run)
          : null,
      run => run.Status is RunStatus.Succeeded or RunStatus.Failed,
      Fingerprint,
      scopes,
      time,
      cancellationToken);
  }

  private static Task<Run?> Find(AppDbContext db, Guid experimentId, Guid runId, CancellationToken cancellationToken) =>
    db.Runs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId && x.ExperimentId == experimentId, cancellationToken);

  private static string Fingerprint(RunResponse run) =>
    $"{run.Status}|{run.Progress}|{run.Message}|{run.StartedAt}|{run.FinishedAt}|{run.Error}";
}
