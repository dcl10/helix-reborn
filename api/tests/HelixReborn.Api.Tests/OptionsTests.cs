namespace HelixReborn.Api.Tests;

using Config;
using Extensions;
using Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

public class OptionsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  [Fact]
  public void HelixOptions_DefaultsToDev_WhenNotOverridden()
  {
    var options = factory.Services.GetRequiredService<IOptions<HelixOptions>>().Value;

    Assert.Equal("dev", options.Version);
  }

  [Fact]
  public void HelixOptions_Binds_WhenConfigured()
  {
    using var configured = factory.WithWebHostBuilder(builder =>
      builder.ConfigureAppConfiguration((_, config) =>
        config.AddInMemoryCollection(new Dictionary<string, string?> { ["Helix:Version"] = "abc123" })));

    var options = configured.Services.GetRequiredService<IOptions<HelixOptions>>().Value;

    Assert.Equal("abc123", options.Version);
  }

  [Fact]
  public void StartupValidation_Passes_WhenOptionsUseDefaults()
  {
    using var provider = BuildOptionsProvider(new()
    {
      ["Keycloak:AuthServerUrl"] = "http://localhost:8080",
      ["Keycloak:Realm"] = "helix",
      ["Keycloak:Resource"] = "helix-api",
    });

    provider.GetRequiredService<IStartupValidator>().Validate();
  }

  [Fact]
  public void StartupValidation_Fails_WhenKeycloakRealmIsMissing()
  {
    using var provider = BuildOptionsProvider(new()
    {
      ["Keycloak:AuthServerUrl"] = "http://localhost:8080",
      ["Keycloak:Resource"] = "helix-api",
    });

    Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
  }

  private static ServiceProvider BuildOptionsProvider(Dictionary<string, string?> settings)
  {
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    return new ServiceCollection()
      .AddSingleton<IConfiguration>(configuration)
      .AddAppOptions()
      .AddOptions<KeycloakOptions>()
      .BindConfiguration(KeycloakOptions.SectionName)
      .ValidateDataAnnotations()
      .ValidateOnStart()
      .Services
      .BuildServiceProvider();
  }
}
