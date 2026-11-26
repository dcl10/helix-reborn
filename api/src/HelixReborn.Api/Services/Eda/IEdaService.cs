namespace HelixReborn.Api.Services.Eda;

using System.Text.Json;
using Experiments;
using Models;
using Models.Eda;

/// <summary>
/// An experiment's exploratory data analysis: summaries, plots and tables made by the EDA service from the
/// experiment's dataset, with every plot and table recorded as an artifact (<c>sourceType</c> <c>"eda"</c>). Reads
/// are for the owner or <c>helix-admin</c>; creating plots and tables is owner only.
/// </summary>
public interface IEdaService
{
  /// <summary>Summarises the experiment's dataset.</summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="userId">The caller's Keycloak user ID.</param>
  /// <param name="isAdmin">Whether the caller has <c>helix-admin</c>.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>EDA's summary JSON, as-is.</returns>
  /// <exception cref="ExperimentNotFoundException">No such experiment, or it isn't visible to the caller.</exception>
  /// <exception cref="NoDatasetException">The experiment has no dataset, or its file is missing.</exception>
  /// <exception cref="EdaException">EDA rejected the request (422) or failed (502).</exception>
  Task<JsonElement> GetSummaryAsync(
    Guid experimentId,
    string userId,
    bool isAdmin,
    CancellationToken cancellationToken = default);

  /// <summary>Renders a plot of the experiment's dataset and records it as an <see cref="ArtifactKind.EdaPlot"/>.</summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="userId">The caller's Keycloak user ID; must be the owner.</param>
  /// <param name="request">Which plot, and its options.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The new artifact.</returns>
  /// <exception cref="ExperimentNotFoundException">No such experiment, or the caller doesn't own it.</exception>
  /// <exception cref="NoDatasetException">The experiment has no dataset, or its file is missing.</exception>
  /// <exception cref="EdaException">EDA rejected the request (422) or failed (502).</exception>
  Task<ArtifactResponse> CreatePlotAsync(
    Guid experimentId,
    string userId,
    EdaPlotRequest request,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Runs normality tests on the experiment's dataset and records the CSV as an <see cref="ArtifactKind.EdaTable"/>.
  /// </summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="userId">The caller's Keycloak user ID; must be the owner.</param>
  /// <param name="columns">The columns to test, or null for every numeric column.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The new artifact and the table's rows.</returns>
  /// <exception cref="ExperimentNotFoundException">No such experiment, or the caller doesn't own it.</exception>
  /// <exception cref="NoDatasetException">The experiment has no dataset, or its file is missing.</exception>
  /// <exception cref="EdaException">EDA rejected the request (422) or failed (502).</exception>
  Task<NormalityResponse> CreateNormalityTableAsync(
    Guid experimentId,
    string userId,
    IReadOnlyList<string>? columns,
    CancellationToken cancellationToken = default);

  /// <summary>Lists the experiment's EDA artifacts, oldest first.</summary>
  /// <param name="experimentId">The experiment's ID.</param>
  /// <param name="userId">The caller's Keycloak user ID.</param>
  /// <param name="isAdmin">Whether the caller has <c>helix-admin</c>.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The artifacts whose <c>sourceType</c> is <c>"eda"</c>.</returns>
  /// <exception cref="ExperimentNotFoundException">No such experiment, or it isn't visible to the caller.</exception>
  Task<IReadOnlyList<ArtifactResponse>> ListArtifactsAsync(
    Guid experimentId,
    string userId,
    bool isAdmin,
    CancellationToken cancellationToken = default);
}
