namespace HelixReborn.Api.Services.Datasets;

using Data;
using Experiments;
using Microsoft.EntityFrameworkCore;
using Models;
using Storage;

/// <summary><see cref="IDatasetService"/> on the app database and object storage.</summary>
/// <param name="db">The app database.</param>
/// <param name="storage">Object storage, to upload the validated file.</param>
/// <param name="time">The clock.</param>
public class DatasetService(AppDbContext db, IObjectStorage storage, TimeProvider time) : IDatasetService
{
  /// <summary>Characters allowed in a stored file name; anything else is replaced with <c>_</c>.</summary>
  private const int MaxFileNameLength = 100;

  /// <inheritdoc />
  public async Task<DatasetResponse> GetAsync(
    Guid experimentId,
    string userId,
    bool isAdmin,
    CancellationToken cancellationToken = default)
  {
    var experiment = await FindVisible(experimentId, userId, isAdmin, cancellationToken);

    return experiment.Dataset is { } dataset
      ? ToResponse(dataset)
      : throw new ExperimentNotFoundException(experimentId);
  }

  /// <inheritdoc />
  public async Task<DatasetResponse> UploadAsync(
    Guid experimentId,
    string userId,
    string fileName,
    string? contentType,
    Stream content,
    CancellationToken cancellationToken = default)
  {
    var experiment = await db.Experiments.Include(x => x.Dataset)
      .SingleOrDefaultAsync(x => x.Id == experimentId, cancellationToken)
      ?? throw new ExperimentNotFoundException(experimentId);

    if (experiment.OwnerSub != userId)
    {
      throw new ExperimentNotFoundException(experimentId);
    }

    if (experiment.Dataset is not null)
    {
      throw new DatasetConflictException($"Experiment {experimentId} already has a dataset.");
    }

    var sanitisedFileName = SanitiseFileName(fileName);

    await using var tempFile = new TempFile();
    long sizeBytes;
    CsvInspectionResult inspection;
    await using (var tempStream = tempFile.OpenWrite())
    {
      await content.CopyToAsync(tempStream, cancellationToken);
      sizeBytes = tempStream.Length;
    }

    await using (var inspectStream = tempFile.OpenRead())
    {
      inspection = CsvInspector.Inspect(inspectStream, fileName, contentType);
    }

    var datasetId = Guid.NewGuid();
    var storageKey = StorageKeys.Dataset(experimentId, datasetId, sanitisedFileName);

    await using (var uploadStream = tempFile.OpenRead())
    {
      await storage.PutAsync(storageKey, uploadStream, "text/csv", cancellationToken);
    }

    var dataset = new Dataset
    {
      Id = datasetId,
      ExperimentId = experimentId,
      FileName = sanitisedFileName,
      SizeBytes = sizeBytes,
      StorageKey = storageKey,
      RowCount = inspection.RowCount,
      Columns = [.. inspection.Columns],
      CreatedAt = time.GetUtcNow(),
    };

    db.Datasets.Add(dataset);
    await db.SaveChangesAsync(cancellationToken);

    return ToResponse(dataset);
  }

  private async Task<Experiment> FindVisible(
    Guid experimentId,
    string userId,
    bool isAdmin,
    CancellationToken cancellationToken)
  {
    var experiment = await db.Experiments.Include(x => x.Dataset)
      .SingleOrDefaultAsync(x => x.Id == experimentId, cancellationToken)
      ?? throw new ExperimentNotFoundException(experimentId);

    return experiment.OwnerSub == userId || isAdmin ? experiment : throw new ExperimentNotFoundException(experimentId);
  }

  /// <summary>Sanitises a client-supplied file name: only <c>[A-Za-z0-9._-]</c>, others become <c>_</c>, max length.</summary>
  private static string SanitiseFileName(string fileName)
  {
    var name = Path.GetFileName(fileName);
    var sanitised = new string([.. name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_')]);

    return sanitised.Length > MaxFileNameLength ? sanitised[..MaxFileNameLength] : sanitised;
  }

  private static DatasetResponse ToResponse(Dataset x) =>
    new(x.Id, x.FileName, x.SizeBytes, x.RowCount, x.CreatedAt, x.Columns);

  /// <summary>A temp file deleted when disposed, used to buffer the upload so it's read from the stream once.</summary>
  private sealed class TempFile : IAsyncDisposable
  {
    private readonly string _path = Path.GetTempFileName();

    public FileStream OpenWrite() => new(_path, FileMode.Truncate, FileAccess.Write, FileShare.None);

    public FileStream OpenRead() => new(_path, FileMode.Open, FileAccess.Read, FileShare.Read);

    public ValueTask DisposeAsync()
    {
      File.Delete(_path);
      return ValueTask.CompletedTask;
    }
  }
}
