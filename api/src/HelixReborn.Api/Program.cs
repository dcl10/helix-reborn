using HelixReborn.Api.Data;
using HelixReborn.Api.Extensions;
using HelixReborn.Api.Health;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAppOptions();

// Keycloak JWTs; every endpoint requires a signed-in user unless it opts out.
builder.Services.AddKeycloakAuth(builder.Environment);

// Postgres via EF Core. Migrations are applied by the migrate image, never at startup.
builder.Services.AddDbContext<AppDbContext>(options =>
  options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// The one anonymous endpoint, for container health checks and the deploy workflow's rollout poll. Not a controller:
// ASP.NET Core's health checks middleware serves it.
app.MapHealthChecks("/health", new() { ResponseWriter = VersionHealthWriter.WriteAsync }).AllowAnonymous();

app.Run();
