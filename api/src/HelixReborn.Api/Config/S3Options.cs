namespace HelixReborn.Api.Config;

using System.ComponentModel.DataAnnotations;

/// <summary>Where the API finds the S3-compatible object store (RustFS locally, DO Spaces in production).</summary>
public class S3Options
{
  /// <summary>Configuration section these options bind to.</summary>
  public const string SectionName = "S3";

  /// <summary>The S3 endpoint, e.g. <c>http://localhost:9000</c> locally.</summary>
  [Required]
  public string ServiceUrl { get; init; } = "http://localhost:9000";

  /// <summary>The AWS region to sign requests for.</summary>
  [Required]
  public string Region { get; init; } = "us-east-1";

  /// <summary>Access key for the object store.</summary>
  [Required]
  public string AccessKey { get; init; } = "helix";

  /// <summary>Secret key for the object store. Secret: user-secrets or the environment only.</summary>
  [Required]
  public string SecretKey { get; init; } = "dev_password";

  /// <summary>The bucket holding every experiment's objects.</summary>
  [Required]
  public string Bucket { get; init; } = "helix";

  /// <summary>
  /// Whether to address the bucket as a path (<c>http://host/bucket/key</c>) rather than a subdomain. Required for
  /// RustFS; DO Spaces supports either.
  /// </summary>
  public bool ForcePathStyle { get; init; } = true;
}
