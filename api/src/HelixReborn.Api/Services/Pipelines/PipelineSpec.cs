namespace HelixReborn.Api.Services.Pipelines;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

/// <summary>
/// A pipeline: target, features, split, preprocessing steps, model and optional hyperparameter search. Mirrors
/// helix-core's <c>PipelineSpec</c> (<c>contracts/messages/pipeline-spec.schema.json</c>) and is snake_case on the
/// wire: (de)serialize it with <see cref="PipelineSpecJson.Options"/>, never the API's camelCase defaults.
/// </summary>
/// <param name="ProblemType"><c>classification</c> or <c>regression</c>.</param>
/// <param name="TargetColumn">The column to predict.</param>
/// <param name="FeatureColumns">The numeric columns to predict it from.</param>
/// <param name="IdColumn">An optional identifier column, kept out of the features.</param>
/// <param name="Split">How the dataset is split into training and test rows.</param>
/// <param name="Steps">Preprocessors and feature selectors, applied in order.</param>
/// <param name="Model">The one model step.</param>
/// <param name="Search">Optional hyperparameter search on the training rows.</param>
public record PipelineSpec(
  string ProblemType,
  string TargetColumn,
  IReadOnlyList<string> FeatureColumns,
  string? IdColumn,
  SplitSpec Split,
  IReadOnlyList<StepSpec> Steps,
  StepSpec Model,
  SearchSpec? Search);

/// <summary>One step: a catalog id and its parameters.</summary>
/// <param name="Step">The catalog step id.</param>
/// <param name="Params">Its parameters; validated against the step's <c>params_schema</c>.</param>
public record StepSpec(string Step, JsonObject Params);

/// <summary>How the dataset is split.</summary>
/// <param name="TestSize">The share of rows held out for testing, 0.05–0.5.</param>
/// <param name="RandomSeed">The seed for the split and every seeded estimator.</param>
/// <param name="CvFolds">Cross-validation folds on the training rows, 2–10, or null for none.</param>
public record SplitSpec(double TestSize = 0.2, int RandomSeed = 42, int? CvFolds = null);

/// <summary>Hyperparameter search on the training rows.</summary>
/// <param name="Method"><c>grid</c> or <c>random</c>.</param>
/// <param name="CvFolds">Folds for the search, 2–10.</param>
/// <param name="NIter">Candidates to try, 1–500; required for <c>random</c>.</param>
/// <param name="ParamGrid">Model parameter → candidate values, or null for the model's default grid.</param>
public record SearchSpec(string Method, int CvFolds = 5, int? NIter = null, JsonObject? ParamGrid = null);

/// <summary>Serialization of <see cref="PipelineSpec"/>, matching the Python contract.</summary>
public static class PipelineSpecJson
{
  /// <summary>
  /// snake_case names, and unknown properties rejected: helix-core's models are <c>extra="forbid"</c>, so a spec with
  /// an extra field would fail when the training worker parses the job.
  /// </summary>
  public static readonly JsonSerializerOptions Options = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
  };

  /// <summary>
  /// Reads a spec from its JSON, filling in the defaults the Python model has for <c>split</c>, <c>steps</c> and
  /// <c>params</c> so the stored spec states them.
  /// </summary>
  /// <param name="json">The spec as sent.</param>
  /// <returns>The spec.</returns>
  /// <exception cref="JsonException">The JSON doesn't have the spec's shape.</exception>
  public static PipelineSpec Read(JsonObject json)
  {
    var spec = json.Deserialize<PipelineSpec>(Options) ?? throw new JsonException("The spec is null.");
    return spec with
    {
      FeatureColumns = spec.FeatureColumns ?? [],
      Split = spec.Split ?? new SplitSpec(),
      Steps = [.. (spec.Steps ?? []).Select(WithParams)],
      Model = spec.Model is null ? null! : WithParams(spec.Model),
    };
  }

  /// <summary>Writes a spec as its snake_case JSON.</summary>
  /// <param name="spec">The spec.</param>
  /// <returns>The JSON text.</returns>
  public static string Write(PipelineSpec spec) => JsonSerializer.Serialize(spec, Options);

  private static StepSpec WithParams(StepSpec step) => step with { Params = step.Params ?? [] };
}
