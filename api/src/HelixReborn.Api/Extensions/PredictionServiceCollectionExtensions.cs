namespace HelixReborn.Api.Extensions;

using Config;
using Filters;
using Services.Predictions;

/// <summary>Registers predictions.</summary>
public static class PredictionServiceCollectionExtensions
{
  /// <summary>
  /// Registers <see cref="PredictionOptions"/> (validated at startup), the singleton <see cref="OnnxModelCache"/>, the
  /// <see cref="PredictionUploadLimitFilter"/> and <see cref="IPredictionService"/>. Call after <c>AddObjectStorage</c>
  /// and <c>AddExperiments</c>.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <returns>The same service collection, for chaining.</returns>
  public static IServiceCollection AddPredictions(this IServiceCollection services)
  {
    services.AddOptions<PredictionOptions>()
      .BindConfiguration(PredictionOptions.SectionName)
      .ValidateDataAnnotations()
      .ValidateOnStart();

    services.AddSingleton<OnnxModelCache>();
    services.AddSingleton<PredictionUploadLimitFilter>();
    services.AddScoped<IPredictionService, PredictionService>();

    return services;
  }
}
