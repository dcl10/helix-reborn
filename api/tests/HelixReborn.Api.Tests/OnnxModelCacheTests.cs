namespace HelixReborn.Api.Tests;

using System.Text;
using Config;
using Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Services.Predictions;
using Services.Storage;
using Xunit;
using ModelMetadata = Services.Predictions.ModelMetadata;

public sealed class OnnxModelCacheTests : IDisposable
{
  private static readonly Guid ExperimentId = Guid.NewGuid();

  private readonly InMemoryObjectStorage _storage = new();
  private readonly ServiceProvider _services;

  public OnnxModelCacheTests()
  {
    _services = new ServiceCollection().AddSingleton<IObjectStorage>(_storage).BuildServiceProvider();
  }

  public void Dispose() => _services.Dispose();

  [Fact]
  public async Task AcquireAsync_LoadsTheModelAndMetadata()
  {
    using var cache = CreateCache();
    var runId = await StoreAsync(PredictionSetup.Classification);

    using var lease = await cache.AcquireAsync(ExperimentId, runId, CancellationToken.None);

    Assert.True(lease.Model.Metadata.IsClassification);
    Assert.Equal(["no", "yes"], lease.Model.Metadata.Classes);
    Assert.Equal(["f0", "f1", "f2"], lease.Model.Metadata.FeatureColumns);
    Assert.Contains("input", lease.Model.Session.InputMetadata.Keys);
  }

  [Fact]
  public async Task AcquireAsync_EvictsTheLeastRecentlyUsedModel_WhenFull()
  {
    using var cache = CreateCache(size: 2);
    var first = await StoreAsync(PredictionSetup.Regression);
    var second = await StoreAsync(PredictionSetup.Regression);
    var third = await StoreAsync(PredictionSetup.Regression);

    await AcquireAndReleaseAsync(cache, first);
    await AcquireAndReleaseAsync(cache, second);
    await AcquireAndReleaseAsync(cache, first); // now the most recently used
    await AcquireAndReleaseAsync(cache, third); // evicts second
    await AcquireAndReleaseAsync(cache, first);
    await AcquireAndReleaseAsync(cache, second);

    Assert.Equal(2, cache.Count);
    Assert.Equal(1, ModelReads(first));
    Assert.Equal(2, ModelReads(second));
  }

  [Fact]
  public async Task AcquireAsync_SharesOneLoad_ForConcurrentRequests()
  {
    using var cache = CreateCache();
    var runId = await StoreAsync(PredictionSetup.Regression);

    var leases = await Task.WhenAll(
      Enumerable.Range(0, 8).Select(_ => cache.AcquireAsync(ExperimentId, runId, CancellationToken.None)));

    Assert.Equal(1, ModelReads(runId));
    Assert.All(leases, lease => Assert.Same(leases[0].Model, lease.Model));
    foreach (var lease in leases)
    {
      lease.Dispose();
    }
  }

  [Fact]
  public async Task AcquireAsync_KeepsAnEvictedSessionUsable_UntilItsLeaseIsReleased()
  {
    using var cache = CreateCache(size: 1);
    var held = await StoreAsync(PredictionSetup.Regression);
    var other = await StoreAsync(PredictionSetup.Regression);

    var lease = await cache.AcquireAsync(ExperimentId, held, CancellationToken.None);
    await AcquireAndReleaseAsync(cache, other); // evicts the held model

    Assert.Equal(1, cache.Count);
    RunOnce(lease.Model); // still usable
    lease.Dispose();
    Assert.ThrowsAny<Exception>(() => RunOnce(lease.Model)); // disposed once released
  }

  [Fact]
  public async Task AcquireAsync_DoesntCacheAFailedLoad()
  {
    using var cache = CreateCache();
    var runId = Guid.NewGuid();

    await Assert.ThrowsAsync<PredictionConflictException>(
      () => cache.AcquireAsync(ExperimentId, runId, CancellationToken.None));
    Assert.Equal(0, cache.Count);

    await PredictionSetup.StoreModelAsync(_storage, ExperimentId, runId, PredictionSetup.Regression);
    using var lease = await cache.AcquireAsync(ExperimentId, runId, CancellationToken.None);
    Assert.False(lease.Model.Metadata.IsClassification);
  }

  [Fact]
  public async Task AcquireAsync_ThrowsConflict_WhenTheModelIsntOnnx()
  {
    using var cache = CreateCache();
    var runId = await StoreAsync(PredictionSetup.Regression);
    await _storage.PutAsync(
      StorageKeys.RunPrefix(ExperimentId, runId) + "model.onnx", new MemoryStream("nope"u8.ToArray()), "x");

    var ex = await Assert.ThrowsAsync<PredictionConflictException>(
      () => cache.AcquireAsync(ExperimentId, runId, CancellationToken.None));
    Assert.Contains("model.onnx", ex.Message, StringComparison.Ordinal);
  }

  [Theory]
  [InlineData("""{"classes": null}""", "a classifier has no classes")]
  [InlineData("""{"onnx_outputs": {"label": "label", "probabilities": null}}""", "onnx_outputs.probabilities")]
  [InlineData("""{"feature_columns": []}""", "feature_columns is empty")]
  [InlineData("""{"problem_type": "clustering"}""", "unknown problem_type")]
  [InlineData("""{"onnx_input_name": null}""", "isn't valid")]
  public void Parse_RejectsInvalidMetadata(string overrides, string problem)
  {
    var metadata = System.Text.Json.Nodes.JsonNode.Parse(
      PredictionSetup.ReadFixture(PredictionSetup.Classification, "metadata.json"))!.AsObject();
    foreach (var (key, value) in System.Text.Json.Nodes.JsonNode.Parse(overrides)!.AsObject())
    {
      metadata[key] = value?.DeepClone();
    }

    var ex = Assert.Throws<InvalidDataException>(
      () => ModelMetadata.Parse(Encoding.UTF8.GetBytes(metadata.ToJsonString())));
    Assert.Contains(problem, ex.Message, StringComparison.Ordinal);
  }

  private OnnxModelCache CreateCache(int size = 10) => new(
    _services.GetRequiredService<IServiceScopeFactory>(),
    Options.Create(new PredictionOptions { ModelCacheSize = size }),
    NullLogger<OnnxModelCache>.Instance);

  private async Task<Guid> StoreAsync(string fixture)
  {
    var runId = Guid.NewGuid();
    await PredictionSetup.StoreModelAsync(_storage, ExperimentId, runId, fixture);
    return runId;
  }

  private int ModelReads(Guid runId) =>
    _storage.ReadCount(StorageKeys.RunPrefix(ExperimentId, runId) + "model.onnx");

  private static async Task AcquireAndReleaseAsync(OnnxModelCache cache, Guid runId)
  {
    using var lease = await cache.AcquireAsync(ExperimentId, runId, CancellationToken.None);
  }

  private static void RunOnce(LoadedModel model)
  {
    using var input = OrtValue.CreateTensorValueFromMemory(new float[] { 1, 2, 3 }, [1, 3]);
    using var options = new RunOptions();
    using var outputs = model.Session.Run(options, ["input"], [input], [model.Metadata.OnnxOutputs.Value!]);
    Assert.Single(outputs);
  }
}
