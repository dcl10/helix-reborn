namespace HelixReborn.Api.Controllers;

using Auth;
using Microsoft.AspNetCore.Mvc;
using Models;
using Services.Experiments;
using Services.Pipelines;

/// <summary>
/// An experiment's saved pipelines. Owner or <c>helix-admin</c> can read; only the owner can create, change or delete
/// them. An invalid spec is a 400 with every problem under <c>errors.spec</c>; no dataset or a duplicate name is a 409.
/// </summary>
/// <param name="pipelines">The pipeline service.</param>
/// <param name="currentUser">The signed-in user.</param>
[ApiController]
[Route("api/experiments/{experimentId:guid}/pipelines")]
public class PipelinesController(IPipelineService pipelines, ICurrentUser currentUser) : ControllerBase
{
  /// <summary>Lists the experiment's pipelines, by name.</summary>
  /// <param name="experimentId">Experiment ID.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The pipelines.</returns>
  [HttpGet]
  public Task<ActionResult<IReadOnlyList<PipelineResponse>>> List(Guid experimentId, CancellationToken cancellationToken) =>
    Handle<IReadOnlyList<PipelineResponse>>(async () =>
      Ok(await pipelines.ListAsync(experimentId, currentUser.UserId, currentUser.IsAdmin, cancellationToken)));

  /// <summary>Gets one pipeline.</summary>
  /// <param name="experimentId">Experiment ID.</param>
  /// <param name="pipelineId">Pipeline ID.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The pipeline.</returns>
  [HttpGet("{pipelineId:guid}")]
  public Task<ActionResult<PipelineResponse>> Get(Guid experimentId, Guid pipelineId, CancellationToken cancellationToken) =>
    Handle<PipelineResponse>(async () =>
      Ok(await pipelines.GetAsync(experimentId, pipelineId, currentUser.UserId, currentUser.IsAdmin, cancellationToken)));

  /// <summary>Validates and saves a new pipeline. Owner only.</summary>
  /// <param name="experimentId">Experiment ID.</param>
  /// <param name="request">The name and snake_case spec.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>201 with the pipeline.</returns>
  [HttpPost]
  public Task<ActionResult<PipelineResponse>> Create(
    Guid experimentId,
    SavePipelineRequest request,
    CancellationToken cancellationToken) =>
    Handle<PipelineResponse>(async () =>
    {
      var pipeline = await pipelines.CreateAsync(
        experimentId, currentUser.UserId, request.Name, request.Spec!, cancellationToken);
      return Created($"/api/experiments/{experimentId}/pipelines/{pipeline.Id}", pipeline);
    });

  /// <summary>Validates and replaces a pipeline's name and spec. Owner only.</summary>
  /// <param name="experimentId">Experiment ID.</param>
  /// <param name="pipelineId">Pipeline ID.</param>
  /// <param name="request">The new name and snake_case spec.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The updated pipeline.</returns>
  [HttpPut("{pipelineId:guid}")]
  public Task<ActionResult<PipelineResponse>> Update(
    Guid experimentId,
    Guid pipelineId,
    SavePipelineRequest request,
    CancellationToken cancellationToken) =>
    Handle<PipelineResponse>(async () =>
      Ok(await pipelines.UpdateAsync(
        experimentId, pipelineId, currentUser.UserId, request.Name, request.Spec!, cancellationToken)));

  /// <summary>Deletes a pipeline. Owner only. Runs keep their own copy of the spec (#25).</summary>
  /// <param name="experimentId">Experiment ID.</param>
  /// <param name="pipelineId">Pipeline ID.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>204.</returns>
  [HttpDelete("{pipelineId:guid}")]
  public async Task<IActionResult> Delete(Guid experimentId, Guid pipelineId, CancellationToken cancellationToken)
  {
    var result = await Handle<object>(async () =>
    {
      await pipelines.DeleteAsync(experimentId, pipelineId, currentUser.UserId, cancellationToken);
      return NoContent();
    });
    return result.Result!;
  }

  /// <summary>Maps the pipeline service's exceptions to problem responses.</summary>
  private async Task<ActionResult<T>> Handle<T>(Func<Task<ActionResult<T>>> action)
  {
    try
    {
      return await action();
    }
    catch (ExperimentNotFoundException ex)
    {
      return Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
    }
    catch (PipelineNotFoundException ex)
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
  }
}
