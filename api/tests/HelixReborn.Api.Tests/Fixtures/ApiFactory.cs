namespace HelixReborn.Api.Tests.Fixtures;

using System.Text.Json;
using Auth;
using Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Services.Keycloak;

/// <summary>
/// Test host for integration tests. Each factory gets its own Postgres database, <c>helix_test_&lt;guid&gt;</c>, on
/// the server named by <see cref="ServerConnectionVariable"/> (by default the <c>postgres-app</c> service in
/// <c>docker-compose.yml</c>), built with the real migrations and dropped with the factory, so test classes stay
/// isolated and can run in parallel.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
  /// <summary>
  /// The environment variable holding an Npgsql connection string for the Postgres server tests create their
  /// databases on. Its <c>Database</c> is the one connected to while creating and dropping them.
  /// </summary>
  public const string ServerConnectionVariable = "HELIX_TEST_POSTGRES";

  /// <summary>The <c>postgres-app</c> service in <c>docker-compose.yml</c>, used when the variable isn't set.</summary>
  public const string DefaultServerConnection =
    "Host=localhost;Port=5432;Database=postgres;Username=helix;Password=dev_password";

  /// <summary>The fake Keycloak admin API the app uses, shared by every request to this factory's host.</summary>
  public InMemoryKeycloakUserAdmin Keycloak { get; } = new();

  private readonly string _serverConnection;
  private readonly string _databaseName = $"helix_test_{Guid.NewGuid():N}";
  private readonly Lock _databaseLock = new();
  private bool _databaseCreated;

  /// <summary>The connection string of this factory's own database.</summary>
  public string ConnectionString { get; }

  /// <summary>Creates a test host on a new database, created when the host first starts.</summary>
  public ApiFactory()
  {
    var server = Environment.GetEnvironmentVariable(ServerConnectionVariable);
    _serverConnection = string.IsNullOrWhiteSpace(server) ? DefaultServerConnection : server;
    ConnectionString = new NpgsqlConnectionStringBuilder(_serverConnection) { Database = _databaseName }
      .ConnectionString;
  }

  /// <inheritdoc />
  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    builder.UseEnvironment("Testing");

    // Tests have no Keycloak: authenticate from the X-Test-Claims header instead of a JWT.
    builder.ConfigureTestServices(services =>
    {
      services
        .AddAuthentication(TestAuthHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

      // Controllers in this test assembly (TestAdminController) aren't discovered by default; add them.
      services.AddControllers().AddApplicationPart(typeof(ApiFactory).Assembly);

      // Tests never call Keycloak's admin API.
      services.RemoveAll<IKeycloakUserAdmin>();
      services.AddSingleton<IKeycloakUserAdmin>(Keycloak);
    });

    builder.ConfigureServices(services =>
    {
      // AddDbContext registers more than DbContextOptions<AppDbContext>: EF Core also adds an
      // IDbContextOptionsConfiguration<AppDbContext> per call, so removing only the options would leave Npgsql
      // registered twice. Remove every descriptor generic over AppDbContext, plus the context itself, and register it
      // again on this factory's database.
      var dbDescriptors = services
        .Where(d => d.ServiceType == typeof(AppDbContext) ||
                    (d.ServiceType.IsGenericType &&
                     d.ServiceType.GetGenericArguments().Contains(typeof(AppDbContext))))
        .ToList();

      foreach (var descriptor in dbDescriptors)
      {
        services.Remove(descriptor);
      }

      services.AddDbContext<AppDbContext>(o => o.UseNpgsql(ConnectionString));
    });
  }

  /// <inheritdoc />
  protected override IHost CreateHost(IHostBuilder builder)
  {
    // Hosts made with WithWebHostBuilder come through here too and share this factory's database, so it's created
    // only once; migrating it again is a no-op.
    lock (_databaseLock)
    {
      if (!_databaseCreated)
      {
        ExecuteOnServer($"CREATE DATABASE \"{_databaseName}\"");
        _databaseCreated = true;
      }
    }

    var host = base.CreateHost(builder);

    lock (_databaseLock)
    {
      using var scope = host.Services.CreateScope();
      scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
    }

    return host;
  }

  /// <summary>Creates a client that sends no credentials.</summary>
  /// <returns>An anonymous client.</returns>
  public HttpClient CreateAnonymousClient() => CreateClient();

  /// <summary>
  /// Creates a client signed in as a user with the given realm roles, carried in <c>realm_access</c> as Keycloak puts
  /// them, so the realm roles claims transformation runs as it does in production.
  /// </summary>
  /// <param name="userId">The user's <c>sub</c>.</param>
  /// <param name="email">The user's email, or null to leave the claim out.</param>
  /// <param name="roles">Realm roles; <c>helix-user</c> if none are given.</param>
  /// <returns>An authenticated client.</returns>
  public HttpClient CreateUserClient(
    string userId = "user-1",
    string? email = "user@example.com",
    params string[] roles)
  {
    var realmAccess = JsonSerializer.Serialize(new { roles = roles.Length > 0 ? roles : [Roles.User] });

    List<TestAuthHandler.TestClaim> claims =
    [
      new(KeycloakClaims.Subject, userId),
      new(KeycloakClaims.RealmAccess, realmAccess),
    ];
    if (email is not null)
    {
      claims.Add(new TestAuthHandler.TestClaim(KeycloakClaims.Email, email));
    }

    var client = CreateClient();
    client.DefaultRequestHeaders.Add(TestAuthHandler.ClaimsHeader, JsonSerializer.Serialize(claims));

    return client;
  }

  /// <summary>Creates a client signed in as an admin (<c>helix-admin</c> and <c>helix-user</c>).</summary>
  /// <returns>An authenticated admin client.</returns>
  public HttpClient CreateAdminClient() =>
    CreateUserClient("admin-1", "admin@example.com", Roles.Admin, Roles.User);

  /// <inheritdoc />
  protected override void Dispose(bool disposing)
  {
    base.Dispose(disposing);
    if (disposing)
    {
      if (_databaseCreated)
      {
        using (var connection = new NpgsqlConnection(ConnectionString))
        {
          NpgsqlConnection.ClearPool(connection);
        }

        // FORCE closes any connection still open.
        ExecuteOnServer($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)");
        _databaseCreated = false;
      }
    }
  }

  private void ExecuteOnServer(string sql)
  {
    using var connection = new NpgsqlConnection(_serverConnection);
    connection.Open();
    using var command = new NpgsqlCommand(sql, connection);
    command.ExecuteNonQuery();
  }
}
