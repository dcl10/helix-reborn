namespace HelixReborn.Api.Services.Runs;

using Experiments;
using Models;
using Pipelines;

/// <summary>
/// An experiment's training runs. Reads are for the owner or <c>helix-admin</c>; starting a run is owner only, so an
/// admin's is a <see cref="ExperimentNotFoundException"/>, as for experiments.
/// </summary>
public interface IRunService
{
  /// <summary>
  /// Starts a run of a pipeline: re-validates its spec against the current dataset, stores a <c>Queued</c> run with a
  /// snapshot of the spec, and publishes its <c>TrainingJob</c>.
  /// </summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="pipelineId">The pipeline to run.</param>
  /// <param name="userId">The caller's Keycloak user ID; must be the owner.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The queued run.</returns>
  /// <exception cref="ExperimentNotFoundException">No such experiment, or the caller doesn't own it.</exception>
  /// <exception cref="PipelineNotFoundException">No such pipeline in the experiment.</exception>
  /// <exception cref="PipelineConflictException">The experiment has no dataset.</exception>
  /// <exception cref="PipelineValidationException">The spec no longer fits the dataset or catalog.</exception>
  /// <exception cref="RunQueueUnavailableException">
  /// The job couldn't be published; the run is stored as <c>Failed</c> with <c>ErrorType</c> <c>QueueUnavailable</c>.
  /// </exception>
  Task<RunResponse> StartAsync(
    Guid experimentId,
    Guid pipelineId,
    string userId,
    CancellationToken cancellationToken = default);

  /// <summary>Lists an experiment's runs, newest first.</summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="userId">The caller's Keycloak user ID.</param>
  /// <param name="isAdmin">Whether the caller has <c>helix-admin</c>.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The runs.</returns>
  /// <exception cref="ExperimentNotFoundException">No such experiment, or it isn't visible to the caller.</exception>
  Task<IReadOnlyList<RunResponse>> ListAsync(
    Guid experimentId,
    string userId,
    bool isAdmin,
    CancellationToken cancellationToken = default);

  /// <summary>Gets one run.</summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="runId">The run's ID.</param>
  /// <param name="userId">The caller's Keycloak user ID.</param>
  /// <param name="isAdmin">Whether the caller has <c>helix-admin</c>.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The run.</returns>
  /// <exception cref="ExperimentNotFoundException">No such experiment, or it isn't visible to the caller.</exception>
  /// <exception cref="RunNotFoundException">No such run in the experiment.</exception>
  Task<RunResponse> GetAsync(
    Guid experimentId,
    Guid runId,
    string userId,
    bool isAdmin,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Follows a run: yields it now, then again whenever it changes (polled every second, each poll in a fresh DI
  /// scope), and finishes once it's terminal or gone. Checks access before returning, so the caller can answer 404
  /// before starting a stream.
  /// </summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="runId">The run's ID.</param>
  /// <param name="userId">The caller's Keycloak user ID.</param>
  /// <param name="isAdmin">Whether the caller has <c>helix-admin</c>.</param>
  /// <param name="cancellationToken">Cancels the check; the stream stops when its enumeration is cancelled.</param>
  /// <returns>The run's successive states.</returns>
  /// <exception cref="ExperimentNotFoundException">No such experiment, or it isn't visible to the caller.</exception>
  /// <exception cref="RunNotFoundException">No such run in the experiment.</exception>
  Task<IAsyncEnumerable<RunResponse>> FollowAsync(
    Guid experimentId,
    Guid runId,
    string userId,
    bool isAdmin,
    CancellationToken cancellationToken = default);
}

/// <summary>No such run in the experiment. The API answers 404.</summary>
/// <param name="runId">The run ID that was requested.</param>
public class RunNotFoundException(Guid runId) : Exception($"Run {runId} doesn't exist.");

/// <summary>The run's job couldn't be published to RabbitMQ. The API answers 503.</summary>
/// <param name="message">What went wrong.</param>
/// <param name="innerException">The publish error.</param>
public class RunQueueUnavailableException(string message, Exception innerException) : Exception(message, innerException);
