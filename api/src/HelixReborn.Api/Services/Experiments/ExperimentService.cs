namespace HelixReborn.Api.Services.Experiments;

using Data;
using Microsoft.EntityFrameworkCore;
using Models;
using Storage;

/// <summary><see cref="IExperimentService"/> on the app database and object storage.</summary>
/// <param name="db">The app database.</param>
/// <param name="storage">Object storage, to delete a deleted experiment's files.</param>
/// <param name="time">The clock.</param>
public class ExperimentService(AppDbContext db, IObjectStorage storage, TimeProvider time) : IExperimentService
{
  /// <inheritdoc />
  public async Task<IReadOnlyList<ExperimentResponse>> ListAsync(
    string userId,
    bool isAdmin,
    bool all,
    CancellationToken cancellationToken = default)
  {
    var query = db.Experiments.AsNoTracking().Include(x => x.Dataset).AsQueryable();
    if (!(isAdmin && all))
    {
      query = query.Where(x => x.OwnerSub == userId);
    }

    var experiments = await query.OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);

    return [.. experiments.Select(ToResponse)];
  }

  /// <inheritdoc />
  public async Task<ExperimentResponse> CreateAsync(
    string userId,
    string name,
    string? description,
    CancellationToken cancellationToken = default)
  {
    var now = time.GetUtcNow();
    var experiment = new Experiment
    {
      Id = Guid.NewGuid(),
      OwnerSub = userId,
      Name = name,
      Description = description,
      CreatedAt = now,
      UpdatedAt = now,
    };

    db.Experiments.Add(experiment);
    await db.SaveChangesAsync(cancellationToken);

    return ToResponse(experiment);
  }

  /// <inheritdoc />
  public async Task<ExperimentResponse> GetAsync(
    Guid experimentId,
    string userId,
    bool isAdmin,
    CancellationToken cancellationToken = default)
  {
    var experiment = await Find(experimentId, cancellationToken);
    if (experiment.OwnerSub != userId && !isAdmin)
    {
      throw new ExperimentNotFoundException(experimentId);
    }

    return ToResponse(experiment);
  }

  /// <inheritdoc />
  public async Task<ExperimentResponse> UpdateAsync(
    Guid experimentId,
    string userId,
    string? name,
    string? description,
    CancellationToken cancellationToken = default)
  {
    var experiment = await FindOwned(experimentId, userId, cancellationToken);

    if (name is not null)
    {
      experiment.Name = name;
    }

    if (description is not null)
    {
      experiment.Description = description;
    }

    experiment.UpdatedAt = time.GetUtcNow();

    await db.SaveChangesAsync(cancellationToken);

    return ToResponse(experiment);
  }

  /// <inheritdoc />
  public async Task DeleteAsync(Guid experimentId, string userId, CancellationToken cancellationToken = default)
  {
    var experiment = await FindOwned(experimentId, userId, cancellationToken);

    db.Experiments.Remove(experiment);
    await db.SaveChangesAsync(cancellationToken);

    await storage.DeletePrefixAsync(StorageKeys.Experiment(experimentId), cancellationToken);
  }

  private async Task<Experiment> Find(Guid experimentId, CancellationToken cancellationToken) =>
    await db.Experiments.Include(x => x.Dataset).SingleOrDefaultAsync(x => x.Id == experimentId, cancellationToken)
    ?? throw new ExperimentNotFoundException(experimentId);

  private async Task<Experiment> FindOwned(Guid experimentId, string userId, CancellationToken cancellationToken)
  {
    var experiment = await Find(experimentId, cancellationToken);
    return experiment.OwnerSub == userId ? experiment : throw new ExperimentNotFoundException(experimentId);
  }

  private static ExperimentResponse ToResponse(Experiment x) => new(
    x.Id,
    x.Name,
    x.Description,
    x.OwnerSub,
    x.CreatedAt,
    x.UpdatedAt,
    x.Dataset is { } dataset
      ? new DatasetResponse(dataset.Id, dataset.FileName, dataset.SizeBytes, dataset.RowCount, dataset.CreatedAt, dataset.Columns)
      : null);
}
