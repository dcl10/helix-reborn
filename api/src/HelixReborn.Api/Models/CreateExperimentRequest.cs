namespace HelixReborn.Api.Models;

using System.ComponentModel.DataAnnotations;

/// <summary>Body of <c>POST /api/experiments</c>.</summary>
public class CreateExperimentRequest
{
  /// <summary>Display name, 1–100 characters.</summary>
  [Required]
  [MinLength(1)]
  [MaxLength(100)]
  public string Name { get; init; } = "";

  /// <summary>Optional free-text description, up to 1000 characters.</summary>
  [MaxLength(1000)]
  public string? Description { get; init; }
}
