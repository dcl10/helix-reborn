namespace HelixReborn.Api.Services.Pipelines;

using System.Text.Json.Nodes;
using Experiments;
using Models;

/// <summary>
/// An experiment's saved pipelines. Reads are for the owner or <c>helix-admin</c>; writes are owner only, so an admin's
/// write on someone else's experiment is a <see cref="ExperimentNotFoundException"/>, as for experiments.
/// </summary>
public interface IPipelineService
{
  /// <summary>Lists an experiment's pipelines, by name.</summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="userId">The caller's Keycloak user ID.</param>
  /// <param name="isAdmin">Whether the caller has <c>helix-admin</c>.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The pipelines.</returns>
  /// <exception cref="ExperimentNotFoundException">No such experiment, or it isn't visible to the caller.</exception>
  Task<IReadOnlyList<PipelineResponse>> ListAsync(
    Guid experimentId,
    string userId,
    bool isAdmin,
    CancellationToken cancellationToken = default);

  /// <summary>Gets one pipeline.</summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="pipelineId">The pipeline's ID.</param>
  /// <param name="userId">The caller's Keycloak user ID.</param>
  /// <param name="isAdmin">Whether the caller has <c>helix-admin</c>.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The pipeline.</returns>
  /// <exception cref="ExperimentNotFoundException">No such experiment, or it isn't visible to the caller.</exception>
  /// <exception cref="PipelineNotFoundException">No such pipeline in the experiment.</exception>
  Task<PipelineResponse> GetAsync(
    Guid experimentId,
    Guid pipelineId,
    string userId,
    bool isAdmin,
    CancellationToken cancellationToken = default);

  /// <summary>Validates and saves a new pipeline.</summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="userId">The caller's Keycloak user ID; must be the owner.</param>
  /// <param name="name">The pipeline's name, unique within the experiment.</param>
  /// <param name="spec">The spec, snake_case.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The new pipeline.</returns>
  /// <exception cref="ExperimentNotFoundException">No such experiment, or the caller doesn't own it.</exception>
  /// <exception cref="PipelineConflictException">The experiment has no dataset, or the name is taken.</exception>
  /// <exception cref="PipelineValidationException">The spec is invalid.</exception>
  Task<PipelineResponse> CreateAsync(
    Guid experimentId,
    string userId,
    string name,
    JsonObject spec,
    CancellationToken cancellationToken = default);

  /// <summary>Validates and replaces a pipeline's name and spec.</summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="pipelineId">The pipeline's ID.</param>
  /// <param name="userId">The caller's Keycloak user ID; must be the owner.</param>
  /// <param name="name">The new name, unique within the experiment.</param>
  /// <param name="spec">The new spec, snake_case.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The updated pipeline.</returns>
  /// <exception cref="ExperimentNotFoundException">No such experiment, or the caller doesn't own it.</exception>
  /// <exception cref="PipelineNotFoundException">No such pipeline in the experiment.</exception>
  /// <exception cref="PipelineConflictException">The experiment has no dataset, or the name is taken.</exception>
  /// <exception cref="PipelineValidationException">The spec is invalid.</exception>
  Task<PipelineResponse> UpdateAsync(
    Guid experimentId,
    Guid pipelineId,
    string userId,
    string name,
    JsonObject spec,
    CancellationToken cancellationToken = default);

  /// <summary>Deletes a pipeline. Runs keep their own copy of the spec (#25).</summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="pipelineId">The pipeline's ID.</param>
  /// <param name="userId">The caller's Keycloak user ID; must be the owner.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <exception cref="ExperimentNotFoundException">No such experiment, or the caller doesn't own it.</exception>
  /// <exception cref="PipelineNotFoundException">No such pipeline in the experiment.</exception>
  Task DeleteAsync(Guid experimentId, Guid pipelineId, string userId, CancellationToken cancellationToken = default);
}
