namespace HelixReborn.Api.Services.Datasets;

using Experiments;
using Models;

/// <summary>Uploads and reads an experiment's one dataset.</summary>
public interface IDatasetService
{
  /// <summary>Gets an experiment's dataset.</summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="userId">The caller's Keycloak user ID.</param>
  /// <param name="isAdmin">Whether the caller has <c>helix-admin</c>.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The dataset.</returns>
  /// <exception cref="ExperimentNotFoundException">
  /// No such experiment exists, it belongs to another user and the caller isn't an admin, or it has no dataset.
  /// </exception>
  Task<DatasetResponse> GetAsync(
    Guid experimentId,
    string userId,
    bool isAdmin,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Validates and stores an experiment's dataset: inspects the CSV while buffering it to a temp file, then uploads
  /// the temp file to <c>experiments/{experimentId}/datasets/{datasetId}/{fileName}</c>.
  /// </summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="userId">The caller's Keycloak user ID.</param>
  /// <param name="fileName">The uploaded file's original name.</param>
  /// <param name="contentType">The uploaded file's content type.</param>
  /// <param name="content">The file content.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The new dataset.</returns>
  /// <exception cref="ExperimentNotFoundException">
  /// No such experiment exists, or it belongs to another user.
  /// </exception>
  /// <exception cref="DatasetConflictException">The experiment already has a dataset.</exception>
  /// <exception cref="DatasetValidationException">The file fails one or more of the CSV rules.</exception>
  Task<DatasetResponse> UploadAsync(
    Guid experimentId,
    string userId,
    string fileName,
    string? contentType,
    Stream content,
    CancellationToken cancellationToken = default);
}
