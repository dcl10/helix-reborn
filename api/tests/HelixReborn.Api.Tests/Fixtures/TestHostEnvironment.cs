namespace HelixReborn.Api.Tests.Fixtures;

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

/// <summary>A bare <see cref="IHostEnvironment"/> for constructing services that resolve contract files.</summary>
/// <param name="contentRoot">The content root to resolve relative paths from.</param>
public sealed class TestHostEnvironment(string contentRoot) : IHostEnvironment
{
  /// <inheritdoc />
  public string EnvironmentName { get; set; } = "Testing";

  /// <inheritdoc />
  public string ApplicationName { get; set; } = "HelixReborn.Api";

  /// <inheritdoc />
  public string ContentRootPath { get; set; } = contentRoot;

  /// <inheritdoc />
  public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
