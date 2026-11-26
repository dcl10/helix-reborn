namespace HelixReborn.Api.Services.Eda;

/// <summary>
/// An EDA call needs the experiment's dataset, but it has none yet, or its file is missing from S3. The API answers
/// 409.
/// </summary>
/// <param name="experimentId">The experiment's ID.</param>
public class NoDatasetException(Guid experimentId)
  : Exception($"Experiment {experimentId} has no dataset. Upload one first.");
