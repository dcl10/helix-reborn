namespace HelixReborn.Api.Controllers;

using System.IO.Compression;
using Auth;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Models;
using Services.Artifacts;
using Services.Experiments;
using Services.Storage;

/// <summary>
/// An experiment's artifacts: the dataset, EDA plots and tables, a run's model and metadata, evaluation plots and
/// tables, and predictions. Owner or <c>helix-admin</c> can read; artifacts are only ever written by other services
/// through <see cref="IArtifactService.RecordAsync"/>.
/// </summary>
/// <param name="experiments">Checks the experiment is visible to the caller.</param>
/// <param name="artifacts">The artifact registry.</param>
/// <param name="storage">Object storage, to stream artifact content.</param>
/// <param name="currentUser">The signed-in user.</param>
[ApiController]
[Route("api/experiments/{experimentId:guid}")]
public class ArtifactsController(
  IExperimentService experiments,
  IArtifactService artifacts,
  IObjectStorage storage,
  ICurrentUser currentUser) : ControllerBase
{
  /// <summary>Lists an experiment's artifacts, ordered by when they were recorded.</summary>
  /// <param name="experimentId">Experiment ID.</param>
  /// <param name="sourceType">When given, only artifacts with this source type.</param>
  /// <param name="sourceId">When given, only artifacts with this source ID.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The matching artifacts.</returns>
  [HttpGet("artifacts")]
  public async Task<ActionResult<IReadOnlyList<ArtifactResponse>>> List(
    Guid experimentId,
    string? sourceType,
    Guid? sourceId,
    CancellationToken cancellationToken)
  {
    var notFound = await CheckVisible(experimentId, cancellationToken);
    if (notFound is not null)
    {
      return notFound;
    }

    var list = await artifacts.ListAsync(experimentId, sourceType, sourceId, cancellationToken);
    return Ok(list.Select(ToResponse).ToList());
  }

  /// <summary>
  /// Streams one artifact's content with its stored <c>Content-Type</c>. <c>?download=true</c> adds a
  /// <c>Content-Disposition: attachment</c> header with the storage key's last segment as the file name; otherwise
  /// <c>inline</c>.
  /// </summary>
  /// <param name="experimentId">Experiment ID.</param>
  /// <param name="artifactId">Artifact ID.</param>
  /// <param name="download">Whether to ask the browser to download rather than display the file.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The streamed content, or 404 if the artifact record or its object is missing.</returns>
  [HttpGet("artifacts/{artifactId:guid}/content")]
  public async Task<IActionResult> Content(
    Guid experimentId,
    Guid artifactId,
    bool download,
    CancellationToken cancellationToken)
  {
    if (await CheckVisible(experimentId, cancellationToken) is { } notFound)
    {
      return notFound;
    }

    var artifact = await artifacts.GetAsync(experimentId, artifactId, cancellationToken);
    if (artifact is null)
    {
      return ArtifactNotFound(artifactId);
    }

    Stream content;
    try
    {
      content = await storage.OpenReadAsync(artifact.StorageKey, cancellationToken);
    }
    catch (ObjectNotFoundException)
    {
      return ArtifactNotFound(artifactId);
    }

    await using (content)
    {
      Response.ContentType = artifact.ContentType;
      Response.Headers.ContentDisposition = new ContentDispositionHeaderValue(download ? "attachment" : "inline")
      {
        FileName = FileNameOf(artifact.StorageKey),
      }.ToString();

      await content.CopyToAsync(Response.Body, cancellationToken);
    }

    return new EmptyResult();
  }

  /// <summary>
  /// Streams a zip of every artifact of an experiment, one entry per artifact named by the storage key with the
  /// experiment's prefix removed.
  /// </summary>
  /// <param name="experimentId">Experiment ID.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The zip, as <c>application/zip</c>.</returns>
  [HttpGet("archive")]
  public async Task<IActionResult> Archive(Guid experimentId, CancellationToken cancellationToken)
  {
    ExperimentResponse experiment;
    try
    {
      experiment = await experiments.GetAsync(experimentId, currentUser.UserId, currentUser.IsAdmin, cancellationToken);
    }
    catch (ExperimentNotFoundException ex)
    {
      return Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
    }

    var list = await artifacts.ListAsync(experimentId, null, null, cancellationToken);
    var prefix = StorageKeys.Experiment(experimentId);

    var id8 = experimentId.ToString("N")[..8];
    Response.ContentType = "application/zip";
    Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
    {
      FileName = $"{SanitiseForArchiveName(experiment.Name)}-{id8}.zip",
    }.ToString();

    HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

    await using (var archive = await ZipArchive.CreateAsync(
      Response.Body,
      ZipArchiveMode.Create,
      leaveOpen: true,
      entryNameEncoding: null,
      cancellationToken))
    {
      foreach (var artifact in list)
      {
        Stream objectContent;
        try
        {
          objectContent = await storage.OpenReadAsync(artifact.StorageKey, cancellationToken);
        }
        catch (ObjectNotFoundException)
        {
          continue;
        }

        await using (objectContent)
        {
          var entryName = artifact.StorageKey.StartsWith(prefix, StringComparison.Ordinal)
            ? artifact.StorageKey[prefix.Length..]
            : artifact.StorageKey;
          var entry = archive.CreateEntry(entryName);
          await using var entryStream = await entry.OpenAsync(cancellationToken);
          await objectContent.CopyToAsync(entryStream, cancellationToken);
        }
      }
    }

    return new EmptyResult();
  }

  /// <summary>Checks the experiment is visible to the caller, returning a 404 problem result if not.</summary>
  private async Task<ObjectResult?> CheckVisible(Guid experimentId, CancellationToken cancellationToken)
  {
    try
    {
      await experiments.GetAsync(experimentId, currentUser.UserId, currentUser.IsAdmin, cancellationToken);
      return null;
    }
    catch (ExperimentNotFoundException ex)
    {
      return Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
    }
  }

  private ObjectResult ArtifactNotFound(Guid artifactId) =>
    Problem($"Artifact {artifactId} doesn't exist.", statusCode: StatusCodes.Status404NotFound);

  private static string FileNameOf(string storageKey) => storageKey.Split('/')[^1];

  /// <summary>Sanitises an experiment name for use in a zip file name: only <c>[A-Za-z0-9._-]</c>, others become <c>_</c>.</summary>
  private static string SanitiseForArchiveName(string name)
  {
    var sanitised = new string([.. name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_')]);
    return sanitised.Length == 0 ? "experiment" : sanitised;
  }

  private static ArtifactResponse ToResponse(Artifact x) => new(
    x.Id,
    x.Kind,
    x.Title,
    x.ContentType,
    x.SizeBytes,
    x.SourceType,
    x.SourceId,
    x.CreatedAt,
    FileNameOf(x.StorageKey));
}
