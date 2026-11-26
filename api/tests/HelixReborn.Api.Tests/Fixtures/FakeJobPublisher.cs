namespace HelixReborn.Api.Tests.Fixtures;

using System.Collections.Concurrent;
using Messaging;

/// <summary>
/// <see cref="IJobPublisher"/> for tests, which have no RabbitMQ. Records every published job; <see cref="FailFor"/>
/// makes publishing one experiment's jobs throw, so test classes sharing a factory don't affect each other.
/// </summary>
public class FakeJobPublisher : IJobPublisher
{
  private readonly ConcurrentQueue<TrainingJob> _jobs = new();
  private readonly ConcurrentDictionary<Guid, bool> _failing = new();

  /// <summary>Every training job published, oldest first.</summary>
  public IReadOnlyList<TrainingJob> TrainingJobs => [.. _jobs];

  /// <summary>Makes publishing this experiment's jobs throw <see cref="JobPublishException"/>.</summary>
  /// <param name="experimentId">The experiment.</param>
  public void FailFor(Guid experimentId) => _failing[experimentId] = true;

  /// <summary>The training jobs published for one experiment, oldest first.</summary>
  /// <param name="experimentId">The experiment.</param>
  /// <returns>The jobs.</returns>
  public IReadOnlyList<TrainingJob> JobsFor(Guid experimentId) => [.. _jobs.Where(j => j.ExperimentId == experimentId)];

  /// <inheritdoc />
  public Task PublishTrainingJobAsync(TrainingJob job, CancellationToken cancellationToken)
  {
    if (_failing.ContainsKey(job.ExperimentId))
    {
      throw new JobPublishException("Couldn't queue the job: connection refused");
    }

    _jobs.Enqueue(job);
    return Task.CompletedTask;
  }
}
