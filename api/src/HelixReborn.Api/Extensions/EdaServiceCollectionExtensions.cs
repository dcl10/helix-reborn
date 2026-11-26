namespace HelixReborn.Api.Extensions;

using Config;
using Duende.AccessTokenManagement;
using Microsoft.Extensions.Options;
using Services.Eda;

/// <summary>Registers the EDA service client and the experiment EDA endpoints' service.</summary>
public static class EdaServiceCollectionExtensions
{
  /// <summary>The Duende client-credentials client the EDA client gets its tokens from.</summary>
  public const string TokenClientName = "helix-eda";

  /// <summary>
  /// Registers <see cref="EdaOptions"/>, <see cref="IEdaService"/>, and <see cref="IEdaClient"/> as a typed
  /// <see cref="HttpClient"/> (base address <c>Eda:BaseUrl</c>, timeout <c>Eda:TimeoutSeconds</c>) whose requests carry a
  /// client-credentials token for <c>helix-api</c>. Those tokens carry <c>aud: helix-eda</c> (Keycloak's audience
  /// mapper), and Duende.AccessTokenManagement caches them until they expire. Call after <c>AddKeycloakAuth</c>.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <param name="configuration">App configuration, holding the <c>Keycloak</c> section.</param>
  /// <returns>The same service collection, for chaining.</returns>
  public static IServiceCollection AddEda(this IServiceCollection services, IConfiguration configuration)
  {
    services.AddOptions<EdaOptions>()
      .BindConfiguration(EdaOptions.SectionName)
      .ValidateDataAnnotations()
      .ValidateOnStart();

    var keycloak = configuration.GetSection(KeycloakOptions.SectionName).Get<KeycloakOptions>() ?? new KeycloakOptions();
    var clientName = ClientCredentialsClientName.Parse(TokenClientName);

    services.AddDistributedMemoryCache();
    services
      .AddClientCredentialsTokenManagement()
      .AddClient(clientName, o =>
      {
        // The same helix-api service account as the Keycloak admin client, as a separate Duende client so each
        // caches its own token. EdaClient reports a missing secret before any call is made.
        o.ClientId = ClientId.Parse(keycloak.Resource);
        o.ClientSecret = string.IsNullOrWhiteSpace(keycloak.Secret) ? null : ClientSecret.Parse(keycloak.Secret);
        o.TokenEndpoint = new Uri($"{keycloak.Authority}/protocol/openid-connect/token");
      });

    services
      .AddHttpClient<IEdaClient, EdaClient>((sp, http) =>
      {
        var options = sp.GetRequiredService<IOptions<EdaOptions>>().Value;
        http.BaseAddress = new Uri(options.BaseUrl);
        http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
      })
      .AddClientCredentialsTokenHandler(clientName);

    services.AddScoped<IEdaService, EdaService>();

    return services;
  }
}
