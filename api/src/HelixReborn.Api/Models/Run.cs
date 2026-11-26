namespace HelixReborn.Api.Models;

/// <summary>
/// One training of a pipeline. It snapshots the pipeline's name and spec when it's created, so it stays meaningful
/// after the pipeline changes or is deleted, and follows the worker's job events.
/// </summary>
public class Run
{
  /// <summary>Unique identifier; the <c>entity_id</c> of the job's events.</summary>
  public Guid Id { get; set; }

  /// <summary>The owning experiment. Cascade deleted with it.</summary>
  public Guid ExperimentId { get; set; }

  /// <summary>The pipeline it ran, or null once that pipeline is deleted.</summary>
  public Guid? PipelineId { get; set; }

  /// <summary>The pipeline's name when the run was created.</summary>
  public string PipelineName { get; set; } = "";

  /// <summary>The pipeline's spec when the run was created: snake_case JSON, stored as <c>jsonb</c>.</summary>
  public string SpecJson { get; set; } = "";

  /// <summary>The <c>TrainingJob</c>'s ID; events for another job ID are ignored.</summary>
  public Guid JobId { get; set; }

  /// <summary>Where the run is.</summary>
  public RunStatus Status { get; set; }

  /// <summary>The latest progress, 0–1.</summary>
  public double? Progress { get; set; }

  /// <summary>The latest progress message.</summary>
  public string? Message { get; set; }

  /// <summary>The <c>succeeded</c> event's result: snake_case JSON, stored as <c>jsonb</c>.</summary>
  public string? ResultJson { get; set; }

  /// <summary>The error's type, e.g. <c>InvalidDataError</c>, or <c>QueueUnavailable</c> if it never got queued.</summary>
  public string? ErrorType { get; set; }

  /// <summary>What went wrong.</summary>
  public string? ErrorMessage { get; set; }

  /// <summary>When the run was created.</summary>
  public DateTimeOffset CreatedAt { get; set; }

  /// <summary>When a worker started it.</summary>
  public DateTimeOffset? StartedAt { get; set; }

  /// <summary>When it succeeded or failed.</summary>
  public DateTimeOffset? FinishedAt { get; set; }

  /// <summary>Whether no more events will change it.</summary>
  public bool IsTerminal => Status is RunStatus.Succeeded or RunStatus.Failed;
}
