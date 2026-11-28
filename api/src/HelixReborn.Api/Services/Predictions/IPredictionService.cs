namespace HelixReborn.Api.Services.Predictions;

using Experiments;
using Models;
using Runs;

/// <summary>
/// Predictions with a succeeded run's ONNX model, run inside the API. Predicting is owner only, so an admin's is a
/// <see cref="ExperimentNotFoundException"/>, as for experiments; listing is for the owner or <c>helix-admin</c>.
/// </summary>
public interface IPredictionService
{
  /// <summary>
  /// Predicts on an uploaded CSV: maps its columns to the model's features by name (extra columns are ignored, blanks
  /// become <c>NaN</c>), runs the model, stores the output CSV and records it as a <c>predictions</c> artifact.
  /// </summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="runId">The run whose model to use.</param>
  /// <param name="userId">The caller's Keycloak user ID; must be the owner.</param>
  /// <param name="fileName">The upload's file name; must end in <c>.csv</c>.</param>
  /// <param name="contentType">The upload's content type.</param>
  /// <param name="content">The upload.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The prediction, with a preview of its first rows.</returns>
  /// <exception cref="ExperimentNotFoundException">No such experiment, or the caller doesn't own it.</exception>
  /// <exception cref="RunNotFoundException">No such run in the experiment.</exception>
  /// <exception cref="PredictionConflictException">The run hasn't succeeded, or its model is unusable.</exception>
  /// <exception cref="PredictionValidationException">The upload isn't a CSV the model can predict on.</exception>
  Task<PredictionResponse> PredictAsync(
    Guid experimentId,
    Guid runId,
    string userId,
    string fileName,
    string? contentType,
    Stream content,
    CancellationToken cancellationToken = default);

  /// <summary>Lists an experiment's prediction output artifacts, oldest first.</summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="userId">The caller's Keycloak user ID.</param>
  /// <param name="isAdmin">Whether the caller has <c>helix-admin</c>.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The artifacts with <c>sourceType</c> <c>prediction</c>.</returns>
  /// <exception cref="ExperimentNotFoundException">No such experiment, or it isn't visible to the caller.</exception>
  Task<IReadOnlyList<ArtifactResponse>> ListAsync(
    Guid experimentId,
    string userId,
    bool isAdmin,
    CancellationToken cancellationToken = default);
}
