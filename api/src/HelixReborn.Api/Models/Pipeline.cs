namespace HelixReborn.Api.Models;

/// <summary>A saved pipeline in an experiment: a validated <see cref="Services.Pipelines.PipelineSpec"/> with a name.</summary>
public class Pipeline
{
  /// <summary>Unique identifier.</summary>
  public Guid Id { get; set; }

  /// <summary>The owning experiment. Cascade deleted with it.</summary>
  public Guid ExperimentId { get; set; }

  /// <summary>Display name, 1–100 characters, unique within the experiment.</summary>
  public string Name { get; set; } = "";

  /// <summary>
  /// The spec as snake_case JSON, exactly as validated (defaults filled in). Stored as <c>jsonb</c>; sent as is in a
  /// run's <c>TrainingJob</c> (#25).
  /// </summary>
  public string SpecJson { get; set; } = "";

  /// <summary>When the pipeline was created.</summary>
  public DateTimeOffset CreatedAt { get; set; }

  /// <summary>When the pipeline was last changed.</summary>
  public DateTimeOffset UpdatedAt { get; set; }
}
