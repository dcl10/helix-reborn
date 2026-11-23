namespace HelixReborn.Api.Models;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Body of <c>PATCH /api/experiments/{id}</c>. Both fields are optional; a field left out of the request body keeps
/// its current value.
/// </summary>
public class UpdateExperimentRequest
{
  /// <summary>New display name, 1–100 characters, if changing it.</summary>
  [MinLength(1)]
  [MaxLength(100)]
  public string? Name { get; init; }

  /// <summary>New description, up to 1000 characters, if changing it.</summary>
  [MaxLength(1000)]
  public string? Description { get; init; }
}
