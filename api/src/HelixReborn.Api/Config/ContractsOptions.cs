namespace HelixReborn.Api.Config;

using System.ComponentModel.DataAnnotations;

/// <summary>Where the API finds the generated contract files (<c>contracts/</c> at the repo root).</summary>
public class ContractsOptions
{
  /// <summary>Configuration section these options bind to.</summary>
  public const string SectionName = "Contracts";

  /// <summary>
  /// The contracts directory. An absolute path is used as is (the Docker image sets <c>/app/contracts</c>). A relative
  /// one is looked for in the app's content root and each of its parents, then in the app's base directory and its
  /// parents, so <c>contracts</c> finds the repo's own directory from <c>dotnet run</c> and from tests.
  /// </summary>
  [Required]
  public string Root { get; init; } = "contracts";
}
