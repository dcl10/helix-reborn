namespace HelixReborn.Api.Messaging;

using System.Text.Json;
using RabbitMQ.Client;

/// <summary>
/// <see cref="IJobPublisher"/> on RabbitMQ: a channel per publish, with publisher confirms, so the publish only
/// returns once the broker has the job. Jobs are rare enough that a channel each costs nothing.
/// </summary>
/// <param name="connection">The shared connection.</param>
public class RabbitMqJobPublisher(RabbitMqConnection connection) : IJobPublisher
{
  private static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(10);

  /// <inheritdoc />
  public async Task PublishTrainingJobAsync(TrainingJob job, CancellationToken cancellationToken)
  {
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(PublishTimeout);
    try
    {
      var open = await connection.GetAsync(timeout.Token);
      await using var channel = await open.CreateChannelAsync(
        new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
        timeout.Token);

      var properties = new BasicProperties
      {
        Persistent = true,
        ContentType = "application/json",
        MessageId = job.JobId.ToString(),
      };
      var body = JsonSerializer.SerializeToUtf8Bytes(job, JobMessageJson.Options);

      // mandatory: an unroutable job (no queue) fails the publish instead of vanishing.
      await channel.BasicPublishAsync(
        exchange: "",
        routingKey: Queues.TrainingJobs,
        mandatory: true,
        basicProperties: properties,
        body: body,
        cancellationToken: timeout.Token);
    }
    catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
    {
      throw new JobPublishException($"RabbitMQ didn't confirm the job within {PublishTimeout.TotalSeconds:0} seconds.", ex);
    }
    catch (Exception ex) when (ex is not OperationCanceledException and not JobPublishException)
    {
      throw new JobPublishException($"Couldn't queue the job: {ex.Message}", ex);
    }
  }
}
