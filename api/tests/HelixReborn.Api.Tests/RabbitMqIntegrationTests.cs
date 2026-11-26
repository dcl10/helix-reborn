namespace HelixReborn.Api.Tests;

using System.Text.Json;
using Config;
using Fixtures;
using Messaging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Services.Pipelines;
using Xunit;

/// <summary>
/// Against a real RabbitMQ (<c>docker compose up -d rabbitmq</c>); skipped unless <c>HELIX_INTEGRATION=1</c>. Declaring
/// the topology here also proves the C# arguments match helix-core's: a mismatch fails with
/// <c>PRECONDITION_FAILED</c>.
/// </summary>
[Trait("Category", "Integration")]
public class RabbitMqIntegrationTests
{
  [Fact]
  public async Task PublishTrainingJobAsync_PutsTheJobOnTrainingJobs()
  {
    Assert.SkipUnless(
      Environment.GetEnvironmentVariable("HELIX_INTEGRATION") == "1",
      "Set HELIX_INTEGRATION=1 to run against compose RabbitMQ.");

    await using var connection = new RabbitMqConnection(Options.Create(new RabbitMqOptions()));
    var publisher = new RabbitMqJobPublisher(connection);
    var spec = PipelineSpecJson.Write(PipelineSpecJson.Read(RunSetup.Spec()));
    var job = new TrainingJob(
      Guid.NewGuid(),
      Guid.NewGuid(),
      Guid.NewGuid(),
      "integration-tests/data.csv",
      "integration-tests/run/",
      JsonDocument.Parse(spec).RootElement.Clone());

    await publisher.PublishTrainingJobAsync(job, TestContext.Current.CancellationToken);

    // Take our message off the queue; put anything else (a real job) back.
    var open = await connection.GetAsync(TestContext.Current.CancellationToken);
    await using var channel = await open.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
    BasicGetResult? found = null;
    for (var i = 0; i < 100 && found is null; i++)
    {
      var message = await channel.BasicGetAsync(Queues.TrainingJobs, autoAck: false, TestContext.Current.CancellationToken);
      if (message is null)
      {
        break;
      }

      if (message.BasicProperties.MessageId == job.JobId.ToString())
      {
        found = message;
        await channel.BasicAckAsync(message.DeliveryTag, multiple: false, TestContext.Current.CancellationToken);
      }
      else
      {
        await channel.BasicNackAsync(message.DeliveryTag, multiple: false, requeue: true, TestContext.Current.CancellationToken);
      }
    }

    Assert.NotNull(found);
    Assert.True(found.BasicProperties.Persistent);
    Assert.Equal("application/json", found.BasicProperties.ContentType);
    var body = JsonDocument.Parse(found.Body).RootElement;
    Assert.Equal(job.RunId.ToString(), body.GetProperty("run_id").GetString());
  }
}
