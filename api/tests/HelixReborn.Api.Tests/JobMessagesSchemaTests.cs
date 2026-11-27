namespace HelixReborn.Api.Tests;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Fixtures;
using Json.Schema;
using Messaging;
using Services.Evaluations;
using Services.Pipelines;
using Xunit;

/// <summary>
/// The C# job messages against helix-core's generated JSON Schemas (<c>contracts/messages/</c>), and against the
/// examples in <c>docs/architecture.md</c>, so a contract change fails here on whichever side lags.
/// </summary>
public partial class JobMessagesSchemaTests
{
  private static readonly string RepoRoot = FindRepoRoot();

  private static JsonSchema Schema(string name) => JsonSchema.Build(
    JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "contracts", "messages", $"{name}.schema.json"))).RootElement,
    new BuildOptions { SchemaRegistry = new SchemaRegistry() },
    new Uri($"https://helix-reborn.local/contracts/{name}-{Guid.NewGuid():N}"));

  private static void AssertValid(JsonSchema schema, string json)
  {
    var results = schema.Evaluate(JsonDocument.Parse(json).RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
    var errors = (results.Details ?? []).Where(d => d.Errors is not null)
      .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}"));
    Assert.True(results.IsValid, $"{json}\n{string.Join("\n", errors)}");
  }

  private static TrainingJob SampleTrainingJob()
  {
    var spec = PipelineSpecJson.Write(PipelineSpecJson.Read(RunSetup.Spec()));
    return new TrainingJob(
      Guid.NewGuid(),
      Guid.NewGuid(),
      Guid.NewGuid(),
      "experiments/e/datasets/d/data.csv",
      "experiments/e/runs/r/",
      JsonDocument.Parse(spec).RootElement.Clone());
  }

  [Fact]
  public void TrainingJob_MatchesTheContractSchema()
  {
    var json = JsonSerializer.Serialize(SampleTrainingJob(), JobMessageJson.Options);

    AssertValid(Schema("training-job"), json);
    var node = JsonNode.Parse(json)!.AsObject();
    Assert.Equal(1, (int)node["schema_version"]!);
    Assert.True(node.ContainsKey("output_prefix"));
  }

  [Fact]
  public void EvaluationJob_MatchesTheContractSchema()
  {
    var options = EvaluationOptionsValidator.Read(JsonNode.Parse("""
      {"local_methods": ["lime", "shap"], "ensemble_methods": ["mean"],
       "fuzzy": {"number_clusters": 3, "cluster_names": ["low", "medium", "high"]},
       "plot_options": {"dpi": 200}}
      """)!.AsObject());
    var job = new EvaluationJob(
      Guid.NewGuid(),
      Guid.NewGuid(),
      Guid.NewGuid(),
      Guid.NewGuid(),
      "experiments/e/datasets/d/data.csv",
      "experiments/e/runs/r/",
      "experiments/e/evaluations/v/",
      options);

    var json = JsonSerializer.Serialize(job, JobMessageJson.Options);

    AssertValid(Schema("evaluation-job"), json);
    var node = JsonNode.Parse(json)!.AsObject();
    Assert.Equal(1, (int)node["schema_version"]!);
    Assert.Equal(3, (int)node["options"]!["fuzzy"]!["number_clusters"]!);
  }

  [Fact]
  public void EvaluationJob_WithDefaultOptions_MatchesTheContractSchema()
  {
    var job = new EvaluationJob(
      Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "d.csv", "m/", "o/", new EvaluationJobOptions());

    AssertValid(Schema("evaluation-job"), JsonSerializer.Serialize(job, JobMessageJson.Options));
  }

  [Theory]
  [InlineData("started", null, null)]
  [InlineData("progress", null, null)]
  [InlineData("succeeded", """{"model_key": "k", "train_rows": 2}""", null)]
  [InlineData("failed", null, "InvalidDataError")]
  public void JobEvent_MatchesTheContractSchema(string status, string? result, string? errorType)
  {
    var jobEvent = new JobEvent(
      1,
      Guid.NewGuid(),
      "training",
      Guid.NewGuid(),
      status,
      status == "progress" ? 0.4 : null,
      status == "progress" ? "Split data" : null,
      result is null ? null : JsonDocument.Parse(result).RootElement.Clone(),
      errorType is null ? null : new JobError(errorType, "Too few rows"),
      DateTimeOffset.UtcNow);

    AssertValid(Schema("job-event"), JsonSerializer.Serialize(jobEvent, JobMessageJson.Options));
  }

  [Fact]
  public void JobEvent_DeserializesTheArchitectureExample()
  {
    var example = WithIds(ArchitectureExample("JobEvent"));

    var jobEvent = JsonSerializer.Deserialize<JobEvent>(example, JobMessageJson.Options)!;

    Assert.Equal("training", jobEvent.JobType);
    Assert.Equal("progress", jobEvent.Status);
    Assert.Equal(0.4, jobEvent.Progress);
    Assert.Equal("Cross-validating (fold 2 of 5)", jobEvent.Message);
    AssertValid(Schema("job-event"), example);
  }

  [Fact]
  public void TrainingJob_DeserializesTheArchitectureExample()
  {
    var example = WithIds(ArchitectureExample("TrainingJob"));

    var job = JsonSerializer.Deserialize<TrainingJob>(example, JobMessageJson.Options)!;

    Assert.Equal(1, job.SchemaVersion);
    Assert.Equal("experiments/{e}/runs/{r}/", job.OutputPrefix);
    Assert.Equal(JsonValueKind.Object, job.Pipeline.ValueKind);
  }

  /// <summary>The first <c>```json</c> block after the <c>### {heading}…</c> line in <c>docs/architecture.md</c>.</summary>
  private static string ArchitectureExample(string heading)
  {
    var doc = File.ReadAllText(Path.Combine(RepoRoot, "docs", "architecture.md"));
    var headingMatch = Regex.Match(doc, $@"^### {Regex.Escape(heading)}\b.*$", RegexOptions.Multiline);
    Assert.True(headingMatch.Success, $"No '### {heading}' in architecture.md");
    var match = JsonBlock().Match(doc, headingMatch.Index);
    Assert.True(match.Success, $"No json block after '### {heading}'");
    return match.Groups["json"].Value;
  }

  /// <summary>The examples elide IDs as <c>"…"</c>; give them real ones.</summary>
  private static string WithIds(string example) => Ellipsis().Replace(example, $"\"{Guid.NewGuid()}\"");

  private static string FindRepoRoot()
  {
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
      if (Directory.Exists(Path.Combine(dir.FullName, "contracts", "messages")))
      {
        return dir.FullName;
      }
    }

    throw new InvalidOperationException("Couldn't find the repo's contracts/messages directory.");
  }

  [GeneratedRegex(@"```json\n(?<json>.*?)\n```", RegexOptions.Singleline)]
  private static partial Regex JsonBlock();

  [GeneratedRegex("\"…[^\"]*\"")]
  private static partial Regex Ellipsis();
}
