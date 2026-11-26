namespace HelixReborn.Api.Services.Pipelines;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Models;

/// <summary>
/// Checks a <see cref="PipelineSpec"/> against the catalog and the experiment's dataset, applying the same rules as
/// helix-core's <c>PipelineSpec</c> and <c>validate_spec</c> (see <c>docs/pipelines.md</c>), so a saved pipeline
/// never fails on those when it's run.
/// </summary>
/// <param name="catalog">The step catalog.</param>
public class PipelineSpecValidator(IPipelineCatalog catalog)
{
  private const string Numeric = "numeric";
  private static readonly string[] ProblemTypes = ["classification", "regression"];
  private static readonly string[] SearchMethods = ["grid", "random"];
  private static readonly EvaluationOptions Evaluation = new() { OutputFormat = OutputFormat.List };

  /// <summary>
  /// Keywords whose errors only summarise their children's ("some properties did not match"); the children's errors
  /// say what's wrong.
  /// </summary>
  private static readonly HashSet<string> SummaryKeywords =
    ["properties", "additionalProperties", "patternProperties", "items", "prefixItems", "anyOf", "oneOf", "allOf", "$ref"];

  /// <summary>Validates a spec.</summary>
  /// <param name="spec">The spec.</param>
  /// <param name="dataset">The experiment's dataset, for its columns.</param>
  /// <returns>Human-readable errors, each starting with the field's path, e.g.
  /// <c>"steps[1].params.k: must be at least 1"</c>; empty when the spec is valid.</returns>
  public IReadOnlyList<string> Validate(PipelineSpec spec, Dataset dataset)
  {
    var errors = new List<string>();
    var columns = dataset.Columns.ToDictionary(c => c.Name, StringComparer.Ordinal);

    var problemTypeValid = ProblemTypes.Contains(spec.ProblemType);
    if (!problemTypeValid)
    {
      errors.Add("problem_type: must be 'classification' or 'regression'");
    }

    ValidateColumns(spec, columns, errors);
    ValidateSplit(spec.Split, errors);

    for (var i = 0; i < spec.Steps.Count; i++)
    {
      var step = spec.Steps[i];
      if (ValidateStep(step, $"steps[{i}]", spec, problemTypeValid, errors) is { Kind: CatalogStep.ModelKind })
      {
        errors.Add($"steps[{i}].step: '{step.Step}' is a model; put it in 'model', not 'steps'");
      }
    }

    CatalogStep? model = null;
    if (spec.Model is null)
    {
      errors.Add("model: is required");
    }
    else if (ValidateStep(spec.Model, "model", spec, problemTypeValid, errors) is { } modelStep)
    {
      if (modelStep.Kind == CatalogStep.ModelKind)
      {
        model = modelStep;
      }
      else
      {
        errors.Add($"model.step: '{spec.Model.Step}' isn't a model");
      }
    }

    if (spec.Search is not null)
    {
      ValidateSearch(spec.Search, model, errors);
    }

    return errors;
  }

  private static void ValidateColumns(
    PipelineSpec spec,
    Dictionary<string, DatasetColumn> columns,
    List<string> errors)
  {
    var features = spec.FeatureColumns;
    if (features.Count == 0)
    {
      errors.Add("feature_columns: choose at least one column");
    }

    foreach (var duplicate in features.GroupBy(f => f, StringComparer.Ordinal).Where(g => g.Count() > 1))
    {
      errors.Add($"feature_columns: '{duplicate.Key}' is listed more than once");
    }

    foreach (var feature in features.Distinct(StringComparer.Ordinal))
    {
      if (!columns.TryGetValue(feature, out var column))
      {
        errors.Add($"feature_columns: '{feature}' isn't a column in the dataset");
      }
      else if (column.InferredType != Numeric)
      {
        errors.Add($"feature_columns: '{feature}' is not numeric");
      }
    }

    if (string.IsNullOrEmpty(spec.TargetColumn))
    {
      errors.Add("target_column: is required");
    }
    else if (!columns.TryGetValue(spec.TargetColumn, out var target))
    {
      errors.Add($"target_column: '{spec.TargetColumn}' isn't a column in the dataset");
    }
    else
    {
      if (features.Contains(spec.TargetColumn))
      {
        errors.Add($"target_column: '{spec.TargetColumn}' is also a feature column");
      }

      if (spec.ProblemType == "regression" && target.InferredType != Numeric)
      {
        errors.Add($"target_column: '{spec.TargetColumn}' is not numeric, so it can't be a regression target");
      }
    }

    if (spec.IdColumn is { } idColumn)
    {
      if (!columns.ContainsKey(idColumn))
      {
        errors.Add($"id_column: '{idColumn}' isn't a column in the dataset");
      }
      else if (features.Contains(idColumn))
      {
        errors.Add($"id_column: '{idColumn}' is also a feature column");
      }
    }
  }

  private static void ValidateSplit(SplitSpec split, List<string> errors)
  {
    if (split.TestSize is < 0.05 or > 0.5)
    {
      errors.Add("split.test_size: must be between 0.05 and 0.5");
    }

    if (split.CvFolds is < 2 or > 10)
    {
      errors.Add("split.cv_folds: must be between 2 and 10, or null");
    }
  }

  /// <summary>Checks one step's id, problem type and params; returns its catalog entry if the id exists.</summary>
  private CatalogStep? ValidateStep(
    StepSpec step,
    string path,
    PipelineSpec spec,
    bool problemTypeValid,
    List<string> errors)
  {
    if (!catalog.Steps.TryGetValue(step.Step ?? "", out var definition))
    {
      errors.Add($"{path}.step: unknown step '{step.Step}'");
      return null;
    }

    if (problemTypeValid && !definition.ProblemTypes.Contains(spec.ProblemType))
    {
      errors.Add(
        $"{path}.step: '{step.Step}' doesn't support {spec.ProblemType}; it supports " +
        string.Join(" and ", definition.ProblemTypes));
    }

    errors.AddRange(SchemaErrors(definition, step.Params, $"{path}.params"));
    return definition;
  }

  private static void ValidateSearch(SearchSpec search, CatalogStep? model, List<string> errors)
  {
    if (!SearchMethods.Contains(search.Method))
    {
      errors.Add("search.method: must be 'grid' or 'random'");
    }

    if (search.CvFolds is < 2 or > 10)
    {
      errors.Add("search.cv_folds: must be between 2 and 10");
    }

    if (search.NIter is null && search.Method == "random")
    {
      errors.Add("search.n_iter: is required for a random search");
    }
    else if (search.NIter is < 1 or > 500)
    {
      errors.Add("search.n_iter: must be between 1 and 500");
    }

    // The grid can only be checked against a valid model.
    if (model is null)
    {
      return;
    }

    if (search.ParamGrid is null)
    {
      if (!model.HasSearchGrid)
      {
        errors.Add($"search.param_grid: '{model.Id}' has no default search grid; give a param_grid");
      }

      return;
    }

    foreach (var (key, values) in search.ParamGrid)
    {
      var path = $"search.param_grid.{key}";
      if (!model.ParamNames.Contains(key))
      {
        errors.Add($"{path}: '{model.Id}' has no parameter '{key}'");
        continue;
      }

      if (values is not JsonArray list || list.Count == 0)
      {
        errors.Add($"{path}: must be a list of at least one value");
        continue;
      }

      for (var i = 0; i < list.Count; i++)
      {
        var single = new JsonObject { [key] = list[i]?.DeepClone() };
        foreach (var error in SchemaErrors(model, single, "search.param_grid"))
        {
          // "search.param_grid.k: …" → "search.param_grid.k[1]: …", naming the bad candidate.
          errors.Add(error.Replace($"{path}:", $"{path}[{i}]:", StringComparison.Ordinal));
        }
      }
    }
  }

  /// <summary>Evaluates params against a step's schema and turns each failure into "path: message".</summary>
  private static IEnumerable<string> SchemaErrors(CatalogStep step, JsonObject parameters, string path)
  {
    var results = step.ParamsSchema.Evaluate(JsonSerializer.SerializeToElement(parameters), Evaluation);
    if (results.IsValid)
    {
      return [];
    }

    var messages = new List<string>();
    foreach (var detail in results.Details ?? [])
    {
      if (detail.Errors is null)
      {
        continue;
      }

      var location = PathOf(detail.InstanceLocation.ToString());
      foreach (var (keyword, message) in detail.Errors)
      {
        // additionalProperties: false fails each unknown property against the false schema.
        if (detail.EvaluationPath.ToString().EndsWith("/additionalProperties", StringComparison.Ordinal))
        {
          messages.Add($"{path}{location}: '{step.Id}' has no parameter by that name");
        }
        else if (SummaryKeywords.Contains(keyword))
        {
          continue;
        }
        else if (keyword == "type" && message.EndsWith("should be \"null\"", StringComparison.Ordinal))
        {
          // The null branch of an optional parameter's anyOf: the other branch's error is the useful one.
          continue;
        }
        else
        {
          messages.Add($"{path}{location}: {message}");
        }
      }
    }

    return messages.Count > 0 ? messages.Distinct() : [$"{path}: doesn't match the step's parameters"];
  }

  /// <summary>Turns a JSON Pointer like <c>/hidden_layer_sizes/0</c> into <c>.hidden_layer_sizes[0]</c>.</summary>
  private static string PathOf(string pointer) =>
    string.Concat(pointer.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(segment =>
      int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out _)
        ? $"[{segment}]"
        : $".{segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)}"));
}
