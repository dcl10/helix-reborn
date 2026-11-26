namespace HelixReborn.Api.Extensions;

using Config;
using Services.Pipelines;

/// <summary>Registers the pipeline catalog and saved pipelines.</summary>
public static class PipelineServiceCollectionExtensions
{
  /// <summary>
  /// Registers <see cref="ContractsOptions"/>, the <see cref="IPipelineCatalog"/> singleton (loaded from
  /// <c>contracts/pipeline-catalog.json</c>), <see cref="PipelineCatalogStartupCheck"/>, which loads it at startup so a
  /// missing or invalid catalog stops the app, <see cref="PipelineSpecValidator"/> and <see cref="IPipelineService"/>.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <returns>The same service collection, for chaining.</returns>
  public static IServiceCollection AddPipelines(this IServiceCollection services)
  {
    services.AddOptions<ContractsOptions>()
      .BindConfiguration(ContractsOptions.SectionName)
      .ValidateDataAnnotations()
      .ValidateOnStart();

    services.AddSingleton<IPipelineCatalog, PipelineCatalog>();
    services.AddHostedService<PipelineCatalogStartupCheck>();
    services.AddSingleton<PipelineSpecValidator>();
    services.AddScoped<IPipelineService, PipelineService>();

    return services;
  }
}
