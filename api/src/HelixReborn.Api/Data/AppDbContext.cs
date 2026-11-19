namespace HelixReborn.Api.Data;

using Microsoft.EntityFrameworkCore;

/// <summary>The app database, on Postgres (in tests too).</summary>
/// <param name="options">Provider and connection settings.</param>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
  /// <inheritdoc />
  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    // No entities yet: domain entities (experiments, datasets, runs, evaluations) are added by later issues.
  }
}
