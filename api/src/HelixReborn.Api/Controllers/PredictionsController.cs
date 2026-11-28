namespace HelixReborn.Api.Controllers;

using Auth;
using Filters;
using Microsoft.AspNetCore.Mvc;
using Models;
using Services.Experiments;
using Services.Predictions;
using Services.Runs;

/// <summary>
/// Predictions with a succeeded run's model, run inside the API with ONNX Runtime. Only the owner can predict; the
/// owner or <c>helix-admin</c> can list. Errors: <c>404</c> experiment or run missing or not visible (or an admin
/// predicting); <c>400</c> the upload can't be predicted on; <c>409</c> the run hasn't succeeded or its model is
/// unusable; <c>413</c> the upload is over <c>Predictions:MaxUploadBytes</c>.
/// </summary>
/// <param name="predictions">The prediction service.</param>
/// <param name="currentUser">The signed-in user.</param>
[ApiController]
[Route("api/experiments/{experimentId:guid}")]
public class PredictionsController(IPredictionService predictions, ICurrentUser currentUser) : ControllerBase
{
  /// <summary>
  /// Predicts on an uploaded CSV with a succeeded run's model, stores the output CSV as a <c>predictions</c> artifact,
  /// and returns a preview. Owner only.
  /// </summary>
  /// <param name="experimentId">Experiment ID.</param>
  /// <param name="runId">Run ID.</param>
  /// <param name="file">The CSV, field name <c>file</c>.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>201 with the prediction; <c>Location</c> is the output CSV's content.</returns>
  [HttpPost("runs/{runId:guid}/predictions")]
  [ServiceFilter(typeof(PredictionUploadLimitFilter))]
  public async Task<ActionResult<PredictionResponse>> Predict(
    Guid experimentId,
    Guid runId,
    IFormFile file,
    CancellationToken cancellationToken)
  {
    try
    {
      await using var stream = file.OpenReadStream();
      var prediction = await predictions.PredictAsync(
        experimentId, runId, currentUser.UserId, file.FileName, file.ContentType, stream, cancellationToken);
      return Created($"/api/experiments/{experimentId}/artifacts/{prediction.ArtifactId}/content", prediction);
    }
    catch (Exception ex) when (ex is ExperimentNotFoundException or RunNotFoundException)
    {
      return Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
    }
    catch (PredictionValidationException ex)
    {
      return ValidationProblem(new ValidationProblemDetails(
        new Dictionary<string, string[]> { ["file"] = [.. ex.Problems] }));
    }
    catch (PredictionConflictException ex)
    {
      return Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
    }
  }

  /// <summary>Lists the experiment's prediction outputs (artifacts with <c>sourceType</c> <c>prediction</c>).</summary>
  /// <param name="experimentId">Experiment ID.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The artifacts, oldest first.</returns>
  [HttpGet("predictions")]
  public async Task<ActionResult<IReadOnlyList<ArtifactResponse>>> List(
    Guid experimentId,
    CancellationToken cancellationToken)
  {
    try
    {
      return Ok(await predictions.ListAsync(experimentId, currentUser.UserId, currentUser.IsAdmin, cancellationToken));
    }
    catch (ExperimentNotFoundException ex)
    {
      return Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
    }
  }
}
