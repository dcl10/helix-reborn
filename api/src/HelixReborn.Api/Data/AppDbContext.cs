namespace HelixReborn.Api.Data;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Models;

/// <summary>The app database, on Postgres (in tests too).</summary>
/// <param name="options">Provider and connection settings.</param>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
  /// <summary>Invitations sent by admins.</summary>
  public DbSet<Invitation> Invitations => Set<Invitation>();

  /// <summary>Experiments: the top-level thing a user works in.</summary>
  public DbSet<Experiment> Experiments => Set<Experiment>();

  /// <summary>Uploaded datasets, one per experiment.</summary>
  public DbSet<Dataset> Datasets => Set<Dataset>();

  /// <summary>Files registered against an experiment: datasets, EDA, models, evaluations, predictions.</summary>
  public DbSet<Artifact> Artifacts => Set<Artifact>();

  /// <summary>Saved pipeline specs, per experiment.</summary>
  public DbSet<Pipeline> Pipelines => Set<Pipeline>();

  /// <summary>Training runs: a pipeline snapshot and the worker's progress.</summary>
  public DbSet<Run> Runs => Set<Run>();

  /// <summary>Evaluations of runs' models: the options and the worker's progress.</summary>
  public DbSet<Evaluation> Evaluations => Set<Evaluation>();

  /// <inheritdoc />
  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<Invitation>(invitation =>
    {
      invitation.Property(x => x.Email).HasMaxLength(254);
      invitation.Property(x => x.KeycloakUserId).HasMaxLength(64);
      invitation.Property(x => x.InvitedBy).HasMaxLength(64);

      // Not unique: an address whose Keycloak user was deleted can be invited again.
      invitation.HasIndex(x => x.Email);
      invitation.HasIndex(x => x.KeycloakUserId).IsUnique();
    });

    modelBuilder.Entity<Experiment>(experiment =>
    {
      experiment.Property(x => x.Name).HasMaxLength(100);
      experiment.Property(x => x.Description).HasMaxLength(1000);

      experiment.HasIndex(x => x.OwnerSub);

      experiment.HasOne(x => x.Dataset)
        .WithOne()
        .HasForeignKey<Dataset>(x => x.ExperimentId)
        .OnDelete(DeleteBehavior.Cascade);
    });

    var columnsConverter = new ValueConverter<List<DatasetColumn>, string>(
      columns => JsonSerializer.Serialize(columns, JsonSerializerOptions.Web),
      json => JsonSerializer.Deserialize<List<DatasetColumn>>(json, JsonSerializerOptions.Web) ?? new());

    modelBuilder.Entity<Dataset>(dataset =>
    {
      dataset.Property(x => x.FileName).HasMaxLength(100);

      dataset.Property(x => x.Columns)
        .HasConversion(columnsConverter)
        .HasColumnType("jsonb")
        .Metadata.SetValueComparer(new ValueComparer<List<DatasetColumn>>(
          (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
          v => v.Aggregate(0, (hash, x) => HashCode.Combine(hash, x.GetHashCode())),
          v => v.ToList()));

      dataset.HasIndex(x => x.ExperimentId).IsUnique();
    });

    modelBuilder.Entity<Artifact>(artifact =>
    {
      artifact.Property(x => x.Kind).HasConversion<string>();
      artifact.Property(x => x.StorageKey).IsRequired();

      artifact.HasIndex(x => x.StorageKey).IsUnique();
      artifact.HasIndex(x => x.ExperimentId);
      artifact.HasIndex(x => x.SourceId);

      artifact.HasOne<Experiment>()
        .WithMany()
        .HasForeignKey(x => x.ExperimentId)
        .OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<Pipeline>(pipeline =>
    {
      pipeline.Property(x => x.Name).HasMaxLength(100);
      pipeline.Property(x => x.SpecJson).HasColumnType("jsonb");

      // Unique per experiment; also serves lookups by experiment.
      pipeline.HasIndex(x => new { x.ExperimentId, x.Name }).IsUnique();

      pipeline.HasOne<Experiment>()
        .WithMany()
        .HasForeignKey(x => x.ExperimentId)
        .OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<Run>(run =>
    {
      run.Property(x => x.PipelineName).HasMaxLength(100);
      run.Property(x => x.SpecJson).HasColumnType("jsonb");
      run.Property(x => x.ResultJson).HasColumnType("jsonb");
      run.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
      run.Property(x => x.ErrorType).HasMaxLength(100);
      run.Ignore(x => x.IsTerminal);

      run.HasIndex(x => x.ExperimentId);
      run.HasIndex(x => x.JobId).IsUnique();

      run.HasOne<Experiment>()
        .WithMany()
        .HasForeignKey(x => x.ExperimentId)
        .OnDelete(DeleteBehavior.Cascade);

      // A run outlives its pipeline: it keeps the spec snapshot and loses only the link.
      run.HasOne<Pipeline>()
        .WithMany()
        .HasForeignKey(x => x.PipelineId)
        .OnDelete(DeleteBehavior.SetNull);
    });

    modelBuilder.Entity<Evaluation>(evaluation =>
    {
      evaluation.Property(x => x.OptionsJson).HasColumnType("jsonb");
      evaluation.Property(x => x.MetricsJson).HasColumnType("jsonb");
      evaluation.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
      evaluation.Property(x => x.ErrorType).HasMaxLength(100);
      evaluation.Ignore(x => x.IsTerminal);

      evaluation.HasIndex(x => x.ExperimentId);
      evaluation.HasIndex(x => x.RunId);
      evaluation.HasIndex(x => x.JobId).IsUnique();

      evaluation.HasOne<Experiment>()
        .WithMany()
        .HasForeignKey(x => x.ExperimentId)
        .OnDelete(DeleteBehavior.Cascade);

      evaluation.HasOne<Run>()
        .WithMany()
        .HasForeignKey(x => x.RunId)
        .OnDelete(DeleteBehavior.Cascade);
    });
  }
}
