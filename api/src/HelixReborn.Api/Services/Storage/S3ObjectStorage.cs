namespace HelixReborn.Api.Services.Storage;

using Amazon.S3;
using Amazon.S3.Model;
using Config;
using Microsoft.Extensions.Options;

/// <summary><see cref="IObjectStorage"/> on an S3-compatible bucket (MinIO/RustFS locally, DO Spaces in production).</summary>
/// <param name="client">The S3 client, configured for <see cref="S3Options"/>.</param>
/// <param name="options">The bucket to use.</param>
public class S3ObjectStorage(IAmazonS3 client, IOptions<S3Options> options) : IObjectStorage
{
  /// <summary>The maximum number of keys S3's delete API accepts in one request.</summary>
  private const int DeleteBatchSize = 1000;

  private readonly string _bucket = options.Value.Bucket;

  /// <inheritdoc />
  public async Task PutAsync(
    string key,
    Stream content,
    string contentType,
    CancellationToken cancellationToken = default) =>
    await client.PutObjectAsync(
      new PutObjectRequest { BucketName = _bucket, Key = key, InputStream = content, ContentType = contentType },
      cancellationToken);

  /// <inheritdoc />
  public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await client.GetObjectAsync(
        new GetObjectRequest { BucketName = _bucket, Key = key },
        cancellationToken);
      return response.ResponseStream;
    }
    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
      throw new ObjectNotFoundException(key);
    }
  }

  /// <inheritdoc />
  public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
  {
    try
    {
      await client.GetObjectMetadataAsync(
        new GetObjectMetadataRequest { BucketName = _bucket, Key = key },
        cancellationToken);
      return true;
    }
    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
      return false;
    }
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<StoredObject>> ListAsync(
    string prefix,
    CancellationToken cancellationToken = default)
  {
    List<StoredObject> objects = [];
    string? continuationToken = null;

    do
    {
      var response = await client.ListObjectsV2Async(
        new ListObjectsV2Request { BucketName = _bucket, Prefix = prefix, ContinuationToken = continuationToken },
        cancellationToken);

      objects.AddRange(response.S3Objects.Select(x => new StoredObject(x.Key, x.Size, x.LastModified)));
      continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;
    }
    while (continuationToken is not null);

    return objects;
  }

  /// <inheritdoc />
  public async Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default)
  {
    var objects = await ListAsync(prefix, cancellationToken);

    foreach (var batch in objects.Chunk(DeleteBatchSize))
    {
      await client.DeleteObjectsAsync(
        new DeleteObjectsRequest
        {
          BucketName = _bucket,
          Objects = [.. batch.Select(x => new KeyVersion { Key = x.Key })],
        },
        cancellationToken);
    }
  }
}
