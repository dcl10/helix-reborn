namespace HelixReborn.Api.Services.Pipelines;

using System.Text.Json;
using System.Text.Json.Nodes;
using Data;
using Experiments;
using Microsoft.EntityFrameworkCore;
using Models;
using Npgsql;

/// <summary><see cref="IPipelineService"/> on the app database.</summary>
/// <param name="db">The app database.</param>
/// <param name="experiments">Checks the experiment is visible to, or owned by, the caller.</param>
/// <param name="validator">Validates specs against the catalog and the dataset.</param>
/// <param name="time">The clock.</param>
public class PipelineService(
  AppDbContext db,
  IExperimentService experiments,
  PipelineSpecValidator validator,
  TimeProvider time) : IPipelineService
{
  /// <inheritdoc />
  public async Task<IReadOnlyList<PipelineResponse>> ListAsync(
    Guid experimentId,
    string userId,
    bool isAdmin,
    CancellationToken cancellationToken = default)
  {
    await experiments.GetAsync(experimentId, userId, isAdmin, cancellationToken);
    var pipelines = await db.Pipelines.AsNoTracking()
      .Where(x => x.ExperimentId == experimentId)
      .OrderBy(x => x.Name)
      .ToListAsync(cancellationToken);

    return [.. pipelines.Select(ToResponse)];
  }

  /// <inheritdoc />
  public async Task<PipelineResponse> GetAsync(
    Guid experimentId,
    Guid pipelineId,
    string userId,
    bool isAdmin,
    CancellationToken cancellationToken = default)
  {
    await experiments.GetAsync(experimentId, userId, isAdmin, cancellationToken);
    return ToResponse(await Find(experimentId, pipelineId, cancellationToken));
  }

  /// <inheritdoc />
  public async Task<PipelineResponse> CreateAsync(
    Guid experimentId,
    string userId,
    string name,
    JsonObject spec,
    CancellationToken cancellationToken = default)
  {
    await experiments.GetAsync(experimentId, userId, isAdmin: false, cancellationToken);
    var specJson = await ValidatedSpecJson(experimentId, spec, cancellationToken);
    await EnsureNameFree(experimentId, name, null, cancellationToken);

    var now = time.GetUtcNow();
    var pipeline = new Pipeline
    {
      Id = Guid.NewGuid(),
      ExperimentId = experimentId,
      Name = name,
      SpecJson = specJson,
      CreatedAt = now,
      UpdatedAt = now,
    };
    db.Pipelines.Add(pipeline);
    await Save(name, cancellationToken);

    return ToResponse(pipeline);
  }

  /// <inheritdoc />
  public async Task<PipelineResponse> UpdateAsync(
    Guid experimentId,
    Guid pipelineId,
    string userId,
    string name,
    JsonObject spec,
    CancellationToken cancellationToken = default)
  {
    await experiments.GetAsync(experimentId, userId, isAdmin: false, cancellationToken);
    var pipeline = await Find(experimentId, pipelineId, cancellationToken);
    var specJson = await ValidatedSpecJson(experimentId, spec, cancellationToken);
    await EnsureNameFree(experimentId, name, pipelineId, cancellationToken);

    pipeline.Name = name;
    pipeline.SpecJson = specJson;
    pipeline.UpdatedAt = time.GetUtcNow();
    await Save(name, cancellationToken);

    return ToResponse(pipeline);
  }

  /// <inheritdoc />
  public async Task DeleteAsync(
    Guid experimentId,
    Guid pipelineId,
    string userId,
    CancellationToken cancellationToken = default)
  {
    await experiments.GetAsync(experimentId, userId, isAdmin: false, cancellationToken);
    var pipeline = await Find(experimentId, pipelineId, cancellationToken);

    db.Pipelines.Remove(pipeline);
    await db.SaveChangesAsync(cancellationToken);
  }

  private async Task<Pipeline> Find(Guid experimentId, Guid pipelineId, CancellationToken cancellationToken) =>
    await db.Pipelines.SingleOrDefaultAsync(x => x.Id == pipelineId && x.ExperimentId == experimentId, cancellationToken)
    ?? throw new PipelineNotFoundException(pipelineId);

  /// <summary>Parses and validates a spec against the experiment's dataset; returns it as normalised JSON.</summary>
  private async Task<string> ValidatedSpecJson(Guid experimentId, JsonObject json, CancellationToken cancellationToken)
  {
    var dataset = await db.Datasets.AsNoTracking().SingleOrDefaultAsync(x => x.ExperimentId == experimentId, cancellationToken)
      ?? throw new PipelineConflictException("Upload a dataset before saving a pipeline: its columns are needed to check it.");

    PipelineSpec spec;
    try
    {
      spec = PipelineSpecJson.Read(json);
    }
    catch (JsonException ex)
    {
      throw new PipelineValidationException([$"spec: {ex.Message}"]);
    }

    var errors = validator.Validate(spec, dataset);
    return errors.Count > 0 ? throw new PipelineValidationException(errors) : PipelineSpecJson.Write(spec);
  }

  private async Task EnsureNameFree(Guid experimentId, string name, Guid? exceptId, CancellationToken cancellationToken)
  {
    var taken = await db.Pipelines.AnyAsync(
      x => x.ExperimentId == experimentId && x.Name == name && x.Id != exceptId,
      cancellationToken);
    if (taken)
    {
      throw NameTaken(name);
    }
  }

  /// <summary>Saves, turning a unique-name race lost to another request into the same conflict.</summary>
  private async Task Save(string name, CancellationToken cancellationToken)
  {
    try
    {
      await db.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
    {
      throw NameTaken(name);
    }
  }

  private static PipelineConflictException NameTaken(string name) =>
    new($"This experiment already has a pipeline called '{name}'.");

  private static PipelineResponse ToResponse(Pipeline x) =>
    new(x.Id, x.Name, JsonNode.Parse(x.SpecJson)!.AsObject(), x.CreatedAt, x.UpdatedAt);
}
