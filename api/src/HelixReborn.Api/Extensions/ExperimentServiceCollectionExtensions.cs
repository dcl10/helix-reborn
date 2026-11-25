namespace HelixReborn.Api.Extensions;

using Services.Artifacts;
using Services.Datasets;
using Services.Experiments;

/// <summary>Registers experiments, dataset upload and the artifact registry.</summary>
public static class ExperimentServiceCollectionExtensions
{
  /// <summary>
  /// Registers <see cref="IExperimentService"/>, <see cref="IDatasetService"/> and <see cref="IArtifactService"/>.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <returns>The same service collection, for chaining.</returns>
  public static IServiceCollection AddExperiments(this IServiceCollection services)
  {
    services.AddScoped<IExperimentService, ExperimentService>();
    services.AddScoped<IDatasetService, DatasetService>();
    services.AddScoped<IArtifactService, ArtifactService>();

    return services;
  }
}
