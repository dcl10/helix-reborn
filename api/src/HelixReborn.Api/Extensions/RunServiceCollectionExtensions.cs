namespace HelixReborn.Api.Extensions;

using Config;
using Messaging;
using Services.Runs;

/// <summary>Registers RabbitMQ messaging and training runs.</summary>
public static class RunServiceCollectionExtensions
{
  /// <summary>
  /// Registers <see cref="RabbitMqOptions"/>, the shared <see cref="RabbitMqConnection"/>, <see cref="IJobPublisher"/>,
  /// the <see cref="JobEventConsumer"/> background service (which keeps retrying while RabbitMQ is down, so the API
  /// starts without it), the training <see cref="IJobEventHandler"/> and <see cref="IRunService"/>. Call after
  /// <c>AddPipelines</c>, whose validator runs re-validate with.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <returns>The same service collection, for chaining.</returns>
  public static IServiceCollection AddRuns(this IServiceCollection services)
  {
    services.AddOptions<RabbitMqOptions>()
      .BindConfiguration(RabbitMqOptions.SectionName)
      .ValidateDataAnnotations()
      .ValidateOnStart();

    services.AddSingleton<RabbitMqConnection>();
    services.AddSingleton<IJobPublisher, RabbitMqJobPublisher>();
    services.AddHostedService<JobEventConsumer>();

    services.AddScoped<IJobEventHandler, RunEventHandler>();
    services.AddScoped<IRunService, RunService>();

    return services;
  }
}
