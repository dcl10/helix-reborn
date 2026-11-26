namespace HelixReborn.Api.Models;

/// <summary>An artifact as the API returns it.</summary>
/// <param name="Id">Artifact ID.</param>
/// <param name="Kind">What kind of file this is.</param>
/// <param name="Title">Human readable title.</param>
/// <param name="ContentType">The file's content type.</param>
/// <param name="SizeBytes">Size of the file, in bytes.</param>
/// <param name="SourceType">What produced the file, if recorded.</param>
/// <param name="SourceId">The ID of the thing that produced the file, if recorded.</param>
/// <param name="CreatedAt">When the artifact was recorded.</param>
/// <param name="FileName">The file name: the storage key's last path segment.</param>
public record ArtifactResponse(
  Guid Id,
  ArtifactKind Kind,
  string Title,
  string ContentType,
  long SizeBytes,
  string? SourceType,
  Guid? SourceId,
  DateTimeOffset CreatedAt,
  string FileName)
{
  /// <summary>Builds the response for an artifact; the file name is the storage key's last path segment.</summary>
  /// <param name="artifact">The artifact.</param>
  /// <returns>The response.</returns>
  public static ArtifactResponse From(Artifact artifact) => new(
    artifact.Id,
    artifact.Kind,
    artifact.Title,
    artifact.ContentType,
    artifact.SizeBytes,
    artifact.SourceType,
    artifact.SourceId,
    artifact.CreatedAt,
    artifact.StorageKey.Split('/')[^1]);
}
