namespace HelixReborn.Api.Models;

/// <summary>
/// The top-level thing a user works in: one uploaded dataset, and (added by later issues) its EDA artifacts,
/// pipelines, runs, evaluations and predictions.
/// </summary>
public class Experiment
{
  /// <summary>Unique identifier.</summary>
  public Guid Id { get; set; }

  /// <summary>Keycloak user ID (<c>sub</c>) of the owner. Only the owner and <c>helix-admin</c> can see it.</summary>
  public required string OwnerSub { get; set; }

  /// <summary>Display name, 1–100 characters.</summary>
  public required string Name { get; set; }

  /// <summary>Optional free-text description, up to 1000 characters.</summary>
  public string? Description { get; set; }

  /// <summary>When the experiment was created.</summary>
  public DateTimeOffset CreatedAt { get; set; }

  /// <summary>When the experiment was last modified.</summary>
  public DateTimeOffset UpdatedAt { get; set; }

  /// <summary>The experiment's one dataset, if a CSV has been uploaded.</summary>
  public Dataset? Dataset { get; set; }
}
