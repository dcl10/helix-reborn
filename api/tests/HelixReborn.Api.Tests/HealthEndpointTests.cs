namespace HelixReborn.Api.Tests;

using System.Net;
using System.Text.Json;
using Fixtures;
using Xunit;

public class HealthEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  [Fact]
  public async Task GetHealth_ReturnsHealthy_WithoutAuthentication()
  {
    var response = await factory.CreateClient().GetAsync("/health");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task GetHealth_ReturnsVersion()
  {
    var response = await factory.CreateClient().GetAsync("/health");

    using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());
    Assert.Equal("dev", body.RootElement.GetProperty("version").GetString());
  }
}
