namespace HelixReborn.Api.Tests.Fixtures;

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Models.Eda;
using Services.Eda;

/// <summary>
/// <see cref="IEdaClient"/> for tests, which have no EDA service. Records every call and returns canned results;
/// <see cref="FailFor"/> makes calls for one experiment's dataset throw instead, so test classes sharing a factory
/// don't affect each other.
/// </summary>
public class FakeEdaClient : IEdaClient
{
  /// <summary>The summary every <see cref="GetSummaryAsync"/> call returns.</summary>
  public static readonly JsonElement Summary = JsonSerializer.Deserialize<JsonElement>(
    """{"n_rows": 2, "n_columns": 2, "columns": [{"name": "age", "dtype": "int64", "missing_count": 0}]}""");

  /// <summary>The rows every <see cref="CreateNormalityTableAsync"/> call returns.</summary>
  public static readonly JsonElement NormalityRows = JsonSerializer.Deserialize<JsonElement>(
    """[{"column": "age", "n": 2, "shapiro_statistic": 0.9, "shapiro_p": 0.5, "ks_statistic": 0.1, "ks_p": 0.9, "normal_at_0_05": true}]""");

  private readonly ConcurrentQueue<EdaCall> _calls = new();
  private readonly ConcurrentDictionary<Guid, EdaException> _failures = new();

  /// <summary>Every call made, oldest first.</summary>
  public IReadOnlyList<EdaCall> Calls => [.. _calls];

  /// <summary>Makes every call for this experiment's dataset throw <paramref name="exception"/>.</summary>
  /// <param name="experimentId">The experiment whose calls fail.</param>
  /// <param name="exception">The exception to throw.</param>
  public void FailFor(Guid experimentId, EdaException exception) => _failures[experimentId] = exception;

  /// <summary>The calls made for one experiment's dataset, oldest first.</summary>
  /// <param name="experimentId">The experiment.</param>
  /// <returns>The calls.</returns>
  public IReadOnlyList<EdaCall> CallsFor(Guid experimentId) =>
    [.. _calls.Where(x => x.DatasetKey.Contains($"/{experimentId:D}/", StringComparison.Ordinal))];

  /// <inheritdoc />
  public Task<JsonElement> GetSummaryAsync(string datasetKey, CancellationToken cancellationToken)
  {
    Record(new EdaCall("summary", datasetKey, null, null, null, null));
    return Task.FromResult(Summary);
  }

  /// <inheritdoc />
  public Task<EdaFileResult> CreatePlotAsync(
    EdaPlotType type,
    string datasetKey,
    string outputKey,
    JsonObject options,
    CancellationToken cancellationToken)
  {
    Record(new EdaCall("plot", datasetKey, outputKey, type, options.DeepClone().AsObject(), null));
    return Task.FromResult(new EdaFileResult(outputKey, "image/png", 1234, $"Fake {type}"));
  }

  /// <inheritdoc />
  public Task<EdaTableResult> CreateNormalityTableAsync(
    string datasetKey,
    string outputKey,
    IReadOnlyList<string>? columns,
    CancellationToken cancellationToken)
  {
    Record(new EdaCall("normality", datasetKey, outputKey, null, null, columns));
    return Task.FromResult(new EdaTableResult(outputKey, "text/csv", 321, "Normality tests", NormalityRows));
  }

  private void Record(EdaCall call)
  {
    _calls.Enqueue(call);
    var failure = _failures.FirstOrDefault(x => call.DatasetKey.Contains($"/{x.Key:D}/", StringComparison.Ordinal));
    if (failure.Value is not null)
    {
      throw failure.Value;
    }
  }

  /// <summary>One call to the fake.</summary>
  /// <param name="Operation"><c>summary</c>, <c>plot</c> or <c>normality</c>.</param>
  /// <param name="DatasetKey">The dataset key sent.</param>
  /// <param name="OutputKey">The output key sent, for plots and tables.</param>
  /// <param name="PlotType">The plot type, for plots.</param>
  /// <param name="Options">The plot options sent, for plots.</param>
  /// <param name="Columns">The columns sent, for normality tables.</param>
  public record EdaCall(
    string Operation,
    string DatasetKey,
    string? OutputKey,
    EdaPlotType? PlotType,
    JsonObject? Options,
    IReadOnlyList<string>? Columns);
}
