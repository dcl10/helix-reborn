namespace HelixReborn.Api.Extensions;

using Messaging;
using Services.Evaluations;

/// <summary>Registers evaluations.</summary>
public static class EvaluationServiceCollectionExtensions
{
  /// <summary>
  /// Registers <see cref="EvaluationOptionsValidator"/> (loaded from
  /// <c>contracts/messages/evaluation-job.schema.json</c>), the evaluation <see cref="IJobEventHandler"/> and
  /// <see cref="IEvaluationService"/>. Call after <c>AddPipelines</c> (which binds the contracts options) and
  /// <c>AddRuns</c> (which registers the publisher and the event consumer).
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <returns>The same service collection, for chaining.</returns>
  public static IServiceCollection AddEvaluations(this IServiceCollection services)
  {
    services.AddSingleton<EvaluationOptionsValidator>();
    services.AddScoped<IJobEventHandler, EvaluationEventHandler>();
    services.AddScoped<IEvaluationService, EvaluationService>();

    return services;
  }
}
