using HelixReborn.Api.Data;
using HelixReborn.Api.Extensions;
using HelixReborn.Api.Health;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAppOptions();

// Keycloak JWTs; every endpoint requires a signed-in user unless it opts out.
builder.Services.AddKeycloakAuth(builder.Environment);

// Invitations, sent through the Keycloak admin API as the helix-api service account.
builder.Services.AddInvitations(builder.Configuration);

// Experiments, datasets and the S3-compatible object store they're uploaded to.
builder.Services.AddObjectStorage();
builder.Services.AddExperiments();

// Exploratory data analysis, made by the EDA service (called with a helix-api service token).
builder.Services.AddEda(builder.Configuration);

// Pipelines: the step catalog from contracts/pipeline-catalog.json, and saved, validated pipeline specs.
builder.Services.AddPipelines();

// Training runs: TrainingJobs published to RabbitMQ, and the workers' events consumed from jobs.events.
builder.Services.AddRuns();

// Evaluations: EvaluationJobs for a succeeded run's model, with their events from jobs.events.
builder.Services.AddEvaluations();

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
