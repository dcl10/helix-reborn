namespace HelixReborn.Api.Tests.Fixtures;

using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Models;
using Services.Storage;

/// <summary>
/// Builds what a prediction needs: a succeeded run whose model files are in the fake object store, copied from
/// <c>Fixtures/models/{classification,regression}</c> (made by <c>make_test_models.py</c>).
/// </summary>
public static class PredictionSetup
{
  private static readonly JsonSerializerOptions SnakeCase =
    new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

  /// <summary>The classification fixture: standard_scaler + logistic_regression, classes no and yes.</summary>
  public const string Classification = "classification";

  /// <summary>The regression fixture: simple_imputer (median) + ridge.</summary>
  public const string Regression = "regression";

  /// <summary>Creates a succeeded run and stores a fixture model under its prefix.</summary>
  /// <param name="factory">The test host.</param>
  /// <param name="client">The owner's client.</param>
  /// <param name="fixture"><see cref="Classification"/> or <see cref="Regression"/>.</param>
  /// <returns>The experiment and the succeeded run.</returns>
  public static async Task<(ExperimentResponse Experiment, RunResponse Run)> RunWithModelAsync(
    ApiFactory factory,
    HttpClient client,
    string fixture)
  {
    var (experiment, _, run) = await EvaluationSetup.SucceededRunAsync(factory, client);
    await StoreModelAsync(factory.ObjectStorage, experiment.Id, run.Id, fixture);
    return (experiment, run);
  }

  /// <summary>Stores a fixture's <c>model.onnx</c> and <c>metadata.json</c> under a run's prefix.</summary>
  /// <param name="storage">The object store.</param>
  /// <param name="experimentId">The experiment.</param>
  /// <param name="runId">The run.</param>
  /// <param name="fixture"><see cref="Classification"/> or <see cref="Regression"/>.</param>
  /// <returns>A task that completes once both files are stored.</returns>
  public static async Task StoreModelAsync(IObjectStorage storage, Guid experimentId, Guid runId, string fixture)
  {
    var prefix = StorageKeys.RunPrefix(experimentId, runId);
    foreach (var file in new[] { "model.onnx", "metadata.json" })
    {
      await using var content = File.OpenRead(FixturePath(fixture, file));
      await storage.PutAsync(prefix + file, content, "application/octet-stream");
    }
  }

  /// <summary>Reads a fixture file's bytes.</summary>
  /// <param name="fixture"><see cref="Classification"/> or <see cref="Regression"/>.</param>
  /// <param name="file">The file name.</param>
  /// <returns>The content.</returns>
  public static byte[] ReadFixture(string fixture, string file) => File.ReadAllBytes(FixturePath(fixture, file));

  /// <summary>Reads a fixture's <c>expected.json</c>: the ONNX model's own predictions for 3 rows.</summary>
  /// <param name="fixture"><see cref="Classification"/> or <see cref="Regression"/>.</param>
  /// <returns>The expected rows and predictions.</returns>
  public static Expected ReadExpected(string fixture) =>
    JsonSerializer.Deserialize<Expected>(
      File.ReadAllText(FixturePath(fixture, "expected.json")), SnakeCase)!;

  /// <summary>Formats a feature value for a CSV cell: blank for null, otherwise round-trip invariant.</summary>
  /// <param name="value">The value.</param>
  /// <returns>The cell text.</returns>
  public static string Cell(double? value) =>
    value is { } v ? ((float)v).ToString("R", CultureInfo.InvariantCulture) : "";

  /// <summary>Posts a CSV to the predictions endpoint.</summary>
  /// <param name="client">The caller's client.</param>
  /// <param name="experimentId">The experiment.</param>
  /// <param name="runId">The run.</param>
  /// <param name="csv">The CSV text.</param>
  /// <param name="fileName">The upload's file name.</param>
  /// <param name="contentType">The upload's content type.</param>
  /// <returns>The response.</returns>
  public static Task<HttpResponseMessage> PostAsync(
    HttpClient client,
    Guid experimentId,
    Guid runId,
    string csv,
    string fileName = "new.csv",
    string contentType = "text/csv") =>
    PostBytesAsync(client, experimentId, runId, Encoding.UTF8.GetBytes(csv), fileName, contentType);

  /// <summary>Posts raw bytes as the upload.</summary>
  /// <param name="client">The caller's client.</param>
  /// <param name="experimentId">The experiment.</param>
  /// <param name="runId">The run.</param>
  /// <param name="bytes">The file content.</param>
  /// <param name="fileName">The upload's file name.</param>
  /// <param name="contentType">The upload's content type.</param>
  /// <returns>The response.</returns>
  public static async Task<HttpResponseMessage> PostBytesAsync(
    HttpClient client,
    Guid experimentId,
    Guid runId,
    byte[] bytes,
    string fileName = "new.csv",
    string contentType = "text/csv")
  {
    using var content = new MultipartFormDataContent();
    var file = new ByteArrayContent(bytes);
    file.Headers.ContentType = new(contentType);
    content.Add(file, "file", fileName);
    return await client.PostAsync($"/api/experiments/{experimentId}/runs/{runId}/predictions", content);
  }

  /// <summary>Posts a CSV and returns the prediction, failing on an error status.</summary>
  /// <param name="client">The owner's client.</param>
  /// <param name="experimentId">The experiment.</param>
  /// <param name="runId">The run.</param>
  /// <param name="csv">The CSV text.</param>
  /// <returns>The prediction.</returns>
  public static async Task<PredictionResponse> PredictAsync(
    HttpClient client,
    Guid experimentId,
    Guid runId,
    string csv)
  {
    var response = await PostAsync(client, experimentId, runId, csv);
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<PredictionResponse>())!;
  }

  private static string FixturePath(string fixture, string file) =>
    Path.Combine(AppContext.BaseDirectory, "Fixtures", "models", fixture, file);

  /// <summary>A fixture's <c>expected.json</c>.</summary>
  /// <param name="Ids">The rows' IDs (classification only).</param>
  /// <param name="Rows">The feature rows, f0, f1, f2; null is a blank.</param>
  /// <param name="Labels">The predicted class labels (classification).</param>
  /// <param name="Probabilities">The class probabilities, in classes order (classification).</param>
  /// <param name="Values">The predicted values (regression).</param>
  public record Expected(
    List<string>? Ids,
    List<List<double?>> Rows,
    List<string>? Labels,
    List<List<double>>? Probabilities,
    List<double>? Values);
}
