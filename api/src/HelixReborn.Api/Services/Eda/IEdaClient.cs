namespace HelixReborn.Api.Services.Eda;

using System.Text.Json;
using System.Text.Json.Nodes;
using Models.Eda;

/// <summary>
/// Calls the EDA service (<c>services/eda</c>) server-to-server, as the <c>helix-api</c> service account. Doesn't
/// check ownership: callers check the experiment is visible to the caller first.
/// </summary>
public interface IEdaClient
{
  /// <summary>Summarises a dataset: <c>POST /summary</c>.</summary>
  /// <param name="datasetKey">The dataset's S3 key.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>EDA's summary JSON, as-is.</returns>
  /// <exception cref="EdaException">The call failed.</exception>
  Task<JsonElement> GetSummaryAsync(string datasetKey, CancellationToken cancellationToken);

  /// <summary>Renders a plot to S3: <c>POST /plots/{type}</c>.</summary>
  /// <param name="type">Which plot.</param>
  /// <param name="datasetKey">The dataset's S3 key.</param>
  /// <param name="outputKey">The S3 key EDA writes the PNG to.</param>
  /// <param name="options">
  /// The plot's other fields, snake_case, including <c>plot_options</c>. Any <c>dataset_key</c> or
  /// <c>output_key</c> in it is overwritten. Not modified.
  /// </param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The written file.</returns>
  /// <exception cref="EdaException">The call failed.</exception>
  Task<EdaFileResult> CreatePlotAsync(
    EdaPlotType type,
    string datasetKey,
    string outputKey,
    JsonObject options,
    CancellationToken cancellationToken);

  /// <summary>Runs normality tests and writes them to S3 as a CSV: <c>POST /tables/normality</c>.</summary>
  /// <param name="datasetKey">The dataset's S3 key.</param>
  /// <param name="outputKey">The S3 key EDA writes the CSV to.</param>
  /// <param name="columns">The columns to test, or null for every numeric column.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The written file and the table's rows.</returns>
  /// <exception cref="EdaException">The call failed.</exception>
  Task<EdaTableResult> CreateNormalityTableAsync(
    string datasetKey,
    string outputKey,
    IReadOnlyList<string>? columns,
    CancellationToken cancellationToken);
}

/// <summary>A file EDA wrote to S3.</summary>
/// <param name="Key">The S3 key it was written to.</param>
/// <param name="ContentType">Its content type, e.g. <c>image/png</c>.</param>
/// <param name="SizeBytes">Its size, in bytes.</param>
/// <param name="Title">A human readable title, e.g. "Correlation heatmap (pearson)".</param>
public record EdaFileResult(string Key, string ContentType, long SizeBytes, string Title);

/// <summary>A table EDA wrote to S3 as a CSV, with its rows.</summary>
/// <param name="Key">The S3 key it was written to.</param>
/// <param name="ContentType">Its content type, <c>text/csv</c>.</param>
/// <param name="SizeBytes">Its size, in bytes.</param>
/// <param name="Title">A human readable title, e.g. "Normality tests".</param>
/// <param name="Rows">The table's rows as EDA returned them (a JSON array of snake_case objects).</param>
public record EdaTableResult(string Key, string ContentType, long SizeBytes, string Title, JsonElement Rows);
