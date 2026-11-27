namespace HelixReborn.Api.Tests;

using System.Text.Json.Nodes;
using Config;
using Fixtures;
using Microsoft.Extensions.Options;
using Services.Evaluations;
using Xunit;

public class EvaluationOptionsValidatorTests
{
  private static readonly EvaluationOptionsValidator Validator =
    new(Options.Create(new ContractsOptions()), new TestHostEnvironment(AppContext.BaseDirectory));

  private static IReadOnlyList<string> Validate(string json) => Validator.Validate(JsonNode.Parse(json)!.AsObject());

  [Theory]
  [InlineData("{}")]
  [InlineData("""{"local_methods": ["lime", "shap"], "local_max_samples": 20}""")]
  [InlineData("""{"local_methods": ["lime"], "ensemble_methods": ["mean"], "fuzzy": {}}""")]
  [InlineData("""{"plot_options": {"dpi": 200, "colour_map": "magma"}}""")]
  [InlineData("""
    {"local_methods": ["shap"], "ensemble_methods": ["mean", "majority_vote"],
     "fuzzy": {"number_clusters": 3, "cluster_names": ["low", "medium", "high"]}}
    """)]
  public void Validate_ReturnsNoErrors_ForValidOptions(string json) => Assert.Empty(Validate(json));

  [Fact]
  public void Validate_RejectsFuzzy_WithoutALocalMethod()
  {
    var errors = Validate("""{"ensemble_methods": ["mean"], "fuzzy": {}}""");

    Assert.Equal(["fuzzy: needs at least one local method and at least one ensemble method"], errors);
  }

  [Fact]
  public void Validate_RejectsFuzzy_WithoutAnEnsembleMethod()
  {
    var errors = Validate("""{"local_methods": ["lime"], "fuzzy": {}}""");

    Assert.Equal(["fuzzy: needs at least one local method and at least one ensemble method"], errors);
  }

  [Fact]
  public void Validate_RejectsClusterNames_ThatDontMatchNumberClusters()
  {
    var errors = Validate("""
      {"local_methods": ["lime"], "ensemble_methods": ["mean"],
       "fuzzy": {"number_clusters": 3, "cluster_names": ["low", "high"]}}
      """);

    Assert.Equal(["fuzzy.cluster_names: has 2 names but number_clusters is 3"], errors);
  }

  [Fact]
  public void Validate_RejectsDefaultClusterNames_WithAnotherNumberOfClusters()
  {
    var errors = Validate("""{"local_methods": ["lime"], "ensemble_methods": ["mean"], "fuzzy": {"number_clusters": 3}}""");

    Assert.Equal(["fuzzy.cluster_names: has 5 names but number_clusters is 3"], errors);
  }

  [Fact]
  public void Validate_RejectsEnsemble_WithoutAGlobalMethod()
  {
    var errors = Validate("""{"global_methods": [], "ensemble_methods": ["mean"]}""");

    Assert.Equal(["ensemble_methods: needs at least one global method"], errors);
  }

  [Theory]
  [InlineData("""{"permutation_repeats": 0}""", "permutation_repeats")]
  [InlineData("""{"local_max_samples": 5000}""", "local_max_samples")]
  [InlineData("""{"global_methods": ["gini"]}""", "global_methods")]
  [InlineData("""{"surprise": true}""", "options")]
  [InlineData("""{"fuzzy": {"number_clusters": 1}}""", "fuzzy")]
  public void Validate_ReturnsSchemaErrors_NamingTheField(string json, string field)
  {
    var errors = Validate(json);

    Assert.NotEmpty(errors);
    Assert.Contains(errors, e => e.StartsWith(field, StringComparison.Ordinal));
  }

  [Fact]
  public void Read_FillsInTheDefaults()
  {
    var options = EvaluationOptionsValidator.Read(JsonNode.Parse("""{"local_methods": ["lime"]}""")!.AsObject());

    Assert.True(options.Plots);
    Assert.Equal(["permutation", "shap"], options.GlobalMethods);
    Assert.Equal(10, options.PermutationRepeats);
    Assert.Equal(["lime"], options.LocalMethods);
    Assert.Equal(50, options.LocalMaxSamples);
    Assert.Empty(options.EnsembleMethods);
    Assert.Equal(10, options.NumFeaturesToPlot);
    Assert.Null(options.Fuzzy);
    Assert.Null(options.PlotOptions);
  }
}
