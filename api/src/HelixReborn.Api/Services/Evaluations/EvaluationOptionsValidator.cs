namespace HelixReborn.Api.Services.Evaluations;

using System.Text.Json;
using System.Text.Json.Nodes;
using Config;
using Contracts;
using Json.Schema;
using Messaging;
using Microsoft.Extensions.Options;

/// <summary>
/// Validates evaluation options against the <c>EvaluationOptions</c> definition in
/// <c>contracts/messages/evaluation-job.schema.json</c> (generated from helix-core), then applies the cross-field rules
/// the schema can't express: fuzzy needs a local and an ensemble method, <c>cluster_names</c> needs one name per
/// cluster, and ensembles need a global method. The schema is read and compiled once, in the constructor.
/// </summary>
public class EvaluationOptionsValidator
{
  /// <summary>The evaluation job schema's path inside the contracts directory.</summary>
  public const string SchemaPath = "messages/evaluation-job.schema.json";

  private readonly JsonSchema _schema;

  /// <summary>Loads and compiles the options schema.</summary>
  /// <param name="options">Where the contracts directory is.</param>
  /// <param name="environment">The host environment, whose content root a relative path is resolved from.</param>
  /// <exception cref="InvalidOperationException">The schema file is missing.</exception>
  public EvaluationOptionsValidator(IOptions<ContractsOptions> options, IHostEnvironment environment)
  {
    var path = ContractFiles.Resolve(
      options.Value.Root, environment.ContentRootPath, SchemaPath, "evaluation job schema");
    var jobSchema = JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    // Just the options: the job schema's definitions, rooted at EvaluationOptions.
    var optionsSchema = new JsonObject
    {
      ["$defs"] = jobSchema["$defs"]!.DeepClone(),
      ["$ref"] = "#/$defs/EvaluationOptions",
    };
    _schema = JsonSchema.Build(
      JsonSerializer.SerializeToElement(optionsSchema),
      new BuildOptions { SchemaRegistry = new SchemaRegistry() },
      new Uri("https://helix-reborn.local/contracts/evaluation-options"));
  }

  /// <summary>Checks options against the schema and the cross-field rules.</summary>
  /// <param name="options">The snake_case options as sent; missing fields take the defaults.</param>
  /// <returns>
  /// What's wrong, as <c>"field: problem"</c> messages; empty when the options are valid. Schema errors are returned
  /// alone, since the cross-field rules can't be checked on options that don't parse.
  /// </returns>
  public IReadOnlyList<string> Validate(JsonObject options)
  {
    var results = _schema.Evaluate(
      JsonSerializer.SerializeToElement(options),
      new Json.Schema.EvaluationOptions { OutputFormat = OutputFormat.List });
    if (!results.IsValid)
    {
      var schemaErrors = (results.Details ?? [])
        .Where(d => d.Errors is not null)
        .SelectMany(d => d.Errors!.Select(e => $"{Field(d.InstanceLocation.ToString())}: {e.Value}"))
        .Distinct(StringComparer.Ordinal)
        .ToList();
      return schemaErrors.Count > 0 ? schemaErrors : ["options: invalid"];
    }

    var parsed = Read(options);
    var errors = new List<string>();
    if (parsed.Fuzzy is { } fuzzy)
    {
      if (parsed.LocalMethods.Count == 0 || parsed.EnsembleMethods.Count == 0)
      {
        errors.Add("fuzzy: needs at least one local method and at least one ensemble method");
      }

      if (fuzzy.ClusterNames.Count != fuzzy.NumberClusters)
      {
        errors.Add(
          $"fuzzy.cluster_names: has {fuzzy.ClusterNames.Count} names but number_clusters is {fuzzy.NumberClusters}");
      }
    }

    if (parsed.EnsembleMethods.Count > 0 && parsed.GlobalMethods.Count == 0)
    {
      errors.Add("ensemble_methods: needs at least one global method");
    }

    return errors;
  }

  /// <summary>Reads options into their typed form, filling in defaults. Call after <see cref="Validate"/>.</summary>
  /// <param name="options">The snake_case options.</param>
  /// <returns>The options.</returns>
  public static EvaluationJobOptions Read(JsonObject options) =>
    options.Deserialize<EvaluationJobOptions>(JobMessageJson.Options) ?? new EvaluationJobOptions();

  /// <summary>A JSON pointer as a dotted field name: <c>/fuzzy/number_clusters</c> → <c>fuzzy.number_clusters</c>.</summary>
  private static string Field(string pointer) =>
    pointer.Trim('/') is { Length: > 0 } trimmed ? trimmed.Replace('/', '.') : "options";
}
