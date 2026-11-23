namespace HelixReborn.Api.Extensions;

using Services.Datasets;
using Services.Experiments;

/// <summary>Registers experiments and dataset upload.</summary>
public static class ExperimentServiceCollectionExtensions
{
  /// <summary>Registers <see cref="IExperimentService"/> and <see cref="IDatasetService"/>.</summary>
  /// <param name="services">The service collection.</param>
  /// <returns>The same service collection, for chaining.</returns>
  public static IServiceCollection AddExperiments(this IServiceCollection services)
  {
    services.AddScoped<IExperimentService, ExperimentService>();
    services.AddScoped<IDatasetService, DatasetService>();

    return services;
  }
}
