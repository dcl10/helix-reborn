namespace HelixReborn.Api.Services.Storage;

/// <summary>
/// The S3-compatible object store (RustFS locally, DO Spaces in production). Behind an interface so tests can
/// use an in-memory fake.
/// </summary>
public interface IObjectStorage
{
  /// <summary>Uploads content to a key, overwriting anything already there.</summary>
  /// <param name="key">The object key.</param>
  /// <param name="content">The content to upload.</param>
  /// <param name="contentType">The object's content type.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default);

  /// <summary>Opens a stream to read an object.</summary>
  /// <param name="key">The object key.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>A readable stream positioned at the start of the object.</returns>
  /// <exception cref="ObjectNotFoundException">No object has this key.</exception>
  Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default);

  /// <summary>Checks whether an object exists.</summary>
  /// <param name="key">The object key.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>Whether the object exists.</returns>
  Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

  /// <summary>Lists every object whose key starts with a prefix.</summary>
  /// <param name="prefix">The key prefix.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The matching objects.</returns>
  Task<IReadOnlyList<StoredObject>> ListAsync(string prefix, CancellationToken cancellationToken = default);

  /// <summary>Deletes every object whose key starts with a prefix.</summary>
  /// <param name="prefix">The key prefix.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default);
}

/// <summary>An object as listed from the store.</summary>
/// <param name="Key">The object key.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="LastModified">When the object was last written.</param>
public record StoredObject(string Key, long Size, DateTimeOffset LastModified);
