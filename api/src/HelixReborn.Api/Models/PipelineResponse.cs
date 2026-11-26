namespace HelixReborn.Api.Models;

using System.Text.Json.Nodes;

/// <summary>A pipeline as the API returns it.</summary>
/// <param name="Id">Pipeline ID.</param>
/// <param name="Name">Display name.</param>
/// <param name="Spec">The spec, snake_case, with defaults filled in.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="UpdatedAt">When it was last changed.</param>
public record PipelineResponse(Guid Id, string Name, JsonObject Spec, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
