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
          v => [.. v]));

      dataset.HasIndex(x => x.ExperimentId).IsUnique();
    });
  }
}
