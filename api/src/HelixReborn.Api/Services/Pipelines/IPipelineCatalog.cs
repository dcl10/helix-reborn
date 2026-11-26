namespace HelixReborn.Api.Services.Pipelines;

using Json.Schema;

/// <summary>
/// The pipeline step catalog, <c>contracts/pipeline-catalog.json</c>, generated from helix-core's step registry (#22).
/// Loaded once at startup. It's the source of truth for which steps exist: nothing in C# names a step.
/// </summary>
public interface IPipelineCatalog
{
  /// <summary>The catalog file's text, exactly as on disk.</summary>
  string RawJson { get; }

  /// <summary>Every step, by id.</summary>
  IReadOnlyDictionary<string, CatalogStep> Steps { get; }
}

/// <summary>One step in the catalog.</summary>
/// <param name="Id">The step id used in a spec, e.g. <c>random_forest_regressor</c>.</param>
/// <param name="Kind"><c>preprocessor</c>, <c>feature_selector</c> or <c>model</c>.</param>
/// <param name="ProblemTypes">The problem types it supports: <c>classification</c> and/or <c>regression</c>.</param>
/// <param name="ParamsSchema">Its <c>params_schema</c>, compiled. Every parameter has a default, so <c>{}</c> is valid.</param>
/// <param name="ParamNames">The parameter names: the schema's <c>properties</c>.</param>
/// <param name="HasSearchGrid">Whether it has a default <c>search_grid</c> (models only).</param>
public record CatalogStep(
  string Id,
  string Kind,
  IReadOnlyList<string> ProblemTypes,
  JsonSchema ParamsSchema,
  IReadOnlySet<string> ParamNames,
  bool HasSearchGrid)
{
  /// <summary>The <see cref="Kind"/> of a model step.</summary>
  public const string ModelKind = "model";
}
