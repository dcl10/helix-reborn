namespace HelixReborn.Api.Services.Artifacts;

using Data;
using Microsoft.EntityFrameworkCore;
using Models;

/// <summary><see cref="IArtifactService"/> on the app database.</summary>
/// <param name="db">The app database.</param>
/// <param name="time">The clock.</param>
public class ArtifactService(AppDbContext db, TimeProvider time) : IArtifactService
{
  /// <inheritdoc />
  public Task<Artifact> RecordAsync(
    Guid experimentId,
    ArtifactKind kind,
    string title,
    string storageKey,
    string contentType,
    long sizeBytes,
    string? sourceType,
    Guid? sourceId,
    CancellationToken cancellationToken = default) =>
    RecordAsync(
      Guid.NewGuid(),
      experimentId,
      kind,
      title,
      storageKey,
      contentType,
      sizeBytes,
      sourceType,
      sourceId,
      cancellationToken);

  /// <inheritdoc />
  public async Task<Artifact> RecordAsync(
    Guid artifactId,
    Guid experimentId,
    ArtifactKind kind,
    string title,
    string storageKey,
    string contentType,
    long sizeBytes,
    string? sourceType,
    Guid? sourceId,
    CancellationToken cancellationToken = default)
  {
    var artifact = new Artifact
    {
      Id = artifactId,
      ExperimentId = experimentId,
      Kind = kind,
      Title = title,
      StorageKey = storageKey,
      ContentType = contentType,
      SizeBytes = sizeBytes,
      SourceType = sourceType,
      SourceId = sourceId,
      CreatedAt = time.GetUtcNow(),
    };

    db.Artifacts.Add(artifact);
    await db.SaveChangesAsync(cancellationToken);

    return artifact;
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<Artifact>> ListAsync(
    Guid experimentId,
    string? sourceType,
    Guid? sourceId,
    CancellationToken cancellationToken = default)
  {
    var query = db.Artifacts.AsNoTracking().Where(x => x.ExperimentId == experimentId);

    if (sourceType is not null)
    {
      query = query.Where(x => x.SourceType == sourceType);
    }

    if (sourceId is not null)
    {
      query = query.Where(x => x.SourceId == sourceId);
    }

    return await query.OrderBy(x => x.CreatedAt).ToListAsync(cancellationToken);
  }

  /// <inheritdoc />
  public async Task<Artifact?> GetAsync(
    Guid experimentId,
    Guid artifactId,
    CancellationToken cancellationToken = default) =>
    await db.Artifacts.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == artifactId && x.ExperimentId == experimentId, cancellationToken);
}
