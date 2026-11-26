namespace HelixReborn.Api.Messaging;

using RabbitMQ.Client;

/// <summary>
/// The RabbitMQ topology shared with the Python workers (<c>docs/architecture.md</c>, "Messaging"; helix-core's
/// <c>declare_topology</c>). Both sides declare it idempotently with exactly these arguments: any difference and
/// RabbitMQ closes the channel with <c>PRECONDITION_FAILED</c>.
/// </summary>
public static class Queues
{
  /// <summary>Training jobs, API → training worker.</summary>
  public const string TrainingJobs = "training.jobs";

  /// <summary>Evaluation jobs, API → evaluation worker (#34).</summary>
  public const string EvaluationJobs = "evaluation.jobs";

  /// <summary>Job events, workers → API.</summary>
  public const string JobEvents = "jobs.events";

  /// <summary>The direct exchange rejected jobs are dead-lettered to.</summary>
  public const string DeadLetterExchange = "helix.dlx";

  /// <summary>The dead-letter queue for a job queue: <c>{queue}.dead</c>.</summary>
  /// <param name="queue">The job queue.</param>
  /// <returns>The dead-letter queue's name, which is also its routing key on <see cref="DeadLetterExchange"/>.</returns>
  public static string DeadLetterQueue(string queue) => $"{queue}.dead";

  /// <summary>Declares the exchange and queues, as the workers do.</summary>
  /// <param name="channel">An open channel.</param>
  /// <param name="cancellationToken">Cancels the declarations.</param>
  /// <returns>A task that completes once everything is declared.</returns>
  public static async Task DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken)
  {
    await channel.ExchangeDeclareAsync(
      DeadLetterExchange,
      ExchangeType.Direct,
      durable: true,
      autoDelete: false,
      cancellationToken: cancellationToken);

    foreach (var queue in new[] { TrainingJobs, EvaluationJobs })
    {
      var dead = DeadLetterQueue(queue);
      await channel.QueueDeclareAsync(
        dead, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
      await channel.QueueBindAsync(dead, DeadLetterExchange, dead, cancellationToken: cancellationToken);
      await channel.QueueDeclareAsync(
        queue,
        durable: true,
        exclusive: false,
        autoDelete: false,
        arguments: new Dictionary<string, object?>
        {
          ["x-dead-letter-exchange"] = DeadLetterExchange,
          ["x-dead-letter-routing-key"] = dead,
        },
        cancellationToken: cancellationToken);
    }

    await channel.QueueDeclareAsync(
      JobEvents, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
  }
}
