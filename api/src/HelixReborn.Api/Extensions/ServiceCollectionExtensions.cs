namespace HelixReborn.Api.Extensions;

using Config;

/// <summary>Service registrations used by <c>Program.cs</c>.</summary>
public static class ServiceCollectionExtensions
{
  /// <summary>
  /// Binds the typed options from configuration and validates them when the app starts, so a bad value stops
  /// startup instead of failing on first use. Secrets may be empty at startup; the features that need them check
  /// for them.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <returns>The same service collection, for chaining.</returns>
  public static IServiceCollection AddAppOptions(this IServiceCollection services)
  {
    services.AddValidatedOptions<HelixOptions>(HelixOptions.SectionName);

    return services;
  }

  private static void AddValidatedOptions<TOptions>(this IServiceCollection services, string sectionName)
    where TOptions : class =>
    services.AddOptions<TOptions>()
      .BindConfiguration(sectionName)
      .ValidateDataAnnotations()
      .ValidateOnStart();
}
