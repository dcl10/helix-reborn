namespace HelixReborn.Api.Messaging;

using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

/// <summary>
/// Consumes <c>jobs.events</c> and hands each event to the <see cref="IJobEventHandler"/> for its <c>job_type</c>, in
/// a fresh DI scope. Manual acks, prefetch 20. An unreadable event is logged and acked (dropped); a handler failure is
/// nacked with requeue after a second. When RabbitMQ is down it reconnects with backoff (1 s doubling to 30 s), so the
/// API starts and serves requests without it.
/// </summary>
/// <param name="connection">The shared connection.</param>
/// <param name="scopes">Creates a scope per event.</param>
/// <param name="logger">Logs connection problems and dropped events.</param>
public partial class JobEventConsumer(
  RabbitMqConnection connection,
  IServiceScopeFactory scopes,
  ILogger<JobEventConsumer> logger) : BackgroundService
{
  private const ushort Prefetch = 20;
  private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);
  private static readonly TimeSpan RequeueDelay = TimeSpan.FromSeconds(1);

  /// <inheritdoc />
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    var backoff = TimeSpan.FromSeconds(1);
    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        var open = await connection.GetAsync(stoppingToken);
        await using var channel = await open.CreateChannelAsync(cancellationToken: stoppingToken);
        await Queues.DeclareTopologyAsync(channel, stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: Prefetch, global: false, stoppingToken);

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => OnReceivedAsync(channel, delivery, stoppingToken);
        consumer.ShutdownAsync += (_, _) =>
        {
          closed.TrySetResult();
          return Task.CompletedTask;
        };

        await channel.BasicConsumeAsync(Queues.JobEvents, autoAck: false, consumer, stoppingToken);
        LogConsuming(logger, Queues.JobEvents);
        backoff = TimeSpan.FromSeconds(1);

        // Until the channel or connection drops; then reconnect.
        await closed.Task.WaitAsync(stoppingToken);
        LogDisconnected(logger);
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        return;
      }
      catch (Exception ex)
      {
        LogUnavailable(logger, ex, backoff.TotalSeconds);
      }

      try
      {
        await Task.Delay(backoff, stoppingToken);
      }
      catch (OperationCanceledException)
      {
        return;
      }

      backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
    }
  }

  private async Task OnReceivedAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
  {
    JobEvent? jobEvent;
    try
    {
      jobEvent = JsonSerializer.Deserialize<JobEvent>(delivery.Body.Span, JobMessageJson.Options);
    }
    catch (JsonException ex)
    {
      LogUnreadable(logger, ex);
      await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
      return;
    }

    if (jobEvent is null)
    {
      await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
      return;
    }

    try
    {
      await using var scope = scopes.CreateAsyncScope();
      var handler = scope.ServiceProvider.GetServices<IJobEventHandler>()
        .FirstOrDefault(h => h.JobType == jobEvent.JobType);
      if (handler is null)
      {
        LogNoHandler(logger, jobEvent.JobType, jobEvent.JobId);
      }
      else
      {
        await handler.HandleAsync(jobEvent, stoppingToken);
      }

      await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
    }
    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
    {
      LogHandlerFailed(logger, ex, jobEvent.JobId, jobEvent.Status);
      await Task.Delay(RequeueDelay, stoppingToken);
      await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, stoppingToken);
    }
  }

  [LoggerMessage(Level = LogLevel.Information, Message = "Consuming {Queue}")]
  private static partial void LogConsuming(ILogger logger, string queue);

  [LoggerMessage(Level = LogLevel.Warning, Message = "Lost the RabbitMQ channel; reconnecting")]
  private static partial void LogDisconnected(ILogger logger);

  [LoggerMessage(Level = LogLevel.Warning, Message = "RabbitMQ is unavailable; retrying in {Seconds} s")]
  private static partial void LogUnavailable(ILogger logger, Exception exception, double seconds);

  [LoggerMessage(Level = LogLevel.Warning, Message = "Dropping an unreadable job event")]
  private static partial void LogUnreadable(ILogger logger, Exception exception);

  [LoggerMessage(Level = LogLevel.Information, Message = "Ignoring a {JobType} event for job {JobId}: no handler yet")]
  private static partial void LogNoHandler(ILogger logger, string jobType, Guid jobId);

  [LoggerMessage(Level = LogLevel.Error, Message = "Handling the {Status} event for job {JobId} failed; requeueing it")]
  private static partial void LogHandlerFailed(ILogger logger, Exception exception, Guid jobId, string status);
}
