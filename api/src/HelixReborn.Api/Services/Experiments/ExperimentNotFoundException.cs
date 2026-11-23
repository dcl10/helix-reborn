namespace HelixReborn.Api.Services.Experiments;

/// <summary>No experiment with the given ID exists, or it isn't visible to the caller. The API answers 404.</summary>
/// <param name="experimentId">The experiment ID that was requested.</param>
public class ExperimentNotFoundException(Guid experimentId) : Exception($"Experiment {experimentId} doesn't exist.");
