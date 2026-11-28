namespace HelixReborn.Api.Services.Predictions;

using System.Globalization;
using Artifacts;
using Config;
using CsvHelper;
using Data;
using Datasets;
using Experiments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Models;
using Runs;
using Storage;

/// <summary><see cref="IPredictionService"/> with ONNX Runtime, the object store and the artifact registry.</summary>
/// <param name="db">The app database, for the run.</param>
/// <param name="experiments">Checks the experiment is visible to, or owned by, the caller.</param>
/// <param name="models">The loaded models.</param>
/// <param name="storage">Stores the output CSV.</param>
/// <param name="artifacts">Records the output CSV.</param>
/// <param name="options">The row limit.</param>
public class PredictionService(
  AppDbContext db,
  IExperimentService experiments,
  OnnxModelCache models,
  IObjectStorage storage,
  IArtifactService artifacts,
  IOptions<PredictionOptions> options) : IPredictionService
{
  /// <summary>The artifact <c>sourceType</c> of prediction outputs.</summary>
  public const string SourceType = "prediction";

  /// <summary>The output column holding the prediction.</summary>
  public const string PredictionColumn = "prediction";

  /// <summary>The prefix of each class's probability column, followed by the class label.</summary>
  public const string ProbabilityColumnPrefix = "probability_";

  /// <summary>How many output rows the response previews.</summary>
  public const int PreviewRows = 20;

  /// <summary>How many bad values are reported; the rest are left out.</summary>
  public const int MaxReportedProblems = 10;

  /// <summary>How much of a bad value is quoted back in its problem.</summary>
  private const int MaxQuotedValueLength = 50;

  /// <inheritdoc />
  public async Task<PredictionResponse> PredictAsync(
    Guid experimentId,
    Guid runId,
    string userId,
    string fileName,
    string? contentType,
    Stream content,
    CancellationToken cancellationToken = default)
  {
    await experiments.GetAsync(experimentId, userId, isAdmin: false, cancellationToken);
    var run = await db.Runs.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == runId && x.ExperimentId == experimentId, cancellationToken)
      ?? throw new RunNotFoundException(runId);

    if (run.Status != RunStatus.Succeeded)
    {
      throw new PredictionConflictException("Only a succeeded run's model can make predictions.");
    }

    CheckIsCsv(fileName, contentType);

    using var lease = await models.AcquireAsync(experimentId, runId, cancellationToken);
    var model = lease.Model;
    var input = ReadInput(content, model.Metadata, options.Value.MaxRows);
    var output = Predict(model, input);

    var predictionId = Guid.NewGuid();
    var columns = OutputColumns(model.Metadata, input.Ids is not null);
    var key = StorageKeys.Prediction(experimentId, predictionId);
    long size;
    await using (var file = new FileStream(
      Path.GetTempFileName(),
      FileMode.Create,
      FileAccess.ReadWrite,
      FileShare.None,
      bufferSize: 81920,
      FileOptions.DeleteOnClose | FileOptions.Asynchronous))
    {
      await WriteCsvAsync(file, columns, input, output, model.Metadata, cancellationToken);
      size = file.Length;
      file.Position = 0;
      await storage.PutAsync(key, file, "text/csv", cancellationToken);
    }

    var artifact = await artifacts.RecordAsync(
      experimentId,
      ArtifactKind.Predictions,
      $"Predictions: {Path.GetFileName(fileName)}",
      key,
      "text/csv",
      size,
      SourceType,
      predictionId,
      cancellationToken);

    return new PredictionResponse(
      predictionId,
      runId,
      input.RowCount,
      artifact.Id,
      columns,
      Preview(columns, input, output, model.Metadata));
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<ArtifactResponse>> ListAsync(
    Guid experimentId,
    string userId,
    bool isAdmin,
    CancellationToken cancellationToken = default)
  {
    await experiments.GetAsync(experimentId, userId, isAdmin, cancellationToken);
    var list = await artifacts.ListAsync(experimentId, SourceType, sourceId: null, cancellationToken);
    return [.. list.Select(ArtifactResponse.From)];
  }

  /// <summary>Checks the upload is a CSV by its extension and content type, as dataset uploads are.</summary>
  private static void CheckIsCsv(string fileName, string? contentType)
  {
    List<string> problems = [];
    if (!fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
    {
      problems.Add("The file must have a .csv extension.");
    }

    if (contentType is null ||
        !CsvInspector.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
    {
      problems.Add(
        $"The file's content type must be one of {string.Join(", ", CsvInspector.AllowedContentTypes)}, " +
        $"not '{contentType}'.");
    }

    if (problems.Count > 0)
    {
      throw new PredictionValidationException(problems);
    }
  }

  /// <summary>
  /// Reads the upload into a row-major <c>[N, F]</c> feature matrix in the model's feature order, and the ID column
  /// if the model has one and the upload has it.
  /// </summary>
  private static ModelInput ReadInput(Stream content, ModelMetadata metadata, int maxRows)
  {
    using var csv = CsvReader.Create(content, CsvReaderOptions.Default, leaveOpen: true);
    IReadOnlyList<string> header;
    try
    {
      header = csv.Headers;
    }
    catch (FormatException ex)
    {
      throw new PredictionValidationException([$"The file couldn't be parsed as CSV: {ex.Message}"]);
    }

    if (header.Count == 0)
    {
      throw new PredictionValidationException(["The file has no header row."]);
    }

    var features = metadata.FeatureColumns;
    var featureIndexes = features.Select(name => IndexOf(header, name)).ToArray();
    var missing = features.Where((_, i) => featureIndexes[i] < 0).ToList();
    if (missing.Count > 0)
    {
      throw new PredictionValidationException(
        [$"The file is missing the model's feature column(s): {string.Join(", ", missing)}."]);
    }

    var idIndex = metadata.IdColumn is { } idColumn ? IndexOf(header, idColumn) : -1;
    List<string>? ids = idIndex >= 0 ? [] : null;
    List<float> values = [];
    List<string> problems = [];
    var badValues = 0;
    var rowCount = 0;

    try
    {
      while (csv.Read())
      {
        rowCount++;
        if (rowCount > maxRows)
        {
          throw new PredictionValidationException(
            [$"The file has more than {maxRows:N0} rows; split it into smaller files."]);
        }

        for (var f = 0; f < featureIndexes.Length; f++)
        {
          var index = featureIndexes[f];
          var text = index < csv.FieldCount ? csv.GetString(index) : "";
          if (string.IsNullOrWhiteSpace(text))
          {
            values.Add(float.NaN);
          }
          else if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
                   !float.IsInfinity(value))
          {
            values.Add(value);
          }
          else
          {
            values.Add(float.NaN);
            if (++badValues <= MaxReportedProblems)
            {
              problems.Add($"Row {csv.RowNumber}, column '{features[f]}': '{Quote(text)}' isn't a number.");
            }
          }
        }

        ids?.Add(idIndex < csv.FieldCount ? csv.GetString(idIndex) : "");
      }
    }
    catch (FormatException ex)
    {
      throw new PredictionValidationException([$"The file couldn't be parsed as CSV: {ex.Message}"]);
    }

    if (badValues > MaxReportedProblems)
    {
      problems.Add($"… and {badValues - MaxReportedProblems:N0} more values that aren't numbers.");
    }

    if (rowCount == 0)
    {
      problems.Add("The file must have at least one data row below the header.");
    }

    if (problems.Count > 0)
    {
      throw new PredictionValidationException(problems);
    }

    return new ModelInput(rowCount, [.. values], ids);
  }

  /// <summary>Runs the model, reading its outputs by the names in the metadata.</summary>
  private static ModelOutput Predict(LoadedModel model, ModelInput input)
  {
    var metadata = model.Metadata;
    var outputs = metadata.OnnxOutputs;
    string[] outputNames = metadata.IsClassification ? [outputs.Label!, outputs.Probabilities!] : [outputs.Value!];

    using var tensor = OrtValue.CreateTensorValueFromMemory(
      input.Features, [input.RowCount, metadata.FeatureColumns.Count]);
    using var runOptions = new RunOptions();
    using var results = model.Session.Run(runOptions, [metadata.OnnxInputName], [tensor], outputNames);

    if (!metadata.IsClassification)
    {
      return new ModelOutput(null, results[0].GetTensorDataAsSpan<float>().ToArray(), null);
    }

    var classes = metadata.Classes!;
    var labels = results[0].GetTensorDataAsSpan<long>().ToArray();
    var probabilities = results[1].GetTensorDataAsSpan<float>().ToArray();
    if (labels.Length != input.RowCount || probabilities.Length != input.RowCount * classes.Count)
    {
      throw new PredictionConflictException(
        $"The model's outputs don't match its metadata: {labels.Length} labels and {probabilities.Length} " +
        $"probabilities for {input.RowCount} rows and {classes.Count} classes.");
    }

    var labelNames = new string[labels.Length];
    for (var i = 0; i < labels.Length; i++)
    {
      labelNames[i] = labels[i] >= 0 && labels[i] < classes.Count
        ? classes[(int)labels[i]]
        : throw new PredictionConflictException(
          $"The model predicted class index {labels[i]}, but its metadata has {classes.Count} classes.");
    }

    return new ModelOutput(labelNames, null, probabilities);
  }

  /// <summary>The output columns: ID (if passed through), <c>prediction</c>, then per-class probabilities.</summary>
  private static string[] OutputColumns(ModelMetadata metadata, bool hasIds)
  {
    List<string> columns = [];
    if (hasIds)
    {
      columns.Add(metadata.IdColumn!);
    }

    columns.Add(PredictionColumn);
    if (metadata.IsClassification)
    {
      columns.AddRange(metadata.Classes!.Select(c => ProbabilityColumnPrefix + c));
    }

    return [.. columns];
  }

  /// <summary>Writes the output CSV. Numbers use the invariant culture and round-trip (<c>R</c>) format.</summary>
  private static async Task WriteCsvAsync(
    Stream destination,
    string[] columns,
    ModelInput input,
    ModelOutput output,
    ModelMetadata metadata,
    CancellationToken cancellationToken)
  {
    await using var csv = CsvWriter.Create(destination, CsvWriterOptions.Default, leaveOpen: true);
    csv.WriteHeader(columns);

    var classCount = metadata.Classes?.Count ?? 0;
    for (var row = 0; row < input.RowCount; row++)
    {
      if (input.Ids is { } ids)
      {
        csv.WriteField(ids[row]);
      }

      if (output.Labels is { } labels)
      {
        csv.WriteField(labels[row]);
        for (var k = 0; k < classCount; k++)
        {
          csv.WriteField(Format(output.Probabilities![(row * classCount) + k]));
        }
      }
      else
      {
        csv.WriteField(Format(output.Values![row]));
      }

      await csv.EndRecordAsync(cancellationToken);
    }

    await csv.FlushAsync(cancellationToken);
  }

  /// <summary>The first <see cref="PreviewRows"/> output rows, keyed by column.</summary>
  private static List<IReadOnlyDictionary<string, object?>> Preview(
    string[] columns,
    ModelInput input,
    ModelOutput output,
    ModelMetadata metadata)
  {
    var classCount = metadata.Classes?.Count ?? 0;
    var probabilityColumns = columns[^classCount..];
    List<IReadOnlyDictionary<string, object?>> rows = [];
    for (var row = 0; row < Math.Min(PreviewRows, input.RowCount); row++)
    {
      var values = new Dictionary<string, object?>(StringComparer.Ordinal);
      if (input.Ids is { } ids)
      {
        values[columns[0]] = ids[row];
      }

      if (output.Labels is { } labels)
      {
        values[PredictionColumn] = labels[row];
        for (var k = 0; k < classCount; k++)
        {
          values[probabilityColumns[k]] = Finite(output.Probabilities![(row * classCount) + k]);
        }
      }
      else
      {
        values[PredictionColumn] = Finite(output.Values![row]);
      }

      rows.Add(values);
    }

    return rows;
  }

  private static int IndexOf(IReadOnlyList<string> header, string name)
  {
    for (var i = 0; i < header.Count; i++)
    {
      if (string.Equals(header[i], name, StringComparison.Ordinal))
      {
        return i;
      }
    }

    return -1;
  }

  private static string Format(float value) => value.ToString("R", CultureInfo.InvariantCulture);

  /// <summary>JSON has no NaN or infinity: a model without an imputer gives NaN for rows with blanks.</summary>
  private static float? Finite(float value) => float.IsFinite(value) ? value : null;

  private static string Quote(string value) =>
    value.Length <= MaxQuotedValueLength ? value : value[..MaxQuotedValueLength] + "…";

  /// <summary>The model's input.</summary>
  /// <param name="RowCount">Number of rows, N.</param>
  /// <param name="Features">The row-major <c>[N, F]</c> feature matrix; blanks are <c>NaN</c>.</param>
  /// <param name="Ids">Each row's ID, if passed through.</param>
  private sealed record ModelInput(int RowCount, float[] Features, List<string>? Ids);

  /// <summary>The model's output: class labels and row-major <c>[N, K]</c> probabilities, or values.</summary>
  /// <param name="Labels">Each row's class label (classification).</param>
  /// <param name="Values">Each row's value (regression).</param>
  /// <param name="Probabilities">Row-major <c>[N, K]</c> class probabilities (classification).</param>
  private sealed record ModelOutput(string[]? Labels, float[]? Values, float[]? Probabilities);
}
