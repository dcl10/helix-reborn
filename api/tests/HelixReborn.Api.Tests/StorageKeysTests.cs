namespace HelixReborn.Api.Tests;

using Services.Storage;
using Xunit;

public class StorageKeysTests
{
  private static readonly Guid ExperimentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
  private static readonly Guid DatasetId = Guid.Parse("22222222-2222-2222-2222-222222222222");

  [Fact]
  public void Experiment_ReturnsPrefix()
  {
    Assert.Equal(
      "experiments/11111111-1111-1111-1111-111111111111/",
      StorageKeys.Experiment(ExperimentId));
  }

  [Fact]
  public void Dataset_ReturnsKey()
  {
    Assert.Equal(
      "experiments/11111111-1111-1111-1111-111111111111/datasets/22222222-2222-2222-2222-222222222222/bone.csv",
      StorageKeys.Dataset(ExperimentId, DatasetId, "bone.csv"));
  }

  [Fact]
  public void Eda_ReturnsKey()
  {
    var artifactId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    Assert.Equal(
      "experiments/11111111-1111-1111-1111-111111111111/eda/33333333-3333-3333-3333-333333333333.png",
      StorageKeys.Eda(ExperimentId, artifactId, "png"));
  }
}
