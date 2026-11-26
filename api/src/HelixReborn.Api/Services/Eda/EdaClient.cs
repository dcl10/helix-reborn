namespace HelixReborn.Api.Services.Eda;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Config;
using Microsoft.Extensions.Options;
using Models.Eda;

/// <summary>
/// <see cref="IEdaClient"/> over a typed <see cref="HttpClient"/> whose base address is <c>Eda:BaseUrl</c> and whose
/// handler chain adds a <c>helix-api</c> client-credentials token (registered by <c>AddEda</c>). Bodies are
/// snake_case, as EDA expects.
/// </summary>
/// <param name="http">The typed HTTP client.</param>
/// <param name="keycloak">Keycloak options, to report a missing <c>Keycloak:Secret</c> clearly.</param>
/// <param name="logger">Logs failed calls.</param>
public partial class EdaClient(HttpClient http, IOptions<KeycloakOptions> keycloak, ILogger<EdaClient> logger) : IEdaClient
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
  {
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
  };

  /// <inheritdoc />
  public Task<JsonElement> GetSummaryAsync(string datasetKey, CancellationToken cancellationToken) =>
    PostAsync<JsonElement>("/summary", new JsonObject { ["dataset_key"] = datasetKey }, cancellationToken);

  /// <inheritdoc />
  public Task<EdaFileResult> CreatePlotAsync(
    EdaPlotType type,
    string datasetKey,
    string outputKey,
    JsonObject options,
    CancellationToken cancellationToken)
  {
    var body = options.DeepClone().AsObject();
    body["dataset_key"] = datasetKey;
    body["output_key"] = outputKey;

    return PostAsync<EdaFileResult>(PathOf(type), body, cancellationToken);
  }

  /// <inheritdoc />
  public Task<EdaTableResult> CreateNormalityTableAsync(
    string datasetKey,
    string outputKey,
    IReadOnlyList<string>? columns,
    CancellationToken cancellationToken)
  {
    var body = new JsonObject
    {
      ["dataset_key"] = datasetKey,
      ["output_key"] = outputKey,
      ["columns"] = columns is null ? null : new JsonArray([.. columns.Select(c => JsonValue.Create(c))]),
    };

    return PostAsync<EdaTableResult>("/tables/normality", body, cancellationToken);
  }

  /// <summary>The EDA route for a plot type.</summary>
  /// <param name="type">The plot type.</param>
  /// <returns>The route, e.g. <c>/plots/correlation-heatmap</c>.</returns>
  public static string PathOf(EdaPlotType type) => type switch
  {
    EdaPlotType.TargetDistribution => "/plots/target-distribution",
    EdaPlotType.CorrelationHeatmap => "/plots/correlation-heatmap",
    EdaPlotType.Pairplot => "/plots/pairplot",
    EdaPlotType.Pca => "/plots/pca",
    EdaPlotType.Tsne => "/plots/tsne",
    _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown EDA plot type."),
  };

  /// <summary>
  /// POSTs a JSON body and reads the JSON response, turning every failure into an <see cref="EdaException"/>.
  /// </summary>
  private async Task<T> PostAsync<T>(string path, JsonObject body, CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(keycloak.Value.Secret))
    {
      throw new EdaException(
        EdaException.Failed,
        "Keycloak:Secret isn't configured, so the API can't get a token to call the EDA service. " +
        "See docs/auth-setup.md.");
    }

    HttpResponseMessage response;
    try
    {
      response = await http.PostAsJsonAsync(path, body, Json, cancellationToken);
    }
    catch (HttpRequestException ex)
    {
      LogUnreachable(logger, ex, path);
      throw new EdaException(EdaException.Failed, "Couldn't reach the EDA service.", ex);
    }
    catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
    {
      LogTimedOut(logger, ex, path);
      throw new EdaException(
        EdaException.Failed,
        $"The EDA service didn't respond within {http.Timeout.TotalSeconds:0} seconds.",
        ex);
    }

    using (response)
    {
      if (response.IsSuccessStatusCode)
      {
        try
        {
          return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken)
            ?? throw new JsonException("The response was empty.");
        }
        catch (JsonException ex)
        {
          LogUnreadable(logger, ex, path);
          throw new EdaException(EdaException.Failed, "The EDA service sent a response the API couldn't read.", ex);
        }
      }

      var text = await response.Content.ReadAsStringAsync(cancellationToken);
      switch (response.StatusCode)
      {
        case HttpStatusCode.UnprocessableEntity:
          throw new EdaException(EdaException.Rejected, DetailOf(text) ?? "The EDA service rejected the request.");
        case HttpStatusCode.NotFound:
          throw new EdaException(EdaException.DatasetNotFound, DetailOf(text) ?? "The EDA service couldn't find the dataset.");
        default:
          LogFailed(logger, (int)response.StatusCode, path, text.Length > 500 ? text[..500] : text);
          throw new EdaException(EdaException.Failed, $"The EDA service returned {(int)response.StatusCode}.");
      }
    }
  }

  /// <summary>
  /// Reads FastAPI's error <c>detail</c>: a string for EDA's own errors, or a list of
  /// <c>{"loc": [...], "msg": "..."}</c> for request validation errors, which becomes
  /// <c>"perplexity: Input should be ..."</c> lines joined with <c>"; "</c>.
  /// </summary>
  /// <param name="body">The error response body.</param>
  /// <returns>The detail, or null if the body has none.</returns>
  private static string? DetailOf(string body)
  {
    try
    {
      using var doc = JsonDocument.Parse(body);
      if (doc.RootElement.ValueKind != JsonValueKind.Object ||
          !doc.RootElement.TryGetProperty("detail", out var detail))
      {
        return null;
      }

      if (detail.ValueKind == JsonValueKind.String)
      {
        return detail.GetString();
      }

      if (detail.ValueKind != JsonValueKind.Array)
      {
        return null;
      }

      var messages = detail.EnumerateArray()
        .Where(e => e.ValueKind == JsonValueKind.Object)
        .Select(e =>
        {
          var msg = e.TryGetProperty("msg", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
          var loc = e.TryGetProperty("loc", out var l) && l.ValueKind == JsonValueKind.Array
            ? string.Join(".", l.EnumerateArray().Select(x => x.ToString()).Where(x => x != "body"))
            : "";
          return msg is null ? null : loc.Length > 0 ? $"{loc}: {msg}" : msg;
        })
        .OfType<string>()
        .ToList();

      return messages.Count > 0 ? string.Join("; ", messages) : null;
    }
    catch (JsonException)
    {
      return null;
    }
  }

  [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't reach the EDA service for {Path}")]
  private static partial void LogUnreachable(ILogger logger, Exception exception, string path);

  [LoggerMessage(Level = LogLevel.Warning, Message = "The EDA service timed out on {Path}")]
  private static partial void LogTimedOut(ILogger logger, Exception exception, string path);

  [LoggerMessage(Level = LogLevel.Warning, Message = "The EDA service sent an unreadable response for {Path}")]
  private static partial void LogUnreadable(ILogger logger, Exception exception, string path);

  [LoggerMessage(Level = LogLevel.Warning, Message = "The EDA service returned {StatusCode} for {Path}: {Body}")]
  private static partial void LogFailed(ILogger logger, int statusCode, string path, string body);
}
