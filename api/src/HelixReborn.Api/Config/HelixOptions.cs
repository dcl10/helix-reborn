namespace HelixReborn.Api.Config;

/// <summary>App-wide settings that aren't specific to any one integration.</summary>
public class HelixOptions
{
  /// <summary>Configuration section these options bind to.</summary>
  public const string SectionName = "Helix";

  /// <summary>
  /// The running image's version, reported by <c>/health</c> so the deploy workflow can confirm a rollout. Set from
  /// the image's <c>HELIX_VERSION</c> build arg (see <c>api/Dockerfile</c>); <c>"dev"</c> otherwise.
  /// </summary>
  public string Version { get; init; } = "dev";
}
