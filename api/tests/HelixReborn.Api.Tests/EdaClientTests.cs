namespace HelixReborn.Api.Tests;

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Config;
using Duende.AccessTokenManagement;
using Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Models.Eda;
using Services.Eda;
using Xunit;

/// <summary>
/// <see cref="EdaClient"/> as <c>AddEda</c> registers it, with the token manager stubbed to a fixed token and the
/// network stubbed by <see cref="StubHandler"/>, so the real handler chain (token handler included) runs.
/// </summary>
public sealed class EdaClientTests : IDisposable
{
  private const string Token = "test-service-token";

  private readonly StubHandler _handler = new();
  private ServiceProvider? _provider;

  public void Dispose() => _provider?.Dispose();

  [Theory]
  [InlineData(EdaPlotType.TargetDistribution, "/plots/target-distribution")]
  [InlineData(EdaPlotType.CorrelationHeatmap, "/plots/correlation-heatmap")]
  [InlineData(EdaPlotType.Pairplot, "/plots/pairplot")]
  [InlineData(EdaPlotType.Pca, "/plots/pca")]
  [InlineData(EdaPlotType.Tsne, "/plots/tsne")]
  public async Task CreatePlotAsync_PostsToThePlotsPath_ForEachType(EdaPlotType type, string path)
  {
    _handler.Respond(HttpStatusCode.OK, FileResultJson);

    await Client().CreatePlotAsync(type, "d.csv", "o.png", [], CancellationToken.None);

    var request = Assert.Single(_handler.Requests);
    Assert.Equal(HttpMethod.Post, request.Method);
    Assert.Equal($"http://eda.test{path}", request.Uri);
  }

  [Fact]
  public async Task CreatePlotAsync_SendsSnakeCaseBody_WithKeysOverridingOptions()
  {
    _handler.Respond(HttpStatusCode.OK, FileResultJson);
    var options = new JsonObject
    {
      ["dataset_key"] = "someone-elses.csv",
      ["output_key"] = "elsewhere.png",
      ["method"] = "spearman",
      ["plot_options"] = new JsonObject { ["dpi"] = 100 },
    };

    var result = await Client().CreatePlotAsync(EdaPlotType.CorrelationHeatmap, "d.csv", "o.png", options, CancellationToken.None);

    var body = JsonNode.Parse(Assert.Single(_handler.Requests).Body)!.AsObject();
    Assert.Equal("d.csv", (string?)body["dataset_key"]);
    Assert.Equal("o.png", (string?)body["output_key"]);
    Assert.Equal("spearman", (string?)body["method"]);
    Assert.Equal(100, (int?)body["plot_options"]!["dpi"]);
    Assert.Equal("someone-elses.csv", (string?)options["dataset_key"]);
    Assert.Equal(new EdaFileResult("o.png", "image/png", 812, "PCA"), result);
  }

  [Fact]
  public async Task CreateNormalityTableAsync_SendsColumnsAndReadsRows()
  {
    _handler.Respond(
      HttpStatusCode.OK,
      """{"key": "o.csv", "content_type": "text/csv", "size_bytes": 50, "title": "Normality tests", "rows": [{"column": "age", "n": 3}]}""");

    var result = await Client().CreateNormalityTableAsync("d.csv", "o.csv", ["age"], CancellationToken.None);

    var request = Assert.Single(_handler.Requests);
    Assert.Equal("http://eda.test/tables/normality", request.Uri);
    Assert.Equal("""{"dataset_key":"d.csv","output_key":"o.csv","columns":["age"]}""", request.Body);
    Assert.Equal(("o.csv", "text/csv", 50L, "Normality tests"), (result.Key, result.ContentType, result.SizeBytes, result.Title));
    Assert.Equal("age", result.Rows[0].GetProperty("column").GetString());
  }

  [Fact]
  public async Task CreateNormalityTableAsync_SendsNullColumns_WhenNoneGiven()
  {
    _handler.Respond(
      HttpStatusCode.OK,
      """{"key": "o.csv", "content_type": "text/csv", "size_bytes": 50, "title": "Normality tests", "rows": []}""");

    await Client().CreateNormalityTableAsync("d.csv", "o.csv", null, CancellationToken.None);

    Assert.Equal("""{"dataset_key":"d.csv","output_key":"o.csv","columns":null}""", Assert.Single(_handler.Requests).Body);
  }

  [Fact]
  public async Task GetSummaryAsync_PostsDatasetKeyAndReturnsJson()
  {
    _handler.Respond(HttpStatusCode.OK, """{"n_rows": 2, "columns": []}""");

    var summary = await Client().GetSummaryAsync("d.csv", CancellationToken.None);

    var request = Assert.Single(_handler.Requests);
    Assert.Equal("http://eda.test/summary", request.Uri);
    Assert.Equal("""{"dataset_key":"d.csv"}""", request.Body);
    Assert.Equal(2, summary.GetProperty("n_rows").GetInt32());
  }

  [Fact]
  public async Task Requests_CarryTheServiceToken()
  {
    _handler.Respond(HttpStatusCode.OK, FileResultJson);

    await Client().CreatePlotAsync(EdaPlotType.Pca, "d.csv", "o.png", [], CancellationToken.None);

    Assert.Equal($"Bearer {Token}", Assert.Single(_handler.Requests).Authorization);
  }

  [Theory]
  [InlineData("""{"detail": "Non-numeric column(s): name"}""", "Non-numeric column(s): name")]
  [InlineData(
    """{"detail": [{"type": "less_than_equal", "loc": ["body", "perplexity"], "msg": "Input should be less than or equal to 100"}]}""",
    "perplexity: Input should be less than or equal to 100")]
  [InlineData("not json", "The EDA service rejected the request.")]
  public async Task CreatePlotAsync_ThrowsRejectedWithDetail_WhenEdaReturns422(string body, string detail)
  {
    _handler.Respond(HttpStatusCode.UnprocessableEntity, body);

    var ex = await Assert.ThrowsAsync<EdaException>(() =>
      Client().CreatePlotAsync(EdaPlotType.Pca, "d.csv", "o.png", [], CancellationToken.None));

    Assert.Equal(EdaException.Rejected, ex.StatusCode);
    Assert.Equal(detail, ex.Message);
  }

  [Fact]
  public async Task CreatePlotAsync_ThrowsDatasetNotFound_WhenEdaReturns404()
  {
    _handler.Respond(HttpStatusCode.NotFound, """{"detail": "No object at d.csv"}""");

    var ex = await Assert.ThrowsAsync<EdaException>(() =>
      Client().CreatePlotAsync(EdaPlotType.Pca, "d.csv", "o.png", [], CancellationToken.None));

    Assert.Equal(EdaException.DatasetNotFound, ex.StatusCode);
  }

  [Theory]
  [InlineData(HttpStatusCode.Unauthorized)]
  [InlineData(HttpStatusCode.InternalServerError)]
  [InlineData(HttpStatusCode.ServiceUnavailable)]
  public async Task CreatePlotAsync_ThrowsFailed_WhenEdaReturnsAnotherError(HttpStatusCode status)
  {
    _handler.Respond(status, """{"detail": "nope"}""");

    var ex = await Assert.ThrowsAsync<EdaException>(() =>
      Client().CreatePlotAsync(EdaPlotType.Pca, "d.csv", "o.png", [], CancellationToken.None));

    Assert.Equal(EdaException.Failed, ex.StatusCode);
  }

  [Fact]
  public async Task CreatePlotAsync_ThrowsFailed_WhenEdaIsUnreachable()
  {
    _handler.Throw(new HttpRequestException("Connection refused"));

    var ex = await Assert.ThrowsAsync<EdaException>(() =>
      Client().CreatePlotAsync(EdaPlotType.Pca, "d.csv", "o.png", [], CancellationToken.None));

    Assert.Equal(EdaException.Failed, ex.StatusCode);
  }

  [Fact]
  public async Task CreatePlotAsync_ThrowsFailed_WhenEdaSendsUnreadableJson()
  {
    _handler.Respond(HttpStatusCode.OK, "<html>");

    var ex = await Assert.ThrowsAsync<EdaException>(() =>
      Client().CreatePlotAsync(EdaPlotType.Pca, "d.csv", "o.png", [], CancellationToken.None));

    Assert.Equal(EdaException.Failed, ex.StatusCode);
  }

  [Fact]
  public async Task CreatePlotAsync_ThrowsFailedWithoutCallingEda_WhenSecretIsMissing()
  {
    var ex = await Assert.ThrowsAsync<EdaException>(() =>
      Client(secret: "").CreatePlotAsync(EdaPlotType.Pca, "d.csv", "o.png", [], CancellationToken.None));

    Assert.Equal(EdaException.Failed, ex.StatusCode);
    Assert.Contains("Keycloak:Secret", ex.Message);
    Assert.Empty(_handler.Requests);
  }

  private const string FileResultJson =
    """{"key": "o.png", "content_type": "image/png", "size_bytes": 812, "title": "PCA"}""";

  private IEdaClient Client(string secret = "test-secret")
  {
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
      ["Keycloak:AuthServerUrl"] = "http://keycloak.test",
      ["Keycloak:Realm"] = "helix",
      ["Keycloak:Resource"] = "helix-api",
      ["Keycloak:Secret"] = secret,
      ["Eda:BaseUrl"] = "http://eda.test",
    }).Build();

    var services = new ServiceCollection();
    services.AddSingleton<IConfiguration>(configuration);
    services.AddLogging();
    services.AddOptions<KeycloakOptions>().BindConfiguration(KeycloakOptions.SectionName);
    services.AddEda(configuration);

    services.RemoveAll<IClientCredentialsTokenManager>();
    services.AddSingleton<IClientCredentialsTokenManager>(new FixedTokenManager());
    services.AddHttpClient<IEdaClient, EdaClient>().ConfigurePrimaryHttpMessageHandler(() => _handler);

    _provider = services.BuildServiceProvider();
    return _provider.GetRequiredService<IEdaClient>();
  }

  /// <summary>Hands out <see cref="Token"/> for every client, as if Keycloak had issued it.</summary>
  private sealed class FixedTokenManager : IClientCredentialsTokenManager
  {
    public Task<TokenResult<ClientCredentialsToken>> GetAccessTokenAsync(
      ClientCredentialsClientName clientName,
      TokenRequestParameters? parameters = null,
      CancellationToken ct = default) =>
      Task.FromResult<TokenResult<ClientCredentialsToken>>(new ClientCredentialsToken
      {
        AccessToken = AccessToken.Parse(Token),
        AccessTokenType = AccessTokenType.Parse("Bearer"),
        ClientId = ClientId.Parse("helix-api"),
        Expiration = DateTimeOffset.UtcNow.AddMinutes(5),
        DPoPJsonWebKey = null,
        Scope = null,
      });

    public Task DeleteAccessTokenAsync(
      ClientCredentialsClientName clientName,
      TokenRequestParameters? parameters = null,
      CancellationToken ct = default) =>
      Task.CompletedTask;
  }

  /// <summary>Records each request and answers with a canned response or exception.</summary>
  private sealed class StubHandler : HttpMessageHandler
  {
    private readonly List<RecordedRequest> _requests = [];
    private Func<HttpResponseMessage> _respond = () => new HttpResponseMessage(HttpStatusCode.OK);

    public IReadOnlyList<RecordedRequest> Requests => _requests;

    public void Respond(HttpStatusCode status, string body) =>
      _respond = () => new HttpResponseMessage(status)
      {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
      };

    public void Throw(Exception exception) => _respond = () => throw exception;

    protected override async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken cancellationToken)
    {
      var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
      _requests.Add(new RecordedRequest(
        request.Method,
        request.RequestUri!.ToString(),
        request.Headers.Authorization?.ToString(),
        body));
      return _respond();
    }
  }

  private sealed record RecordedRequest(HttpMethod Method, string Uri, string? Authorization, string Body);
}
