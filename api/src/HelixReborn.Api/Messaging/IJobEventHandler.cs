namespace HelixReborn.Api.Messaging;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Handles the events for one job type. <see cref="JobEventConsumer"/> dispatches each event to the handler whose
/// <see cref="JobType"/> matches; events with no handler (evaluation, until #34) are logged and dropped.
/// </summary>
[SuppressMessage(
  "Naming",
  "CA1711:Identifiers should not have incorrect suffix",
  Justification = "Handles job events from RabbitMQ, not a .NET event delegate; the name is the one issues #25 and #34 use.")]
public interface IJobEventHandler
{
  /// <summary>The <c>job_type</c> this handles: <c>training</c> or <c>evaluation</c>.</summary>
  string JobType { get; }

  /// <summary>Applies one event. Must be idempotent: events can arrive twice or out of order.</summary>
  /// <param name="jobEvent">The event.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>A task that completes once the event is applied; an exception requeues it.</returns>
  Task HandleAsync(JobEvent jobEvent, CancellationToken cancellationToken);
}
