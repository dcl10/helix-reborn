namespace HelixReborn.Api.Extensions;

using Amazon.S3;
using Config;
using Microsoft.Extensions.Options;
using Services.Storage;

/// <summary>Registers object storage.</summary>
public static class StorageServiceCollectionExtensions
{
  /// <summary>
  /// Registers <see cref="S3Options"/> and an <see cref="IObjectStorage"/> backed by an <see cref="IAmazonS3"/>
  /// client pointed at that bucket (MinIO/RustFS locally, DO Spaces in production; <see cref="S3Options.ForcePathStyle"/>
  /// is required for the former).
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <returns>The same service collection, for chaining.</returns>
  public static IServiceCollection AddObjectStorage(this IServiceCollection services)
  {
    services.AddOptions<S3Options>()
      .BindConfiguration(S3Options.SectionName)
      .ValidateDataAnnotations()
      .ValidateOnStart();

    services.AddSingleton<IAmazonS3>(sp =>
    {
      var options = sp.GetRequiredService<IOptions<S3Options>>().Value;
      var config = new AmazonS3Config
      {
        ServiceURL = options.ServiceUrl,
        ForcePathStyle = options.ForcePathStyle,
        AuthenticationRegion = options.Region,
      };

      return new AmazonS3Client(options.AccessKey, options.SecretKey, config);
    });

    services.AddScoped<IObjectStorage, S3ObjectStorage>();

    return services;
  }
}
