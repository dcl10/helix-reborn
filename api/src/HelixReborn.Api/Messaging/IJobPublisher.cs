namespace HelixReborn.Api.Messaging;

/// <summary>Publishes jobs for the workers.</summary>
public interface IJobPublisher
{
  /// <summary>
  /// Publishes a training job to <c>training.jobs</c>: persistent, JSON, <c>message_id</c> = the job ID, and confirmed
  /// by the broker before this returns.
  /// </summary>
  /// <param name="job">The job.</param>
  /// <param name="cancellationToken">Cancels the publish.</param>
  /// <returns>A task that completes once the broker has the job.</returns>
  /// <exception cref="JobPublishException">RabbitMQ is unreachable, or didn't accept the job.</exception>
  Task PublishTrainingJobAsync(TrainingJob job, CancellationToken cancellationToken);

  /// <summary>
  /// Publishes an evaluation job to <c>evaluation.jobs</c>, the same way as
  /// <see cref="PublishTrainingJobAsync"/>.
  /// </summary>
  /// <param name="job">The job.</param>
  /// <param name="cancellationToken">Cancels the publish.</param>
  /// <returns>A task that completes once the broker has the job.</returns>
  /// <exception cref="JobPublishException">RabbitMQ is unreachable, or didn't accept the job.</exception>
  Task PublishEvaluationJobAsync(EvaluationJob job, CancellationToken cancellationToken);
}

/// <summary>A job couldn't be published: RabbitMQ is unreachable, or didn't confirm it.</summary>
/// <param name="message">What went wrong.</param>
/// <param name="innerException">The underlying error, if any.</param>
public class JobPublishException(string message, Exception? innerException = null) : Exception(message, innerException);
