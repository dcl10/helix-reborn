namespace HelixReborn.Api.Controllers;

using Auth;
using Microsoft.AspNetCore.Mvc;
using Models;
using Services.Experiments;
using Services.Jobs;
using Services.Pipelines;
using Services.Runs;

/// <summary>
/// Training runs: starting one from a pipeline, and reading or following an experiment's runs. Owner or
/// <c>helix-admin</c> can read; only the owner can start a run. Errors: <c>404</c> experiment, pipeline or run missing or
/// not visible (or an admin starting a run); <c>409</c> no dataset; <c>400</c> the spec no longer fits the dataset;
/// <c>503</c> the queue is unavailable.
/// </summary>
/// <param name="runs">The run service.</param>
/// <param name="currentUser">The signed-in user.</param>
[ApiController]
[Route("api/experiments/{experimentId:guid}")]
public class RunsController(IRunService runs, ICurrentUser currentUser) : ControllerBase
{
  /// <summary>Starts a run of a pipeline: validates its spec again, then queues a training job. Owner only.</summary>
  /// <param name="experimentId">Experiment ID.</param>
  /// <param name="pipelineId">Pipeline ID.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>202 with the queued run.</returns>
  [HttpPost("pipelines/{pipelineId:guid}/runs")]
  public Task<ActionResult<RunResponse>> Start(Guid experimentId, Guid pipelineId, CancellationToken cancellationToken) =>
    Handle<RunResponse>(async () =>
    {
      var run = await runs.StartAsync(experimentId, pipelineId, currentUser.UserId, cancellationToken);
      return Accepted($"/api/experiments/{experimentId}/runs/{run.Id}", run);
    });

  /// <summary>Lists the experiment's runs, newest first.</summary>
  /// <param name="experimentId">Experiment ID.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The runs.</returns>
  [HttpGet("runs")]
  public Task<ActionResult<IReadOnlyList<RunResponse>>> List(Guid experimentId, CancellationToken cancellationToken) =>
    Handle<IReadOnlyList<RunResponse>>(async () =>
      Ok(await runs.ListAsync(experimentId, currentUser.UserId, currentUser.IsAdmin, cancellationToken)));

  /// <summary>Gets one run.</summary>
  /// <param name="experimentId">Experiment ID.</param>
  /// <param name="runId">Run ID.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The run.</returns>
  [HttpGet("runs/{runId:guid}")]
  public Task<ActionResult<RunResponse>> Get(Guid experimentId, Guid runId, CancellationToken cancellationToken) =>
    Handle<RunResponse>(async () =>
      Ok(await runs.GetAsync(experimentId, runId, currentUser.UserId, currentUser.IsAdmin, cancellationToken)));

  /// <summary>
  /// Follows a run as server-sent events: <c>status</c> with the <see cref="RunResponse"/> (camelCase) now and whenever
  /// it changes, then <c>end</c> (<c>{"status"}</c>) once it's succeeded or failed, and the stream closes. Checked every
  /// second. Owner or admin; 404 otherwise.
  /// </summary>
  /// <param name="experimentId">Experiment ID.</param>
  /// <param name="runId">Run ID.</param>
  /// <param name="cancellationToken">Cancels the request; closing the connection stops the stream.</param>
  /// <returns>The event stream, or a 404 problem.</returns>
  [HttpGet("runs/{runId:guid}/events")]
  [Produces("text/event-stream")]
  public async Task<IResult> Events(Guid experimentId, Guid runId, CancellationToken cancellationToken)
  {
    try
    {
      var states = await runs.FollowAsync(
        experimentId, runId, currentUser.UserId, currentUser.IsAdmin, cancellationToken);
      return TypedResults.ServerSentEvents(JobStatusFeed.ToServerSentEvents(
        states, run => run.Status is RunStatus.Succeeded or RunStatus.Failed, run => run.Status, cancellationToken));
    }
    catch (Exception ex) when (ex is ExperimentNotFoundException or RunNotFoundException)
    {
      return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
    }
  }

  /// <summary>Maps the run service's exceptions to problem responses.</summary>
  private async Task<ActionResult<T>> Handle<T>(Func<Task<ActionResult<T>>> action)
  {
    try
    {
      return await action();
    }
    catch (Exception ex) when (ex is ExperimentNotFoundException or PipelineNotFoundException or RunNotFoundException)
    {
      return Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
    }
    catch (PipelineConflictException ex)
    {
      return Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
    }
    catch (PipelineValidationException ex)
    {
      return ValidationProblem(new ValidationProblemDetails(
        new Dictionary<string, string[]> { ["spec"] = [.. ex.Errors] }));
    }
    catch (RunQueueUnavailableException ex)
    {
      return Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
  }
}
