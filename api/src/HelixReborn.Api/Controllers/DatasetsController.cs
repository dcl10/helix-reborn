namespace HelixReborn.Api.Controllers;

using Auth;
using Microsoft.AspNetCore.Mvc;
using Models;
using Services.Datasets;
using Services.Experiments;

/// <summary>
/// An experiment's one uploaded dataset. Owner or <c>helix-admin</c> can read it; only the owner can upload one.
/// </summary>
/// <param name="datasets">The dataset service.</param>
/// <param name="currentUser">The signed-in user.</param>
[ApiController]
[Route("api/experiments/{experimentId:guid}/dataset")]
public class DatasetsController(IDatasetService datasets, ICurrentUser currentUser) : ControllerBase
{
  /// <summary>Maximum upload size: 50 MB.</summary>
  private const long MaxUploadBytes = 52_428_800;

  /// <summary>Gets an experiment's dataset.</summary>
  /// <param name="experimentId">Experiment ID.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The dataset.</returns>
  [HttpGet]
  public Task<ActionResult<DatasetResponse>> Get(Guid experimentId, CancellationToken cancellationToken) =>
    Handle(() => datasets.GetAsync(experimentId, currentUser.UserId, currentUser.IsAdmin, cancellationToken));

  /// <summary>
  /// Uploads an experiment's dataset. Only the owner may upload; only one dataset is allowed per experiment.
  /// </summary>
  /// <param name="experimentId">Experiment ID.</param>
  /// <param name="file">The CSV file, field name <c>file</c>.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>201 with the dataset.</returns>
  [HttpPost]
  [RequestSizeLimit(MaxUploadBytes)]
  [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
  public async Task<ActionResult<DatasetResponse>> Upload(
    Guid experimentId,
    IFormFile file,
    CancellationToken cancellationToken)
  {
    try
    {
      await using var stream = file.OpenReadStream();
      var dataset = await datasets.UploadAsync(
        experimentId,
        currentUser.UserId,
        file.FileName,
        file.ContentType,
        stream,
        cancellationToken);

      return Created($"api/experiments/{experimentId}/dataset", dataset);
    }
    catch (ExperimentNotFoundException ex)
    {
      return Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
    }
    catch (DatasetConflictException ex)
    {
      return Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
    }
    catch (DatasetValidationException ex)
    {
      return ValidationProblem(new ValidationProblemDetails(
        new Dictionary<string, string[]> { ["file"] = [.. ex.Problems] }));
    }
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
