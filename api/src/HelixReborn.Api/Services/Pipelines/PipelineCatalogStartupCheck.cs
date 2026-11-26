namespace HelixReborn.Api.Services.Pipelines;

/// <summary>
/// Loads the pipeline catalog when the app starts, so a missing or invalid <c>contracts/pipeline-catalog.json</c>
/// stops startup instead of failing the first pipeline request.
/// </summary>
/// <param name="catalog">The catalog; resolving it loads and compiles the file.</param>
/// <param name="logger">Logs which file was loaded.</param>
public partial class PipelineCatalogStartupCheck(IPipelineCatalog catalog, ILogger<PipelineCatalogStartupCheck> logger)
  : IHostedService
{
  /// <summary>Checks the catalog loaded; the constructor's dependency has already thrown if it didn't.</summary>
  /// <param name="cancellationToken">Unused.</param>
  /// <returns>A completed task.</returns>
  public Task StartAsync(CancellationToken cancellationToken)
  {
    LogLoaded(logger, catalog.Steps.Count, (catalog as PipelineCatalog)?.Path ?? "(not from a file)");
    return Task.CompletedTask;
  }

  /// <summary>Nothing to stop.</summary>
  /// <param name="cancellationToken">Unused.</param>
  /// <returns>A completed task.</returns>
  public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

  [LoggerMessage(Level = LogLevel.Information, Message = "Loaded {StepCount} pipeline steps from {Path}")]
  private static partial void LogLoaded(ILogger logger, int stepCount, string path);
}
