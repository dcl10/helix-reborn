namespace HelixReborn.Api.Services.Experiments;

using Models;

/// <summary>
/// Experiments CRUD. Users see only their own experiments; <c>helix-admin</c> can read (but not modify) everyone's.
/// </summary>
public interface IExperimentService
{
  /// <summary>Lists experiments, newest first.</summary>
  /// <param name="userId">The caller's Keycloak user ID.</param>
  /// <param name="isAdmin">Whether the caller has <c>helix-admin</c>.</param>
  /// <param name="all">
  /// When true and <paramref name="isAdmin"/> is true, lists every user's experiments; otherwise only the caller's.
  /// </param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The experiments.</returns>
  Task<IReadOnlyList<ExperimentResponse>> ListAsync(
    string userId,
    bool isAdmin,
    bool all,
    CancellationToken cancellationToken = default);

  /// <summary>Creates an experiment owned by the caller.</summary>
  /// <param name="userId">The caller's Keycloak user ID, stored as the owner.</param>
  /// <param name="name">Display name, 1–100 characters.</param>
  /// <param name="description">Optional description, up to 1000 characters.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The new experiment.</returns>
  Task<ExperimentResponse> CreateAsync(
    string userId,
    string name,
    string? description,
    CancellationToken cancellationToken = default);

  /// <summary>Gets one experiment.</summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="userId">The caller's Keycloak user ID.</param>
  /// <param name="isAdmin">Whether the caller has <c>helix-admin</c>.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The experiment.</returns>
  /// <exception cref="ExperimentNotFoundException">
  /// No such experiment exists, or it belongs to another user and the caller isn't an admin.
  /// </exception>
  Task<ExperimentResponse> GetAsync(
    Guid experimentId,
    string userId,
    bool isAdmin,
    CancellationToken cancellationToken = default);

  /// <summary>Updates an experiment's name and/or description. Owner only, even for admins.</summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="userId">The caller's Keycloak user ID.</param>
  /// <param name="name">New name, if changing it.</param>
  /// <param name="description">New description, if changing it.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The updated experiment.</returns>
  /// <exception cref="ExperimentNotFoundException">
  /// No such experiment exists, or it belongs to another user.
  /// </exception>
  Task<ExperimentResponse> UpdateAsync(
    Guid experimentId,
    string userId,
    string? name,
    string? description,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Deletes an experiment: its database rows and every object stored under it. Owner only, even for admins.
  /// </summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="userId">The caller's Keycloak user ID.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <exception cref="ExperimentNotFoundException">
  /// No such experiment exists, or it belongs to another user.
  /// </exception>
  Task DeleteAsync(Guid experimentId, string userId, CancellationToken cancellationToken = default);
}
