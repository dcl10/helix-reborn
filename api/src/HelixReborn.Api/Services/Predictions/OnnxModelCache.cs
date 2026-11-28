namespace HelixReborn.Api.Services.Predictions;

using Config;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Storage;

/// <summary>
/// A run's model loaded for prediction: its ONNX Runtime session and metadata. Sessions are safe for concurrent
/// <c>Run</c> calls, so one is shared by every request for the run.
/// </summary>
/// <param name="Session">The ONNX Runtime session over <c>model.onnx</c>.</param>
/// <param name="Metadata">The run's <c>metadata.json</c>.</param>
public sealed record LoadedModel(InferenceSession Session, ModelMetadata Metadata);

/// <summary>
/// Loaded models by run ID, read from object storage on first use. A least-recently-used cache bounded by
/// <see cref="PredictionOptions.ModelCacheSize"/>. Thread-safe: concurrent first requests for a run share one load, and
/// callers hold a <see cref="ModelLease"/> while they use a model, so an evicted session is only disposed once the
/// last request using it is done. A failed load isn't cached.
/// </summary>
/// <param name="scopes">A scope per load, for the scoped <see cref="IObjectStorage"/>.</param>
/// <param name="options">The cache size.</param>
/// <param name="logger">Logs loads and evictions.</param>
public sealed partial class OnnxModelCache(
  IServiceScopeFactory scopes,
  IOptions<PredictionOptions> options,
  ILogger<OnnxModelCache> logger) : IDisposable
{
  private readonly Lock _lock = new();
  private readonly Dictionary<Guid, LinkedListNode<Entry>> _entries = [];

  // Most recently used first.
  private readonly LinkedList<Entry> _lru = new();
  private bool _disposed;

  /// <summary>The number of models currently cached (loaded or loading).</summary>
  public int Count
  {
    get
    {
      lock (_lock)
      {
        return _entries.Count;
      }
    }
  }

  /// <summary>Gets a run's model, loading it from <c>experiments/{e}/runs/{r}/</c> if it isn't cached.</summary>
  /// <param name="experimentId">The run's experiment.</param>
  /// <param name="runId">The run.</param>
  /// <param name="cancellationToken">Stops waiting; a load other requests share carries on.</param>
  /// <returns>A lease on the model; dispose it when done with the model.</returns>
  /// <exception cref="PredictionConflictException">The model files are missing or unusable.</exception>
  public async Task<ModelLease> AcquireAsync(Guid experimentId, Guid runId, CancellationToken cancellationToken)
  {
    Entry entry;
    List<Entry> evicted = [];
    lock (_lock)
    {
      ObjectDisposedException.ThrowIf(_disposed, this);

      if (_entries.TryGetValue(runId, out var node))
      {
        _lru.Remove(node);
        _lru.AddFirst(node);
        entry = node.Value;
      }
      else
      {
        // Not tied to the request's token: other requests may join this load.
        entry = new Entry(runId, Task.Run(() => LoadAsync(experimentId, runId), CancellationToken.None));
        _entries[runId] = _lru.AddFirst(entry);
        while (_entries.Count > options.Value.ModelCacheSize)
        {
          var last = _lru.Last!.Value;
          _lru.RemoveLast();
          _entries.Remove(last.RunId);
          evicted.Add(last);
        }
      }

      entry.AddRef();
    }

    foreach (var old in evicted)
    {
      LogEvicted(logger, old.RunId);
      old.Release();
    }

    try
    {
      var model = await entry.Model.WaitAsync(cancellationToken);
      return new ModelLease(entry, model);
    }
    catch
    {
      if (entry.Model.IsFaulted)
      {
        Forget(entry);
      }

      entry.Release();
      throw;
    }
  }

  /// <summary>Disposes every cached session once no request is using it.</summary>
  public void Dispose()
  {
    List<Entry> all;
    lock (_lock)
    {
      if (_disposed)
      {
        return;
      }

      _disposed = true;
      all = [.. _lru];
      _lru.Clear();
      _entries.Clear();
    }

    foreach (var entry in all)
    {
      entry.Release();
    }
  }

  /// <summary>Removes a failed entry, if it's still the cached one for its run.</summary>
  private void Forget(Entry entry)
  {
    var removed = false;
    lock (_lock)
    {
      if (_entries.TryGetValue(entry.RunId, out var node) && node.Value == entry)
      {
        _lru.Remove(node);
        _entries.Remove(entry.RunId);
        removed = true;
      }
    }

    if (removed)
    {
      entry.Release();
    }
  }

  /// <summary>Reads <c>metadata.json</c> and <c>model.onnx</c> and creates the session.</summary>
  private async Task<LoadedModel> LoadAsync(Guid experimentId, Guid runId)
  {
    var prefix = StorageKeys.RunPrefix(experimentId, runId);
    using var scope = scopes.CreateScope();
    var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();

    try
    {
      var metadata = ModelMetadata.Parse(await ReadAllAsync(storage, $"{prefix}metadata.json"));
      var model = await ReadAllAsync(storage, $"{prefix}model.onnx");
      var session = new InferenceSession(model);
      LogLoaded(logger, runId);
      return new LoadedModel(session, metadata);
    }
    catch (ObjectNotFoundException ex)
    {
      throw new PredictionConflictException("The run's model files aren't in storage.", ex);
    }
    catch (InvalidDataException ex)
    {
      throw new PredictionConflictException($"The run's model can't be used: {ex.Message}", ex);
    }
    catch (OnnxRuntimeException ex)
    {
      throw new PredictionConflictException($"The run's model.onnx can't be loaded: {ex.Message}", ex);
    }
  }

  [LoggerMessage(Level = LogLevel.Information, Message = "Loaded run {RunId}'s model for predictions")]
  private static partial void LogLoaded(ILogger logger, Guid runId);

  [LoggerMessage(Level = LogLevel.Information, Message = "Evicted run {RunId}'s model from the prediction cache")]
  private static partial void LogEvicted(ILogger logger, Guid runId);

  private static async Task<byte[]> ReadAllAsync(IObjectStorage storage, string key)
  {
    await using var stream = await storage.OpenReadAsync(key);
    using var buffer = new MemoryStream();
    await stream.CopyToAsync(buffer);
    return buffer.ToArray();
  }

  /// <summary>
  /// One run's cached model, reference counted: the cache holds one reference while the entry is cached, and each
  /// lease another. The session is disposed when the count reaches zero.
  /// </summary>
  /// <param name="runId">The run.</param>
  /// <param name="model">The load.</param>
  internal sealed class Entry(Guid runId, Task<LoadedModel> model)
  {
    private int _references = 1;

    /// <summary>The run.</summary>
    public Guid RunId { get; } = runId;

    /// <summary>The load; completed once the model is loaded or has failed to.</summary>
    public Task<LoadedModel> Model { get; } = model;

    /// <summary>Adds a reference.</summary>
    public void AddRef() => Interlocked.Increment(ref _references);

    /// <summary>Drops a reference, disposing the session (once loaded) when none are left.</summary>
    public void Release()
    {
      if (Interlocked.Decrement(ref _references) == 0)
      {
        _ = Model.ContinueWith(
          t =>
          {
            if (t.IsCompletedSuccessfully)
            {
              t.Result.Session.Dispose();
            }
          },
          CancellationToken.None,
          TaskContinuationOptions.ExecuteSynchronously,
          TaskScheduler.Default);
      }
    }
  }
}

/// <summary>A request's hold on a cached model. Dispose it when done, so an evicted session can be disposed.</summary>
public sealed class ModelLease : IDisposable
{
  private OnnxModelCache.Entry? _entry;

  internal ModelLease(OnnxModelCache.Entry entry, LoadedModel model)
  {
    _entry = entry;
    Model = model;
  }

  /// <summary>The model.</summary>
  public LoadedModel Model { get; }

  /// <summary>Releases the hold.</summary>
  public void Dispose() => Interlocked.Exchange(ref _entry, null)?.Release();
}
