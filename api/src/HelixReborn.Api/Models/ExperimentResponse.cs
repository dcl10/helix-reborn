namespace HelixReborn.Api.Models;

/// <summary>An experiment as the API returns it.</summary>
/// <param name="Id">Experiment ID.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">Free-text description, if any.</param>
/// <param name="OwnerSub">Keycloak user ID of the owner.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="UpdatedAt">When it was last modified.</param>
/// <param name="Dataset">The uploaded dataset, or null if none has been uploaded yet.</param>
public record ExperimentResponse(
  Guid Id,
  string Name,
  string? Description,
  string OwnerSub,
  DateTimeOffset CreatedAt,
  DateTimeOffset UpdatedAt,
  DatasetResponse? Dataset);
