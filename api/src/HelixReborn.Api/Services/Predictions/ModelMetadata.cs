namespace HelixReborn.Api.Services.Predictions;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// The parts of a run's <c>metadata.json</c> (<c>contracts/model-metadata.schema.json</c>) that prediction needs. Read
/// from snake_case JSON with <see cref="ModelMetadataJson.Options"/>; other fields are ignored.
/// </summary>
/// <param name="SchemaVersion">The metadata schema version; 1.</param>
/// <param name="RunId">The run that trained the model.</param>
/// <param name="ProblemType"><c>classification</c> or <c>regression</c>.</param>
/// <param name="TargetColumn">The column the model predicts.</param>
/// <param name="FeatureColumns">The model's input columns, in input order.</param>
/// <param name="IdColumn">The dataset's ID column, passed through to the output if the upload has it.</param>
/// <param name="Classes">Class labels by class index; null for regression.</param>
/// <param name="OnnxInputName">The ONNX input's name, <c>input</c>.</param>
/// <param name="OnnxOutputs">The ONNX output names.</param>
public sealed record ModelMetadata(
  int SchemaVersion,
  Guid RunId,
  string ProblemType,
  string TargetColumn,
  IReadOnlyList<string> FeatureColumns,
  string? IdColumn,
  IReadOnlyList<string>? Classes,
  string OnnxInputName,
  ModelOutputs OnnxOutputs)
{
  /// <summary><see cref="ProblemType"/> of a classifier.</summary>
  public const string Classification = "classification";

  /// <summary><see cref="ProblemType"/> of a regressor.</summary>
  public const string Regression = "regression";

  /// <summary>Whether the model is a classifier.</summary>
  [JsonIgnore]
  public bool IsClassification => ProblemType == Classification;

  /// <summary>Parses and checks <c>metadata.json</c>.</summary>
  /// <param name="json">The file's content.</param>
  /// <returns>The metadata.</returns>
  /// <exception cref="InvalidDataException">The file isn't valid model metadata.</exception>
  public static ModelMetadata Parse(ReadOnlySpan<byte> json)
  {
    ModelMetadata? metadata;
    try
    {
      metadata = JsonSerializer.Deserialize<ModelMetadata>(json, ModelMetadataJson.Options);
    }
    catch (JsonException ex)
    {
      throw new InvalidDataException($"metadata.json isn't valid: {ex.Message}", ex);
    }

    var problem = metadata switch
    {
      null => "it's null",
      { FeatureColumns.Count: 0 } => "feature_columns is empty",
      { ProblemType: Classification, Classes: null or { Count: 0 } } => "a classifier has no classes",
      { ProblemType: Classification, OnnxOutputs: { Label: null } or { Probabilities: null } } =>
        "a classifier needs onnx_outputs.label and onnx_outputs.probabilities",
      { ProblemType: Regression, OnnxOutputs.Value: null } => "a regressor needs onnx_outputs.value",
      { ProblemType: not (Classification or Regression) } => $"unknown problem_type '{metadata.ProblemType}'",
      _ => null,
    };

    return problem is null ? metadata! : throw new InvalidDataException($"metadata.json isn't valid: {problem}.");
  }
}

/// <summary>
/// A model's ONNX output names: <c>label</c> and <c>probabilities</c> for a classifier, <c>value</c> for a regressor.
/// </summary>
/// <param name="Label">The int64 class index output <c>[N]</c>.</param>
/// <param name="Probabilities">The float32 class probabilities output <c>[N, K]</c>, in <c>classes</c> order.</param>
/// <param name="Value">The float32 regression output <c>[N, 1]</c>.</param>
public sealed record ModelOutputs(string? Label = null, string? Probabilities = null, string? Value = null);

/// <summary>
/// JSON options for <c>metadata.json</c>: snake_case; constructor parameters without a default are required, and
/// non-nullable ones can't be null.
/// </summary>
public static class ModelMetadataJson
{
  /// <summary>The options.</summary>
  public static readonly JsonSerializerOptions Options = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
  };
}
