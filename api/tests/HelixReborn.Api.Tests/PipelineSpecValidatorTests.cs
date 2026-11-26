namespace HelixReborn.Api.Tests;

using System.Text.Json.Nodes;
using Models;
using Services.Pipelines;
using Xunit;

public class PipelineSpecValidatorTests
{
  private static readonly PipelineSpecValidator Validator = new(PipelineCatalogTests.RepoCatalog());

  private static readonly Dataset Dataset = new()
  {
    FileName = "data.csv",
    StorageKey = "experiments/e/datasets/d/data.csv",
    Columns =
    [
      new("a", "numeric", 0),
      new("b", "numeric", 2),
      new("c", "numeric", 0),
      new("site", "text", 0),
      new("y", "numeric", 0),
      new("label", "text", 0),
    ],
  };

  private static IReadOnlyList<string> Validate(string specJson) =>
    Validator.Validate(PipelineSpecJson.Read(JsonNode.Parse(specJson)!.AsObject()), Dataset);

  private static string Regression(string model = """{"step": "random_forest_regressor", "params": {"n_estimators": 50}}""",
    string steps = """[{"step": "simple_imputer", "params": {"strategy": "mean"}}, {"step": "standard_scaler"}]""",
    string features = """["a", "b", "c"]""",
    string target = "y",
    string search = "null") =>
    $$"""
    {"problem_type": "regression", "target_column": "{{target}}", "feature_columns": {{features}},
     "steps": {{steps}}, "model": {{model}}, "search": {{search}}}
    """;

  private static string Classification(string model, string search = "null") =>
    $$"""
    {"problem_type": "classification", "target_column": "label", "feature_columns": ["a", "b"],
     "split": {"test_size": 0.25, "random_seed": 7, "cv_folds": 5}, "model": {{model}}, "search": {{search}}}
    """;

  [Fact]
  public void Validate_ReturnsNoErrors_ForAValidRegressionSpec()
  {
    Assert.Empty(Validate(Regression()));
  }

  [Fact]
  public void Validate_ReturnsNoErrors_ForAValidClassificationSpecWithSearch()
  {
    Assert.Empty(Validate(Classification(
      """{"step": "logistic_regression"}""",
      """{"method": "random", "cv_folds": 3, "n_iter": 4, "param_grid": {"C": [0.1, 1, 10]}}""")));
    Assert.Empty(Validate(Classification("""{"step": "random_forest_classifier"}""", """{"method": "grid"}""")));
  }

  [Theory]
  [InlineData("""[{"step": "magic"}]""", "steps[0].step: unknown step 'magic'")]
  [InlineData("""[{"step": "standard_scaler"}, {"step": "ridge"}]""", "steps[1].step: 'ridge' is a model")]
  public void Validate_RejectsBadSteps(string steps, string expected)
  {
    Assert.Contains(Validate(Regression(steps: steps)), e => e.StartsWith(expected, StringComparison.Ordinal));
  }

  [Fact]
  public void Validate_RejectsANonModelAsTheModel()
  {
    Assert.Contains("model.step: 'standard_scaler' isn't a model", Validate(Regression("""{"step": "standard_scaler"}""")));
  }

  [Fact]
  public void Validate_RejectsAModelForTheWrongProblemType()
  {
    Assert.Contains(
      Validate(Regression("""{"step": "logistic_regression"}""")),
      e => e.StartsWith("model.step: 'logistic_regression' doesn't support regression", StringComparison.Ordinal));
  }

  [Theory]
  [InlineData("""{"n_estimators": 0}""", "model.params.n_estimators: ")]
  [InlineData("""{"n_estimators": "many"}""", "model.params.n_estimators: ")]
  [InlineData("""{"max_depth": 500}""", "model.params.max_depth: ")]
  public void Validate_RejectsAParamOutOfRangeOrOfTheWrongType(string parameters, string expectedPrefix)
  {
    var errors = Validate(Regression($$"""{"step": "random_forest_regressor", "params": {{parameters}}}"""));

    Assert.Contains(errors, e => e.StartsWith(expectedPrefix, StringComparison.Ordinal));
  }

  [Fact]
  public void Validate_RejectsAnUnknownParam()
  {
    var errors = Validate(Regression("""{"step": "ridge", "params": {"bogus": 1}}"""));

    Assert.Contains("model.params.bogus: 'ridge' has no parameter by that name", errors);
  }

  [Fact]
  public void Validate_NamesTheListItem_ForANestedParamError()
  {
    var errors = Validate(Regression("""{"step": "mlp_regressor", "params": {"hidden_layer_sizes": [10, 0]}}"""));

    Assert.Contains(errors, e => e.StartsWith("model.params.hidden_layer_sizes[1]: ", StringComparison.Ordinal));
  }

  [Theory]
  [InlineData("""["a", "site"]""", "y", "feature_columns: 'site' is not numeric")]
  [InlineData("""["a", "nope"]""", "y", "feature_columns: 'nope' isn't a column in the dataset")]
  [InlineData("""["a", "a"]""", "y", "feature_columns: 'a' is listed more than once")]
  [InlineData("""["a", "y"]""", "y", "target_column: 'y' is also a feature column")]
  [InlineData("""["a"]""", "label", "target_column: 'label' is not numeric, so it can't be a regression target")]
  [InlineData("""[]""", "y", "feature_columns: choose at least one column")]
  public void Validate_RejectsBadColumns(string features, string target, string expected)
  {
    Assert.Contains(expected, Validate(Regression(features: features, target: target)));
  }

  [Fact]
  public void Validate_RejectsARandomSearchWithoutNIter()
  {
    Assert.Contains(
      "search.n_iter: is required for a random search",
      Validate(Regression(search: """{"method": "random"}""")));
  }

  [Fact]
  public void Validate_RejectsAnUnknownParamGridKey()
  {
    Assert.Contains(
      "search.param_grid.bogus: 'random_forest_regressor' has no parameter 'bogus'",
      Validate(Regression(search: """{"method": "grid", "param_grid": {"bogus": [1]}}""")));
  }

  [Fact]
  public void Validate_RejectsAParamGridValueOutOfRange()
  {
    var errors = Validate(Regression(search: """{"method": "grid", "param_grid": {"n_estimators": [10, 0]}}"""));

    Assert.Contains(errors, e => e.StartsWith("search.param_grid.n_estimators[1]: ", StringComparison.Ordinal));
    Assert.DoesNotContain(errors, e => e.StartsWith("search.param_grid.n_estimators[0]", StringComparison.Ordinal));
  }

  [Fact]
  public void Validate_RejectsAnEmptyParamGridList()
  {
    Assert.Contains(
      "search.param_grid.max_depth: must be a list of at least one value",
      Validate(Regression(search: """{"method": "grid", "param_grid": {"max_depth": []}}""")));
  }

  [Fact]
  public void Validate_RejectsASplitOutOfRange()
  {
    var errors = Validate(
      """
      {"problem_type": "regression", "target_column": "y", "feature_columns": ["a"],
       "split": {"test_size": 0.9, "cv_folds": 1}, "model": {"step": "ridge"}}
      """);

    Assert.Contains("split.test_size: must be between 0.05 and 0.5", errors);
    Assert.Contains("split.cv_folds: must be between 2 and 10, or null", errors);
  }

  [Fact]
  public void Read_RejectsAnUnknownSpecField()
  {
    Assert.Throws<System.Text.Json.JsonException>(() => PipelineSpecJson.Read(JsonNode.Parse(
      """{"problem_type": "regression", "target_column": "y", "feature_columns": ["a"], "model": {"step": "ridge"}, "extra": 1}""")!
      .AsObject()));
  }

  [Fact]
  public void Write_FillsInTheDefaults_InSnakeCase()
  {
    var json = PipelineSpecJson.Write(PipelineSpecJson.Read(JsonNode.Parse(
      """{"problem_type": "regression", "target_column": "y", "feature_columns": ["a"], "model": {"step": "ridge"}}""")!
      .AsObject()));

    var spec = JsonNode.Parse(json)!.AsObject();
    Assert.Equal(0.2, (double)spec["split"]!["test_size"]!);
    Assert.Equal(42, (int)spec["split"]!["random_seed"]!);
    Assert.Empty(spec["steps"]!.AsArray());
    Assert.Empty(spec["model"]!["params"]!.AsObject());
  }
}
