namespace HelixReborn.Api.Models;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;

/// <summary>Body of <c>POST</c> and <c>PUT /api/experiments/{id}/pipelines…</c>.</summary>
public class SavePipelineRequest
{
  /// <summary>Display name, 1–100 characters, unique within the experiment.</summary>
  [Required]
  [MinLength(1)]
  [MaxLength(100)]
  public string Name { get; init; } = "";

  /// <summary>The pipeline spec, in the contract's snake_case (<c>contracts/messages/pipeline-spec.schema.json</c>).</summary>
  [Required]
  public JsonObject? Spec { get; init; }
}
