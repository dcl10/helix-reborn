namespace HelixReborn.Api.Services.Evaluations;

using System.Text.Json.Nodes;
using Experiments;
using Models;
using Runs;

/// <summary>
/// An experiment's evaluations of its runs' models. Reads are for the owner or <c>helix-admin</c>; starting one is
/// owner only, so an admin's is a <see cref="ExperimentNotFoundException"/>, as for runs.
/// </summary>
public interface IEvaluationService
{
  /// <summary>
  /// Starts an evaluation of a succeeded run's model: validates the options, stores a <c>Queued</c> evaluation with
  /// the options (defaults filled in), and publishes its <c>EvaluationJob</c>.
  /// </summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="runId">The run whose model to evaluate.</param>
  /// <param name="options">The snake_case options; null or missing fields take the defaults.</param>
  /// <param name="userId">The caller's Keycloak user ID; must be the owner.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The queued evaluation.</returns>
  /// <exception cref="ExperimentNotFoundException">No such experiment, or the caller doesn't own it.</exception>
  /// <exception cref="RunNotFoundException">No such run in the experiment.</exception>
  /// <exception cref="EvaluationValidationException">The options are invalid.</exception>
  /// <exception cref="EvaluationConflictException">The run hasn't succeeded, or the experiment has no dataset.</exception>
  /// <exception cref="EvaluationQueueUnavailableException">
  /// The job couldn't be published; the evaluation is stored as <c>Failed</c> with <c>ErrorType</c>
  /// <c>QueueUnavailable</c>.
  /// </exception>
  Task<EvaluationResponse> StartAsync(
    Guid experimentId,
    Guid runId,
    JsonObject? options,
    string userId,
    CancellationToken cancellationToken = default);

  /// <summary>Lists an experiment's evaluations, newest first.</summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="runId">When given, only this run's evaluations.</param>
  /// <param name="userId">The caller's Keycloak user ID.</param>
  /// <param name="isAdmin">Whether the caller has <c>helix-admin</c>.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The evaluations.</returns>
  /// <exception cref="ExperimentNotFoundException">No such experiment, or it isn't visible to the caller.</exception>
  Task<IReadOnlyList<EvaluationResponse>> ListAsync(
    Guid experimentId,
    Guid? runId,
    string userId,
    bool isAdmin,
    CancellationToken cancellationToken = default);

  /// <summary>Gets one evaluation.</summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="evaluationId">The evaluation's ID.</param>
  /// <param name="userId">The caller's Keycloak user ID.</param>
  /// <param name="isAdmin">Whether the caller has <c>helix-admin</c>.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The evaluation.</returns>
  /// <exception cref="ExperimentNotFoundException">No such experiment, or it isn't visible to the caller.</exception>
  /// <exception cref="EvaluationNotFoundException">No such evaluation in the experiment.</exception>
  Task<EvaluationResponse> GetAsync(
    Guid experimentId,
    Guid evaluationId,
    string userId,
    bool isAdmin,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Follows an evaluation: yields it now, then again whenever it changes (polled every second, each poll in a fresh
  /// DI scope), and finishes once it's terminal or gone. Checks access before returning, so the caller can answer 404
  /// before starting a stream.
  /// </summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="evaluationId">The evaluation's ID.</param>
  /// <param name="userId">The caller's Keycloak user ID.</param>
  /// <param name="isAdmin">Whether the caller has <c>helix-admin</c>.</param>
  /// <param name="cancellationToken">Cancels the check; the stream stops when its enumeration is cancelled.</param>
  /// <returns>The evaluation's successive states.</returns>
  /// <exception cref="ExperimentNotFoundException">No such experiment, or it isn't visible to the caller.</exception>
  /// <exception cref="EvaluationNotFoundException">No such evaluation in the experiment.</exception>
  Task<IAsyncEnumerable<EvaluationResponse>> FollowAsync(
    Guid experimentId,
    Guid evaluationId,
    string userId,
    bool isAdmin,
    CancellationToken cancellationToken = default);
}

/// <summary>No such evaluation in the experiment. The API answers 404.</summary>
/// <param name="evaluationId">The evaluation ID that was requested.</param>
public class EvaluationNotFoundException(Guid evaluationId) : Exception($"Evaluation {evaluationId} doesn't exist.");

/// <summary>The evaluation can't start in the current state (e.g. the run hasn't succeeded). The API answers 409.</summary>
/// <param name="message">Why not.</param>
public class EvaluationConflictException(string message) : Exception(message);

/// <summary>The evaluation options are invalid. The API answers 400 with the errors.</summary>
/// <param name="errors">What's wrong, as <c>"field: problem"</c> messages.</param>
public class EvaluationValidationException(IReadOnlyList<string> errors)
  : Exception("The evaluation options are invalid: " + string.Join("; ", errors))
{
  /// <summary>What's wrong, as <c>"field: problem"</c> messages.</summary>
  public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>The evaluation's job couldn't be published to RabbitMQ. The API answers 503.</summary>
/// <param name="message">What went wrong.</param>
/// <param name="innerException">The publish error.</param>
public class EvaluationQueueUnavailableException(string message, Exception innerException)
  : Exception(message, innerException);
