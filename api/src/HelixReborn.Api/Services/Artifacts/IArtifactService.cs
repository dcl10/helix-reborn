namespace HelixReborn.Api.Services.Artifacts;

using Models;

/// <summary>
/// Records and reads an experiment's artifacts. Doesn't check ownership itself: callers check the experiment is
/// visible to the caller (<see cref="Experiments.IExperimentService"/>) before calling this.
/// </summary>
public interface IArtifactService
{
  /// <summary>Records a file that's already been uploaded to object storage as an artifact.</summary>
  /// <param name="experimentId">The owning experiment's ID.</param>
  /// <param name="kind">What kind of file this is.</param>
  /// <param name="title">Human readable title, e.g. "Correlation heatmap".</param>
  /// <param name="storageKey">The S3 key the file is stored at.</param>
  /// <param name="contentType">The file's content type.</param>
  /// <param name="sizeBytes">Size of the file, in bytes.</param>
  /// <param name="sourceType">What produced the file, e.g. <c>"dataset"</c>, <c>"run"</c>.</param>
  /// <param name="sourceId">The ID of the thing that produced the file.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The new artifact.</returns>
  Task<Artifact> RecordAsync(
    Guid experimentId,
    ArtifactKind kind,
    string title,
    string storageKey,
    string contentType,
    long sizeBytes,
    string? sourceType,
    Guid? sourceId,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Records a file that's already been uploaded to object storage as an artifact with a given ID, for callers that
  /// need the ID before the file exists (EDA puts it in the storage key).
  /// </summary>
  /// <param name="artifactId">The new artifact's ID.</param>
  /// <param name="experimentId">The owning experiment's ID.</param>
  /// <param name="kind">What kind of file this is.</param>
  /// <param name="title">Human readable title, e.g. "Correlation heatmap".</param>
  /// <param name="storageKey">The S3 key the file is stored at.</param>
  /// <param name="contentType">The file's content type.</param>
  /// <param name="sizeBytes">Size of the file, in bytes.</param>
  /// <param name="sourceType">What produced the file, e.g. <c>"eda"</c>.</param>
  /// <param name="sourceId">The ID of the thing that produced the file.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The new artifact.</returns>
  Task<Artifact> RecordAsync(
    Guid artifactId,
    Guid experimentId,
    ArtifactKind kind,
    string title,
    string storageKey,
    string contentType,
    long sizeBytes,
    string? sourceType,
    Guid? sourceId,
    CancellationToken cancellationToken = default);

  /// <summary>Lists an experiment's artifacts, ordered by <see cref="Artifact.CreatedAt"/>.</summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="sourceType">When given, only artifacts with this source type.</param>
  /// <param name="sourceId">When given, only artifacts with this source ID.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The matching artifacts.</returns>
  Task<IReadOnlyList<Artifact>> ListAsync(
    Guid experimentId,
    string? sourceType,
    Guid? sourceId,
    CancellationToken cancellationToken = default);

  /// <summary>Gets one artifact.</summary>
  /// <param name="experimentId">The owning experiment's ID.</param>
  /// <param name="artifactId">The artifact's ID.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The artifact, or null if no such artifact exists on this experiment.</returns>
  Task<Artifact?> GetAsync(Guid experimentId, Guid artifactId, CancellationToken cancellationToken = default);
}
