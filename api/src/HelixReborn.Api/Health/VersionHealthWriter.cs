namespace HelixReborn.Api.Health;

using System.Text.Json;
using Config;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

/// <summary>
/// Writes <c>/health</c>'s response as <c>{"status":"Healthy","version":"..."}</c>, so the deploy workflow can poll
/// it to confirm a rollout reached the expected image.
/// </summary>
public static class VersionHealthWriter
{
  /// <summary>Writes the health report as JSON with the running <see cref="HelixOptions.Version"/>.</summary>
  /// <param name="context">The current request.</param>
  /// <param name="report">The health report to summarize.</param>
  /// <returns>A task that completes when the response body has been written.</returns>
  public static Task WriteAsync(HttpContext context, HealthReport report)
  {
    var version = context.RequestServices.GetRequiredService<IOptions<HelixOptions>>().Value.Version;

    context.Response.ContentType = "application/json";

    return context.Response.WriteAsync(JsonSerializer.Serialize(new
    {
      status = report.Status.ToString(),
      version,
    }));
  }
}
