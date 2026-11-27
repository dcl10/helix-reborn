namespace HelixReborn.Api.Controllers;

using Auth;
using Microsoft.AspNetCore.Mvc;
using Models;
using Services.Evaluations;
using Services.Experiments;
using Services.Jobs;
using Services.Runs;

/// <summary>
/// Evaluations of a run's model: starting one, and reading or following an experiment's evaluations. Owner or
/// <c>helix-admin</c> can read; only the owner can start one. Errors: <c>404</c> experiment, run or evaluation missing
/// or not visible (or an admin starting one); <c>400</c> invalid options; <c>409</c> the run hasn't succeeded;
/// <c>503</c> the queue is unavailable.
/// </summary>
/// <param name="evaluations">The evaluation service.</param>
/// <param name="currentUser">The signed-in user.</param>
[ApiController]
[Route("api/experiments/{experimentId:guid}")]
public class EvaluationsController(IEvaluationService evaluations, ICurrentUser currentUser) : ControllerBase
{
  /// <summary>Starts an evaluation of a succeeded run's model with the given options. Owner only.</summary>
  /// <param name="experimentId">Experiment ID.</param>
  /// <param name="runId">Run ID.</param>
  /// <param name="request">The options (snake_case); missing fields take the defaults.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>202 with the queued evaluation.</returns>
  [HttpPost("runs/{runId:guid}/evaluations")]
  public Task<ActionResult<EvaluationResponse>> Start(
    Guid experimentId,
    Guid runId,
    [FromBody] CreateEvaluationRequest? request,
    CancellationToken cancellationToken) =>
    Handle<EvaluationResponse>(async () =>
    {
      var evaluation = await evaluations.StartAsync(
        experimentId, runId, request?.Options, currentUser.UserId, cancellationToken);
      return Accepted($"/api/experiments/{experimentId}/evaluations/{evaluation.Id}", evaluation);
    });

  /// <summary>Lists the experiment's evaluations, newest first.</summary>
  /// <param name="experimentId">Experiment ID.</param>
  /// <param name="runId">When given, only this run's evaluations.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The evaluations.</returns>
  [HttpGet("evaluations")]
  public Task<ActionResult<IReadOnlyList<EvaluationResponse>>> List(
    Guid experimentId,
    [FromQuery] Guid? runId,
    CancellationToken cancellationToken) =>
    Handle<IReadOnlyList<EvaluationResponse>>(async () =>
      Ok(await evaluations.ListAsync(experimentId, runId, currentUser.UserId, currentUser.IsAdmin, cancellationToken)));

  /// <summary>Gets one evaluation.</summary>
  /// <param name="experimentId">Experiment ID.</param>
  /// <param name="evaluationId">Evaluation ID.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The evaluation.</returns>
  [HttpGet("evaluations/{evaluationId:guid}")]
  public Task<ActionResult<EvaluationResponse>> Get(Guid experimentId, Guid evaluationId, CancellationToken cancellationToken) =>
    Handle<EvaluationResponse>(async () =>
      Ok(await evaluations.GetAsync(
        experimentId, evaluationId, currentUser.UserId, currentUser.IsAdmin, cancellationToken)));

  /// <summary>
  /// Follows an evaluation as server-sent events, with the same protocol as runs: <c>status</c> with the
  /// <see cref="EvaluationResponse"/> (camelCase) now and whenever it changes, then <c>end</c> (<c>{"status"}</c>) once
  /// it's succeeded or failed, and the stream closes. Owner or admin; 404 otherwise.
  /// </summary>
  /// <param name="experimentId">Experiment ID.</param>
  /// <param name="evaluationId">Evaluation ID.</param>
  /// <param name="cancellationToken">Cancels the request; closing the connection stops the stream.</param>
  /// <returns>The event stream, or a 404 problem.</returns>
  [HttpGet("evaluations/{evaluationId:guid}/events")]
  [Produces("text/event-stream")]
  public async Task<IResult> Events(Guid experimentId, Guid evaluationId, CancellationToken cancellationToken)
  {
    try
    {
      var states = await evaluations.FollowAsync(
        experimentId, evaluationId, currentUser.UserId, currentUser.IsAdmin, cancellationToken);
      return TypedResults.ServerSentEvents(JobStatusFeed.ToServerSentEvents(
        states, EvaluationService.IsTerminal, evaluation => evaluation.Status, cancellationToken));
    }
    catch (Exception ex) when (ex is ExperimentNotFoundException or EvaluationNotFoundException)
    {
      return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
    }
  }

  /// <summary>Maps the evaluation service's exceptions to problem responses.</summary>
  private async Task<ActionResult<T>> Handle<T>(Func<Task<ActionResult<T>>> action)
  {
    try
    {
      return await action();
    }
    catch (Exception ex) when (ex is ExperimentNotFoundException or RunNotFoundException or EvaluationNotFoundException)
    {
      return Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
    }
    catch (EvaluationValidationException ex)
    {
      return ValidationProblem(new ValidationProblemDetails(
        new Dictionary<string, string[]> { ["options"] = [.. ex.Errors] }));
    }
    catch (EvaluationConflictException ex)
    {
      return Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
    }
    catch (EvaluationQueueUnavailableException ex)
    {
      return Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
  }
}
