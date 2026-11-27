namespace HelixReborn.Api.Models;

/// <summary>
/// One evaluation of a succeeded run's model. Stores the options it was queued with and follows the evaluation
/// worker's job events.
/// </summary>
public class Evaluation
{
  /// <summary>Unique identifier; the <c>entity_id</c> of the job's events.</summary>
  public Guid Id { get; set; }

  /// <summary>The owning experiment. Cascade deleted with it.</summary>
  public Guid ExperimentId { get; set; }

  /// <summary>The run whose model is evaluated. Cascade deleted with it.</summary>
  public Guid RunId { get; set; }

  /// <summary>The <c>EvaluationJob</c>'s ID; events for another job ID are ignored.</summary>
  public Guid JobId { get; set; }

  /// <summary>Where the evaluation is.</summary>
  public EvaluationStatus Status { get; set; }

  /// <summary>The latest progress, 0–1.</summary>
  public double? Progress { get; set; }

  /// <summary>The latest progress message.</summary>
  public string? Message { get; set; }

  /// <summary>The options as sent to the worker, defaults filled in: snake_case JSON, stored as <c>jsonb</c>.</summary>
  public string OptionsJson { get; set; } = "";

  /// <summary>The <c>succeeded</c> event's test-set metrics: JSON, stored as <c>jsonb</c>.</summary>
  public string? MetricsJson { get; set; }

  /// <summary>The error's type, e.g. <c>InvalidSpecError</c>, or <c>QueueUnavailable</c> if it never got queued.</summary>
  public string? ErrorType { get; set; }

  /// <summary>What went wrong.</summary>
  public string? ErrorMessage { get; set; }

  /// <summary>When the evaluation was created.</summary>
  public DateTimeOffset CreatedAt { get; set; }

  /// <summary>When a worker started it.</summary>
  public DateTimeOffset? StartedAt { get; set; }

  /// <summary>When it succeeded or failed.</summary>
  public DateTimeOffset? FinishedAt { get; set; }

  /// <summary>Whether no more events will change it.</summary>
  public bool IsTerminal => Status is EvaluationStatus.Succeeded or EvaluationStatus.Failed;
}
