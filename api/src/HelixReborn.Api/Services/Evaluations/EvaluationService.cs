namespace HelixReborn.Api.Services.Evaluations;

using System.Text.Json;
using System.Text.Json.Nodes;
using Data;
using Experiments;
using Jobs;
using Messaging;
using Microsoft.EntityFrameworkCore;
using Models;
using Runs;
using Storage;

/// <summary><see cref="IEvaluationService"/> on the app database and RabbitMQ.</summary>
/// <param name="db">The app database.</param>
/// <param name="experiments">Checks the experiment is visible to, or owned by, the caller.</param>
/// <param name="validator">Validates the options.</param>
/// <param name="publisher">Publishes the evaluation job.</param>
/// <param name="scopes">A fresh scope per poll while following an evaluation.</param>
/// <param name="time">The clock.</param>
public class EvaluationService(
  AppDbContext db,
  IExperimentService experiments,
  EvaluationOptionsValidator validator,
  IJobPublisher publisher,
  IServiceScopeFactory scopes,
  TimeProvider time) : IEvaluationService
{
  /// <summary><see cref="Evaluation.ErrorType"/> of an evaluation whose job couldn't be published.</summary>
  public const string QueueUnavailable = "QueueUnavailable";

  /// <inheritdoc />
  public async Task<EvaluationResponse> StartAsync(
    Guid experimentId,
    Guid runId,
    JsonObject? options,
    string userId,
    CancellationToken cancellationToken = default)
  {
    await experiments.GetAsync(experimentId, userId, isAdmin: false, cancellationToken);
    var run = await db.Runs.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == runId && x.ExperimentId == experimentId, cancellationToken)
      ?? throw new RunNotFoundException(runId);

    var errors = validator.Validate(options ?? []);
    if (errors.Count > 0)
    {
      throw new EvaluationValidationException(errors);
    }

    if (run.Status != RunStatus.Succeeded)
    {
      throw new EvaluationConflictException("Only a succeeded run's model can be evaluated.");
    }

    var dataset = await db.Datasets.AsNoTracking()
      .SingleOrDefaultAsync(x => x.ExperimentId == experimentId, cancellationToken)
      ?? throw new EvaluationConflictException("The experiment has no dataset to evaluate on.");

    var jobOptions = EvaluationOptionsValidator.Read(options ?? []);
    var evaluation = new Evaluation
    {
      Id = Guid.NewGuid(),
      ExperimentId = experimentId,
      RunId = run.Id,
      JobId = Guid.NewGuid(),
      Status = EvaluationStatus.Queued,
      OptionsJson = JsonSerializer.Serialize(jobOptions, JobMessageJson.Options),
      CreatedAt = time.GetUtcNow(),
    };
    db.Evaluations.Add(evaluation);
    await db.SaveChangesAsync(cancellationToken);

    var job = new EvaluationJob(
      evaluation.JobId,
      evaluation.Id,
      run.Id,
      experimentId,
      dataset.StorageKey,
      StorageKeys.RunPrefix(experimentId, run.Id),
      StorageKeys.EvaluationPrefix(experimentId, evaluation.Id),
      jobOptions);
    try
    {
      await publisher.PublishEvaluationJobAsync(job, cancellationToken);
    }
    catch (JobPublishException ex)
    {
      evaluation.Status = EvaluationStatus.Failed;
      evaluation.ErrorType = QueueUnavailable;
      evaluation.ErrorMessage = ex.Message;
      evaluation.FinishedAt = time.GetUtcNow();
      await db.SaveChangesAsync(CancellationToken.None);
      throw new EvaluationQueueUnavailableException("The evaluation queue is unavailable; try again shortly.", ex);
    }

    return EvaluationResponse.From(evaluation, run.PipelineName);
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<EvaluationResponse>> ListAsync(
    Guid experimentId,
    Guid? runId,
    string userId,
    bool isAdmin,
    CancellationToken cancellationToken = default)
  {
    await experiments.GetAsync(experimentId, userId, isAdmin, cancellationToken);
    var evaluations = db.Evaluations.AsNoTracking().Where(x => x.ExperimentId == experimentId);
    if (runId is { } id)
    {
      evaluations = evaluations.Where(x => x.RunId == id);
    }

    return await ToResponsesAsync(db, evaluations, cancellationToken);
  }

  /// <inheritdoc />
  public async Task<EvaluationResponse> GetAsync(
    Guid experimentId,
    Guid evaluationId,
    string userId,
    bool isAdmin,
    CancellationToken cancellationToken = default)
  {
    await experiments.GetAsync(experimentId, userId, isAdmin, cancellationToken);
    return await Find(db, experimentId, evaluationId, cancellationToken)
      ?? throw new EvaluationNotFoundException(evaluationId);
  }

  /// <inheritdoc />
  public async Task<IAsyncEnumerable<EvaluationResponse>> FollowAsync(
    Guid experimentId,
    Guid evaluationId,
    string userId,
    bool isAdmin,
    CancellationToken cancellationToken = default)
  {
    var first = await GetAsync(experimentId, evaluationId, userId, isAdmin, cancellationToken);
    return JobStatusFeed.Poll(
      first,
      (services, ct) => Find(services.GetRequiredService<AppDbContext>(), experimentId, evaluationId, ct),
      IsTerminal,
      Fingerprint,
      scopes,
      time,
      cancellationToken);
  }

  /// <summary>Whether an evaluation state is final.</summary>
  /// <param name="evaluation">The state.</param>
  /// <returns>True once it's succeeded or failed.</returns>
  public static bool IsTerminal(EvaluationResponse evaluation) =>
    evaluation.Status is EvaluationStatus.Succeeded or EvaluationStatus.Failed;

  /// <summary>Loads evaluations with their run's pipeline name, newest first.</summary>
  private static async Task<IReadOnlyList<EvaluationResponse>> ToResponsesAsync(
    AppDbContext db,
    IQueryable<Evaluation> evaluations,
    CancellationToken cancellationToken)
  {
    var rows = await evaluations
      .Join(db.Runs.AsNoTracking(), e => e.RunId, r => r.Id, (e, r) => new { Evaluation = e, r.PipelineName })
      .OrderByDescending(x => x.Evaluation.CreatedAt)
      .ToListAsync(cancellationToken);
    return [.. rows.Select(x => EvaluationResponse.From(x.Evaluation, x.PipelineName))];
  }

  private static async Task<EvaluationResponse?> Find(
    AppDbContext db,
    Guid experimentId,
    Guid evaluationId,
    CancellationToken cancellationToken)
  {
    var matches = db.Evaluations.AsNoTracking().Where(x => x.Id == evaluationId && x.ExperimentId == experimentId);
    return (await ToResponsesAsync(db, matches, cancellationToken)).SingleOrDefault();
  }

  private static string Fingerprint(EvaluationResponse evaluation) =>
    $"{evaluation.Status}|{evaluation.Progress}|{evaluation.Message}|{evaluation.StartedAt}|{evaluation.FinishedAt}|{evaluation.Error}";
}
