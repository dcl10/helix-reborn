namespace HelixReborn.Api.Models.Eda;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;

/// <summary>Body of <c>POST /api/experiments/{id}/eda/plots</c>.</summary>
/// <param name="Type">Which plot to make.</param>
/// <param name="Options">
/// The plot's own fields, in EDA's snake_case (e.g. <c>{"columns": ["a", "b"], "method": "spearman"}</c>). Passed
/// through to EDA, which validates them. <c>dataset_key</c> and <c>output_key</c> are always set by the API,
/// overriding anything given here; <c>plot_options</c> is replaced by <paramref name="PlotOptions"/> when that's given.
/// </param>
/// <param name="PlotOptions">Styling options (EDA's <c>PlotOptions</c>, snake_case), or null for EDA's defaults.</param>
public record EdaPlotRequest(
  [Required] EdaPlotType? Type,
  JsonObject? Options,
  JsonObject? PlotOptions);
