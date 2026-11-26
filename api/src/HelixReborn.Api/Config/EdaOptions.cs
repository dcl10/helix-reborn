namespace HelixReborn.Api.Config;

using System.ComponentModel.DataAnnotations;

/// <summary>Where the API finds the EDA service, and how long it waits for it.</summary>
public class EdaOptions
{
  /// <summary>Configuration section these options bind to.</summary>
  public const string SectionName = "Eda";

  /// <summary>
  /// The EDA service's base URL: <c>http://localhost:8001</c> when EDA runs in Compose and the API on the host,
  /// <c>http://localhost:8000</c> with <c>uv run helix-eda</c>, <c>http://eda:8000</c> inside Compose.
  /// </summary>
  [Required]
  [Url]
  public string BaseUrl { get; init; } = "http://localhost:8001";

  /// <summary>
  /// The audience EDA requires in the service token, <c>helix-eda</c>. Keycloak's audience mapper on the
  /// <c>helix-api</c> client adds it, so this must match that mapper and EDA's <c>HELIX_EDA_AUDIENCE</c>.
  /// </summary>
  [Required]
  public string Audience { get; init; } = "helix-eda";

  /// <summary>How long to wait for one EDA call, in seconds. t-SNE on a large dataset can take a while.</summary>
  [Range(1, 3600)]
  public int TimeoutSeconds { get; init; } = 120;
}
