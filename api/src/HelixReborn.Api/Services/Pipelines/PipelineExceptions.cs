namespace HelixReborn.Api.Services.Pipelines;

/// <summary>No such pipeline in the experiment. The API answers 404.</summary>
/// <param name="pipelineId">The pipeline ID that was requested.</param>
public class PipelineNotFoundException(Guid pipelineId) : Exception($"Pipeline {pipelineId} doesn't exist.");

/// <summary>
/// A pipeline can't be saved as asked: the experiment has no dataset yet, or the name is already used. The API answers
/// 409.
/// </summary>
/// <param name="message">Why.</param>
public class PipelineConflictException(string message) : Exception(message);

/// <summary>The spec is invalid. The API answers 400 with <see cref="Errors"/> under <c>spec</c>.</summary>
/// <param name="errors">Human-readable errors, each starting with the field's path.</param>
public class PipelineValidationException(IReadOnlyList<string> errors)
  : Exception($"The pipeline spec is invalid: {string.Join("; ", errors)}")
{
  /// <summary>The errors, each starting with the field's path.</summary>
  public IReadOnlyList<string> Errors { get; } = errors;
}
