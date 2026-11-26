namespace HelixReborn.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using Services.Pipelines;

/// <summary>
/// The pipeline step catalog (<c>contracts/pipeline-catalog.json</c>), which the web app builds its pipeline forms
/// from. Any signed-in user can read it.
/// </summary>
/// <param name="catalog">The loaded catalog.</param>
[ApiController]
[Route("api/pipeline-catalog")]
public class PipelineCatalogController(IPipelineCatalog catalog) : ControllerBase
{
  /// <summary>Returns the catalog JSON exactly as in the file.</summary>
  /// <returns>The catalog, as <c>application/json</c>.</returns>
  [HttpGet]
  public ContentResult Get() => Content(catalog.RawJson, "application/json");
}
