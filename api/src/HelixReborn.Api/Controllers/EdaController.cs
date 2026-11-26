namespace HelixReborn.Api.Controllers;

using System.Text.Json;
using Auth;
using Microsoft.AspNetCore.Mvc;
using Models;
using Models.Eda;
using Services.Eda;
using Services.Experiments;

/// <summary>
/// An experiment's exploratory data analysis, made by the EDA service from the experiment's dataset. Owner or
/// <c>helix-admin</c> can read; only the owner can make plots and tables. Errors: <c>404</c> experiment missing or
/// not visible (or an admin writing to someone else's), <c>409</c> no dataset, <c>400</c> EDA rejected the request
/// (its detail is passed on), <c>502</c> EDA failed.
/// </summary>
/// <param name="eda">The EDA service.</param>
/// <param name="currentUser">The signed-in user.</param>
[ApiController]
[Route("api/experiments/{experimentId:guid}/eda")]
public class EdaController(IEdaService eda, ICurrentUser currentUser) : ControllerBase
{
  /// <summary>Summarises the experiment's dataset: row and column counts and per-column statistics.</summary>
  /// <param name="experimentId">Experiment ID.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>EDA's summary JSON, passed through as-is (snake_case).</returns>
  [HttpGet("summary")]
  public Task<ActionResult<JsonElement>> Summary(Guid experimentId, CancellationToken cancellationToken) =>
    Handle<JsonElement>(async () => Ok(
      await eda.GetSummaryAsync(experimentId, currentUser.UserId, currentUser.IsAdmin, cancellationToken)));

  /// <summary>Renders a plot of the experiment's dataset and records it as an artifact. Owner only.</summary>
  /// <param name="experimentId">Experiment ID.</param>
  /// <param name="request">Which plot, and its options.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>201 with the new artifact.</returns>
  [HttpPost("plots")]
  public Task<ActionResult<ArtifactResponse>> CreatePlot(
    Guid experimentId,
    EdaPlotRequest request,
    CancellationToken cancellationToken) =>
    Handle<ArtifactResponse>(async () =>
    {
      var artifact = await eda.CreatePlotAsync(experimentId, currentUser.UserId, request, cancellationToken);
      return Created(ContentUrl(experimentId, artifact.Id), artifact);
    });

  /// <summary>Runs normality tests on the experiment's dataset and records the CSV as an artifact. Owner only.</summary>
  /// <param name="experimentId">Experiment ID.</param>
  /// <param name="request">The columns to test.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>201 with the new artifact and the table's rows.</returns>
  [HttpPost("tables/normality")]
  public Task<ActionResult<NormalityResponse>> CreateNormalityTable(
    Guid experimentId,
    NormalityTableRequest request,
    CancellationToken cancellationToken) =>
    Handle<NormalityResponse>(async () =>
    {
      var table = await eda.CreateNormalityTableAsync(
        experimentId,
        currentUser.UserId,
        request.Columns,
        cancellationToken);
      return Created(ContentUrl(experimentId, table.Artifact.Id), table);
    });

  /// <summary>Lists the experiment's EDA plots and tables, oldest first.</summary>
  /// <param name="experimentId">Experiment ID.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The artifacts whose <c>sourceType</c> is <c>"eda"</c>.</returns>
  [HttpGet("artifacts")]
  public Task<ActionResult<IReadOnlyList<ArtifactResponse>>> Artifacts(
    Guid experimentId,
    CancellationToken cancellationToken) =>
    Handle<IReadOnlyList<ArtifactResponse>>(async () => Ok(
      await eda.ListArtifactsAsync(experimentId, currentUser.UserId, currentUser.IsAdmin, cancellationToken)));

  private static string ContentUrl(Guid experimentId, Guid artifactId) =>
    $"/api/experiments/{experimentId}/artifacts/{artifactId}/content";

  /// <summary>Maps the EDA service's exceptions to problem responses.</summary>
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
    catch (NoDatasetException ex)
    {
      return Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
    }
    catch (EdaException ex) when (ex.StatusCode == EdaException.Rejected)
    {
      return Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "The EDA service rejected the request.");
    }
    catch (EdaException ex)
    {
      return Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
  }
}
