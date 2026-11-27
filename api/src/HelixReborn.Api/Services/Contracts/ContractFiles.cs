namespace HelixReborn.Api.Services.Contracts;

/// <summary>Finds files in the generated contracts directory (<see cref="Config.ContractsOptions.Root"/>).</summary>
public static class ContractFiles
{
  /// <summary>
  /// Finds a contract file: under <paramref name="root"/> itself when absolute, otherwise the first
  /// <c>{dir}/{root}/{relativePath}</c> walking up from the content root, then from the app's base directory.
  /// </summary>
  /// <param name="root">The configured contracts directory.</param>
  /// <param name="contentRoot">The app's content root.</param>
  /// <param name="relativePath">The file's path inside the contracts directory, e.g. <c>pipeline-catalog.json</c>.</param>
  /// <param name="description">What the file is, for errors, e.g. <c>"pipeline catalog"</c>.</param>
  /// <returns>The file's full path.</returns>
  /// <exception cref="InvalidOperationException">No such file was found.</exception>
  public static string Resolve(string root, string contentRoot, string relativePath, string description)
  {
    if (Path.IsPathRooted(root))
    {
      var path = Path.Combine(root, relativePath);
      return File.Exists(path)
        ? path
        : throw new InvalidOperationException($"The {description} {path} doesn't exist. Check Contracts:Root.");
    }

    foreach (var start in new[] { contentRoot, AppContext.BaseDirectory })
    {
      for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
      {
        var path = Path.Combine(dir.FullName, root, relativePath);
        if (File.Exists(path))
        {
          return path;
        }
      }
    }

    throw new InvalidOperationException(
      $"Couldn't find the {description} {root}/{relativePath} in {contentRoot}, {AppContext.BaseDirectory} or any of their parents. " +
      "Set Contracts:Root to the contracts directory.");
  }
}
