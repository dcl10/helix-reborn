namespace HelixReborn.Api.Models.Eda;

using System.Text.Json;

/// <summary>Response of <c>POST /api/experiments/{id}/eda/tables/normality</c>.</summary>
/// <param name="Artifact">The CSV table, recorded as an <see cref="ArtifactKind.EdaTable"/> artifact.</param>
/// <param name="Rows">
/// One row per tested column, passed through from EDA as-is (snake_case: <c>column</c>, <c>n</c>,
/// <c>shapiro_statistic</c>, <c>shapiro_p</c>, <c>ks_statistic</c>, <c>ks_p</c>, <c>normal_at_0_05</c>).
/// </param>
public record NormalityResponse(ArtifactResponse Artifact, JsonElement Rows);
