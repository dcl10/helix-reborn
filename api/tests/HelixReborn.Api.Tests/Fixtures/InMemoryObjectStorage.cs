namespace HelixReborn.Api.Tests.Fixtures;

using System.Collections.Concurrent;
using Services.Storage;

/// <summary>Dictionary-backed <see cref="IObjectStorage"/> for tests, which have no S3-compatible store.</summary>
public class InMemoryObjectStorage : IObjectStorage
{
  private readonly ConcurrentDictionary<string, (byte[] Content, string ContentType, DateTimeOffset LastModified)> _objects = new();

  /// <inheritdoc />
  public async Task PutAsync(
    string key,
    Stream content,
    string contentType,
    CancellationToken cancellationToken = default)
  {
    using var buffer = new MemoryStream();
    await content.CopyToAsync(buffer, cancellationToken);
    _objects[key] = (buffer.ToArray(), contentType, DateTimeOffset.UtcNow);
  }

  /// <inheritdoc />
  public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
    _objects.TryGetValue(key, out var stored)
      ? Task.FromResult<Stream>(new MemoryStream(stored.Content))
      : throw new ObjectNotFoundException(key);

  /// <inheritdoc />
  public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
    Task.FromResult(_objects.ContainsKey(key));

  /// <inheritdoc />
  public Task<IReadOnlyList<StoredObject>> ListAsync(string prefix, CancellationToken cancellationToken = default) =>
    Task.FromResult<IReadOnlyList<StoredObject>>(
      [.. _objects
        .Where(x => x.Key.StartsWith(prefix, StringComparison.Ordinal))
        .Select(x => new StoredObject(x.Key, x.Value.Content.Length, x.Value.LastModified))]);

  /// <inheritdoc />
  public Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default)
  {
    foreach (var key in _objects.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)))
    {
      _objects.TryRemove(key, out _);
    }

    return Task.CompletedTask;
  }
}
