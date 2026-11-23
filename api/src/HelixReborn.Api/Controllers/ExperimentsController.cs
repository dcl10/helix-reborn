namespace HelixReborn.Api.Controllers;

using Auth;
using Microsoft.AspNetCore.Mvc;
using Models;
using Services.Experiments;

/// <summary>
/// Experiments: the top-level thing a user works in. Users see only their own; <c>helix-admin</c> can read (with
/// <c>?all=true</c>) but not modify other users' experiments.
/// </summary>
/// <param name="experiments">The experiment service.</param>
/// <param name="currentUser">The signed-in user.</param>
[ApiController]
[Route("api/experiments")]
public class ExperimentsController(IExperimentService experiments, ICurrentUser currentUser) : ControllerBase
{
  /// <summary>Lists the caller's experiments, newest first.</summary>
  /// <param name="all">Admins only: when true, lists every user's experiments instead of just the caller's.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The experiments.</returns>
  [HttpGet]
  public async Task<ActionResult<IReadOnlyList<ExperimentResponse>>> List(
    bool all,
    CancellationToken cancellationToken) =>
    Ok(await experiments.ListAsync(currentUser.UserId, currentUser.IsAdmin, all, cancellationToken));

  /// <summary>Creates an experiment owned by the caller.</summary>
  /// <param name="request">The experiment's name and optional description.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>201 with the experiment.</returns>
  [HttpPost]
  public async Task<ActionResult<ExperimentResponse>> Create(
    CreateExperimentRequest request,
    CancellationToken cancellationToken)
  {
    var experiment = await experiments.CreateAsync(
      currentUser.UserId,
      request.Name,
      request.Description,
      cancellationToken);

    return Created($"api/experiments/{experiment.Id}", experiment);
  }

  /// <summary>Gets one experiment. Owner or <c>helix-admin</c> only.</summary>
  /// <param name="id">Experiment ID.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The experiment.</returns>
  [HttpGet("{id:guid}")]
  public Task<ActionResult<ExperimentResponse>> Get(Guid id, CancellationToken cancellationToken) =>
    Handle(() => experiments.GetAsync(id, currentUser.UserId, currentUser.IsAdmin, cancellationToken));

  /// <summary>Updates an experiment's name and/or description. Owner only, even for admins.</summary>
  /// <param name="id">Experiment ID.</param>
  /// <param name="request">The fields to change.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The updated experiment.</returns>
  [HttpPatch("{id:guid}")]
  public Task<ActionResult<ExperimentResponse>> Update(
    Guid id,
    UpdateExperimentRequest request,
    CancellationToken cancellationToken) =>
    Handle(() => experiments.UpdateAsync(id, currentUser.UserId, request.Name, request.Description, cancellationToken));

  /// <summary>Deletes an experiment: its database rows and every object stored under it. Owner only, even for admins.</summary>
  /// <param name="id">Experiment ID.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>204 on success.</returns>
  [HttpDelete("{id:guid}")]
  public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
  {
    try
    {
      await experiments.DeleteAsync(id, currentUser.UserId, cancellationToken);
    }
    catch (ExperimentNotFoundException ex)
    {
      return Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
    }

    return NoContent();
  }

  /// <summary>Maps <see cref="ExperimentNotFoundException"/> to a 404 problem response.</summary>
  private async Task<ActionResult<T>> Handle<T>(Func<Task<T>> action)
  {
    try
    {
      return await action();
    }
    catch (ExperimentNotFoundException ex)
    {
      return Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
    }
  }
}
