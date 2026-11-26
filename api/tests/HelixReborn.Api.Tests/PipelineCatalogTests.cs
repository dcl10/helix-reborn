namespace HelixReborn.Api.Tests;

using Config;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Services.Pipelines;
using Xunit;

public class PipelineCatalogTests
{
  /// <summary>The repo's real catalog, found by walking up from the test's output directory.</summary>
  public static PipelineCatalog RepoCatalog() =>
    new(Options.Create(new ContractsOptions()), new TestEnvironment(AppContext.BaseDirectory));

  [Fact]
  public void Constructor_LoadsTheRepoCatalog_FromARelativeRoot()
  {
    var catalog = RepoCatalog();

    Assert.EndsWith(Path.Combine("contracts", "pipeline-catalog.json"), catalog.Path);
    Assert.Equal(File.ReadAllText(catalog.Path), catalog.RawJson);
    Assert.Contains("random_forest_regressor", catalog.Steps.Keys);
  }

  [Fact]
  public void Constructor_CompilesEveryStepsParamsSchema()
  {
    var catalog = RepoCatalog();

    Assert.NotEmpty(catalog.Steps);
    foreach (var step in catalog.Steps.Values)
    {
      // Every param has a default, so no params is always valid.
      Assert.True(step.ParamsSchema.Evaluate(System.Text.Json.JsonDocument.Parse("{}").RootElement).IsValid, step.Id);
      Assert.NotEmpty(step.ProblemTypes);
    }
  }

  [Fact]
  public void Constructor_ReadsKindsAndSearchGrids()
  {
    var catalog = RepoCatalog();

    Assert.Equal("model", catalog.Steps["ridge"].Kind);
    Assert.True(catalog.Steps["ridge"].HasSearchGrid);
    Assert.Equal("preprocessor", catalog.Steps["standard_scaler"].Kind);
    Assert.False(catalog.Steps["standard_scaler"].HasSearchGrid);
    Assert.Contains("alpha", catalog.Steps["ridge"].ParamNames);
  }

  [Fact]
  public void Constructor_Throws_WhenTheCatalogIsMissing()
  {
    var root = Path.Combine(Path.GetTempPath(), $"helix-no-contracts-{Guid.NewGuid():N}");

    var ex = Assert.Throws<InvalidOperationException>(() => new PipelineCatalog(
      Options.Create(new ContractsOptions { Root = root }),
      new TestEnvironment(AppContext.BaseDirectory)));

    Assert.Contains("doesn't exist", ex.Message);
  }

  [Fact]
  public void Constructor_Throws_WhenTheCatalogIsInvalid()
  {
    var root = Directory.CreateTempSubdirectory("helix-contracts-").FullName;
    try
    {
      File.WriteAllText(Path.Combine(root, PipelineCatalog.FileName), """{"schema_version": 2, "steps": []}""");

      var ex = Assert.Throws<InvalidOperationException>(() => new PipelineCatalog(
        Options.Create(new ContractsOptions { Root = root }),
        new TestEnvironment(AppContext.BaseDirectory)));

      Assert.Contains("schema_version", ex.Message);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  private sealed class TestEnvironment(string contentRoot) : IHostEnvironment
  {
    public string EnvironmentName { get; set; } = "Testing";

    public string ApplicationName { get; set; } = "HelixReborn.Api";

    public string ContentRootPath { get; set; } = contentRoot;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
  }
}
