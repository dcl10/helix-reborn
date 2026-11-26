namespace HelixReborn.Api.Tests;

using Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

public class PipelineCatalogStartupTests
{
  [Fact]
  public void Startup_Fails_WhenTheCatalogIsMissing()
  {
    using var factory = new ApiFactory();
    var missing = Path.Combine(Path.GetTempPath(), $"helix-no-contracts-{Guid.NewGuid():N}");
    using var broken = factory.WithWebHostBuilder(builder =>
      builder.ConfigureAppConfiguration((_, config) =>
        config.AddInMemoryCollection(new Dictionary<string, string?> { ["Contracts:Root"] = missing })));

    var ex = Assert.ThrowsAny<Exception>(() => broken.CreateClient());

    Assert.Contains("pipeline catalog", ex.ToString(), StringComparison.OrdinalIgnoreCase);
  }
}
