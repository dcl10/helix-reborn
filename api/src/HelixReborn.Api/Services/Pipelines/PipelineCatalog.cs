namespace HelixReborn.Api.Services.Pipelines;

using System.Text.Json;
using System.Text.Json.Nodes;
using Config;
using Contracts;
using Json.Schema;
using Microsoft.Extensions.Options;

/// <summary>
/// <see cref="IPipelineCatalog"/> read from <c>pipeline-catalog.json</c> in <see cref="ContractsOptions.Root"/>. The
/// file is read and every step's <c>params_schema</c> compiled in the constructor, so a missing or invalid catalog
/// fails as soon as the singleton is first resolved, which <see cref="PipelineCatalogStartupCheck"/> does at startup.
/// </summary>
public class PipelineCatalog : IPipelineCatalog
{
  /// <summary>The catalog's file name inside the contracts directory.</summary>
  public const string FileName = "pipeline-catalog.json";

  private static readonly string[] Kinds = ["preprocessor", "feature_selector", CatalogStep.ModelKind];

  /// <summary>Loads and compiles the catalog.</summary>
  /// <param name="options">Where the contracts directory is.</param>
  /// <param name="environment">The host environment, whose content root a relative path is resolved from.</param>
  /// <exception cref="InvalidOperationException">The file is missing or isn't a valid catalog.</exception>
  public PipelineCatalog(IOptions<ContractsOptions> options, IHostEnvironment environment)
  {
    Path = ResolvePath(options.Value.Root, environment.ContentRootPath);
    RawJson = File.ReadAllText(Path);
    Steps = Parse(RawJson, Path);
  }

  /// <summary>The catalog file that was loaded.</summary>
  public string Path { get; }

  /// <inheritdoc />
  public string RawJson { get; }

  /// <inheritdoc />
  public IReadOnlyDictionary<string, CatalogStep> Steps { get; }

  /// <summary>
  /// Finds the catalog file: <paramref name="root"/> itself when absolute, otherwise the first
  /// <c>{dir}/{root}/pipeline-catalog.json</c> walking up from the content root, then from the app's base directory.
  /// </summary>
  /// <param name="root">The configured contracts directory.</param>
  /// <param name="contentRoot">The app's content root.</param>
  /// <returns>The catalog file's full path.</returns>
  /// <exception cref="InvalidOperationException">No catalog file was found.</exception>
  public static string ResolvePath(string root, string contentRoot) =>
    ContractFiles.Resolve(root, contentRoot, FileName, "pipeline catalog");

  private static Dictionary<string, CatalogStep> Parse(string json, string path)
  {
    try
    {
      var root = JsonNode.Parse(json)?.AsObject() ?? throw new JsonException("The file is empty.");
      if (root["schema_version"]?.GetValue<int>() != 1)
      {
        throw new JsonException("schema_version must be 1.");
      }

      var steps = new Dictionary<string, CatalogStep>(StringComparer.Ordinal);

      // A registry of this catalog's own: the global one would refuse a second catalog's schemas at the same URIs.
      var buildOptions = new BuildOptions { SchemaRegistry = new SchemaRegistry() };
      foreach (var node in root["steps"]?.AsArray() ?? throw new JsonException("steps is missing."))
      {
        var step = node?.AsObject() ?? throw new JsonException("A step is null.");
        var id = Required(step, "id");
        var kind = Required(step, "kind");
        if (!Kinds.Contains(kind))
        {
          throw new JsonException($"Step '{id}' has an unknown kind '{kind}'.");
        }

        var paramsSchema = step["params_schema"]?.AsObject()
          ?? throw new JsonException($"Step '{id}' has no params_schema.");
        var schemaElement = JsonSerializer.SerializeToElement(paramsSchema);
        var schema = JsonSchema.Build(
          schemaElement,
          buildOptions,
          new Uri($"https://helix-reborn.local/pipeline-catalog/{Uri.EscapeDataString(id)}"));
        var paramNames = paramsSchema["properties"]?.AsObject().Select(p => p.Key).ToHashSet(StringComparer.Ordinal)
          ?? [];
        var problemTypes = step["problem_types"]?.AsArray().Select(p => p!.GetValue<string>()).ToList()
          ?? throw new JsonException($"Step '{id}' has no problem_types.");
        var hasSearchGrid = step["search_grid"] is JsonObject;

        if (!steps.TryAdd(id, new CatalogStep(id, kind, problemTypes, schema, paramNames, hasSearchGrid)))
        {
          throw new JsonException($"Step '{id}' appears twice.");
        }
      }

      return steps;
    }
    catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
    {
      throw new InvalidOperationException($"The pipeline catalog {path} is invalid: {ex.Message}", ex);
    }
  }

  private static string Required(JsonObject step, string key) =>
    step[key]?.GetValue<string>() is { Length: > 0 } value
      ? value
      : throw new JsonException($"A step has no {key}.");
}
