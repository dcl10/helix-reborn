namespace HelixReborn.Api.Tests;

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Fixtures;
using Microsoft.AspNetCore.Mvc;
using Models;
using Services.Storage;
using Xunit;

public class PredictionsControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  private readonly HttpClient _user = factory.CreateUserClient("user-predictions");

  [Fact]
  public async Task Predict_MatchesTheOnnxModel_ForClassification()
  {
    var (experiment, run) = await PredictionSetup.RunWithModelAsync(factory, _user, PredictionSetup.Classification);
    var expected = PredictionSetup.ReadExpected(PredictionSetup.Classification);
    // Columns out of order, plus one the model doesn't use: features are mapped by name, extras ignored.
    var csv = new StringBuilder("f2,extra,sample_id,f1,f0\n");
    for (var i = 0; i < 3; i++)
    {
      var row = expected.Rows[i];
      csv.AppendJoin(',', PredictionSetup.Cell(row[2]), "x", expected.Ids![i], PredictionSetup.Cell(row[1]),
        PredictionSetup.Cell(row[0])).Append('\n');
    }

    var response = await PredictionSetup.PostAsync(_user, experiment.Id, run.Id, csv.ToString());

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    var prediction = (await response.Content.ReadFromJsonAsync<PredictionResponse>())!;
    Assert.Equal(run.Id, prediction.RunId);
    Assert.Equal(3, prediction.RowCount);
    Assert.Equal(["sample_id", "prediction", "probability_no", "probability_yes"], prediction.Columns);
    Assert.Equal(
      $"/api/experiments/{experiment.Id}/artifacts/{prediction.ArtifactId}/content",
      response.Headers.Location?.OriginalString);
    for (var i = 0; i < 3; i++)
    {
      var row = prediction.Preview[i];
      Assert.Equal(expected.Ids![i], ((JsonElement)row["sample_id"]!).GetString());
      Assert.Equal(expected.Labels![i], ((JsonElement)row["prediction"]!).GetString());
      Assert.Equal(expected.Probabilities![i][0], ((JsonElement)row["probability_no"]!).GetDouble(), 1e-5);
      Assert.Equal(expected.Probabilities![i][1], ((JsonElement)row["probability_yes"]!).GetDouble(), 1e-5);
    }

    // The stored CSV has the same columns and values, probabilities in round-trip format.
    var stored = StoredCsv(experiment.Id, prediction.PredictionId);
    Assert.Equal("sample_id,prediction,probability_no,probability_yes", stored[0]);
    Assert.Equal(4, stored.Length);
    var first = stored[1].Split(',');
    Assert.Equal(expected.Ids![0], first[0]);
    Assert.Equal(expected.Labels![0], first[1]);
    Assert.Equal(expected.Probabilities![0][1], double.Parse(first[3], CultureInfo.InvariantCulture), 1e-5);
  }

  [Fact]
  public async Task Predict_MatchesTheOnnxModel_ForRegression_WithABlankValue()
  {
    var (experiment, run) = await PredictionSetup.RunWithModelAsync(factory, _user, PredictionSetup.Regression);
    var expected = PredictionSetup.ReadExpected(PredictionSetup.Regression);
    Assert.Null(expected.Rows[1][1]); // the fixture's second row has a blank, which the imputer fills
    var csv = new StringBuilder("f0,f1,f2\n");
    foreach (var row in expected.Rows)
    {
      csv.AppendJoin(',', row.Select(PredictionSetup.Cell)).Append('\n');
    }

    var prediction = await PredictionSetup.PredictAsync(_user, experiment.Id, run.Id, csv.ToString());

    Assert.Equal(["prediction"], prediction.Columns);
    for (var i = 0; i < 3; i++)
    {
      Assert.Equal(expected.Values![i], ((JsonElement)prediction.Preview[i]["prediction"]!).GetDouble(), 1e-4);
    }

    var stored = StoredCsv(experiment.Id, prediction.PredictionId);
    Assert.Equal("prediction", stored[0]);
    Assert.Equal(expected.Values![1], double.Parse(stored[2], CultureInfo.InvariantCulture), 1e-4);
  }

  [Fact]
  public async Task Predict_LeavesOutTheIdColumn_WhenTheUploadHasNone()
  {
    var (experiment, run) = await PredictionSetup.RunWithModelAsync(factory, _user, PredictionSetup.Classification);

    var prediction = await PredictionSetup.PredictAsync(_user, experiment.Id, run.Id, "f0,f1,f2\n0.5,-1,0.2\n");

    Assert.Equal(["prediction", "probability_no", "probability_yes"], prediction.Columns);
    Assert.False(prediction.Preview[0].ContainsKey("sample_id"));
  }

  [Fact]
  public async Task Predict_PreviewsOnlyTheFirst20Rows()
  {
    var (experiment, run) = await PredictionSetup.RunWithModelAsync(factory, _user, PredictionSetup.Regression);
    var csv = "f0,f1,f2\n" + string.Concat(Enumerable.Repeat("1,2,3\n", 25));

    var prediction = await PredictionSetup.PredictAsync(_user, experiment.Id, run.Id, csv);

    Assert.Equal(25, prediction.RowCount);
    Assert.Equal(20, prediction.Preview.Count);
    Assert.Equal(26, StoredCsv(experiment.Id, prediction.PredictionId).Length);
  }

  [Fact]
  public async Task Predict_RecordsTheArtifact_AndListsIt()
  {
    var (experiment, run) = await PredictionSetup.RunWithModelAsync(factory, _user, PredictionSetup.Regression);

    var prediction = await PredictionSetup.PredictAsync(_user, experiment.Id, run.Id, "f0,f1,f2\n1,2,3\n");

    var key = StorageKeys.Prediction(experiment.Id, prediction.PredictionId);
    Assert.Equal($"experiments/{experiment.Id:D}/predictions/{prediction.PredictionId:D}/predictions.csv", key);
    var stored = factory.ObjectStorage.Get(key);
    Assert.NotNull(stored);
    Assert.Equal("text/csv", stored.Value.ContentType);

    var artifact = Assert.Single(
      (await _user.GetFromJsonAsync<List<ArtifactResponse>>($"/api/experiments/{experiment.Id}/predictions"))!);
    Assert.Equal(prediction.ArtifactId, artifact.Id);
    Assert.Equal(ArtifactKind.Predictions, artifact.Kind);
    Assert.Equal("Predictions: new.csv", artifact.Title);
    Assert.Equal("text/csv", artifact.ContentType);
    Assert.Equal("prediction", artifact.SourceType);
    Assert.Equal(prediction.PredictionId, artifact.SourceId);
    Assert.Equal(stored.Value.Content.Length, artifact.SizeBytes);
    Assert.Equal("predictions.csv", artifact.FileName);

    var content = await _user.GetAsync($"/api/experiments/{experiment.Id}/artifacts/{artifact.Id}/content");
    Assert.Equal(HttpStatusCode.OK, content.StatusCode);
  }

  [Fact]
  public async Task Predict_Returns400_NamingTheMissingFeatureColumns()
  {
    var (experiment, run) = await PredictionSetup.RunWithModelAsync(factory, _user, PredictionSetup.Classification);

    var response = await PredictionSetup.PostAsync(_user, experiment.Id, run.Id, "f0,other\n1,2\n");

    var problems = await FileProblemsAsync(response);
    Assert.Contains(problems, p => p.Contains("f1, f2", StringComparison.Ordinal));
  }

  [Fact]
  public async Task Predict_Returns400_WithTheRowColumnAndValue_ForANonNumericValue()
  {
    var (experiment, run) = await PredictionSetup.RunWithModelAsync(factory, _user, PredictionSetup.Classification);

    var response = await PredictionSetup.PostAsync(_user, experiment.Id, run.Id, "f0,f1,f2\n1,2,3\n4,abc,6\n");

    var problem = Assert.Single(await FileProblemsAsync(response));
    Assert.Equal("Row 2, column 'f1': 'abc' isn't a number.", problem);
  }

  [Fact]
  public async Task Predict_ReportsOnlyTheFirst10BadValues()
  {
    var (experiment, run) = await PredictionSetup.RunWithModelAsync(factory, _user, PredictionSetup.Classification);
    var csv = "f0,f1,f2\n" + string.Concat(Enumerable.Repeat("x,1,1\n", 12));

    var problems = await FileProblemsAsync(await PredictionSetup.PostAsync(_user, experiment.Id, run.Id, csv));

    Assert.Equal(11, problems.Length);
    Assert.StartsWith("Row 10, column 'f0'", problems[9], StringComparison.Ordinal);
    Assert.Equal("… and 2 more values that aren't numbers.", problems[10]);
  }

  [Fact]
  public async Task Predict_Returns400_WhenOverMaxRows()
  {
    var (experiment, run) = await PredictionSetup.RunWithModelAsync(factory, _user, PredictionSetup.Regression);
    var csv = "f0,f1,f2\n" + string.Concat(Enumerable.Repeat("1,2,3\n", 100_001));

    var problems = await FileProblemsAsync(await PredictionSetup.PostAsync(_user, experiment.Id, run.Id, csv));

    Assert.Contains("more than 100,000 rows", Assert.Single(problems), StringComparison.Ordinal);
  }

  [Theory]
  [InlineData("f0,f1,f2\n", "text/csv", "new.csv", "at least one data row")]
  [InlineData("f0,f1,f2\n1,2,3\n", "text/csv", "new.txt", ".csv extension")]
  [InlineData("f0,f1,f2\n1,2,3\n", "image/png", "new.csv", "content type")]
  public async Task Predict_Returns400_WhenTheFileIsntAUsableCsv(
    string csv,
    string contentType,
    string fileName,
    string problem)
  {
    var (experiment, run) = await PredictionSetup.RunWithModelAsync(factory, _user, PredictionSetup.Regression);

    var response = await PredictionSetup.PostAsync(_user, experiment.Id, run.Id, csv, fileName, contentType);

    Assert.Contains(await FileProblemsAsync(response), p => p.Contains(problem, StringComparison.Ordinal));
  }

  [Fact]
  public async Task Predict_Returns413_WhenOverMaxUploadBytes()
  {
    var (experiment, run) = await PredictionSetup.RunWithModelAsync(factory, _user, PredictionSetup.Regression);

    var response = await PredictionSetup.PostBytesAsync(_user, experiment.Id, run.Id, new byte[(10 * 1024 * 1024) + 1]);

    Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
  }

  [Fact]
  public async Task Predict_Returns409_WhenTheRunHasntSucceeded()
  {
    var (experiment, _, run) = await EvaluationSetup.QueuedRunAsync(_user);

    var response = await PredictionSetup.PostAsync(_user, experiment.Id, run.Id, "f0,f1,f2\n1,2,3\n");

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
  }

  [Fact]
  public async Task Predict_Returns409_WhenTheModelFilesArentInStorage()
  {
    var (experiment, _, run) = await EvaluationSetup.SucceededRunAsync(factory, _user);

    var response = await PredictionSetup.PostAsync(_user, experiment.Id, run.Id, "f0,f1,f2\n1,2,3\n");

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
  }

  [Fact]
  public async Task Predict_Returns404_ForAnUnknownRun()
  {
    var (experiment, _) = await PredictionSetup.RunWithModelAsync(factory, _user, PredictionSetup.Regression);

    var response = await PredictionSetup.PostAsync(_user, experiment.Id, Guid.NewGuid(), "f0,f1,f2\n1,2,3\n");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task AnotherUsersExperiment_Returns404()
  {
    var other = factory.CreateUserClient("user-predictions-other");
    var (experiment, run) = await PredictionSetup.RunWithModelAsync(factory, other, PredictionSetup.Regression);

    var post = await PredictionSetup.PostAsync(_user, experiment.Id, run.Id, "f0,f1,f2\n1,2,3\n");
    var list = await _user.GetAsync($"/api/experiments/{experiment.Id}/predictions");

    Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
  }

  [Fact]
  public async Task Admin_CanListPredictions_ButNotPredict()
  {
    var (experiment, run) = await PredictionSetup.RunWithModelAsync(factory, _user, PredictionSetup.Regression);
    await PredictionSetup.PredictAsync(_user, experiment.Id, run.Id, "f0,f1,f2\n1,2,3\n");
    var admin = factory.CreateAdminClient();

    var list = await admin.GetFromJsonAsync<List<ArtifactResponse>>($"/api/experiments/{experiment.Id}/predictions");
    var post = await PredictionSetup.PostAsync(admin, experiment.Id, run.Id, "f0,f1,f2\n1,2,3\n");

    Assert.Single(list!);
    Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
  }

  [Fact]
  public async Task Predict_DoesntReadTheModelAgain_ForTheSameRun()
  {
    var (experiment, run) = await PredictionSetup.RunWithModelAsync(factory, _user, PredictionSetup.Regression);
    var prefix = StorageKeys.RunPrefix(experiment.Id, run.Id);

    await PredictionSetup.PredictAsync(_user, experiment.Id, run.Id, "f0,f1,f2\n1,2,3\n");
    await PredictionSetup.PredictAsync(_user, experiment.Id, run.Id, "f0,f1,f2\n4,5,6\n");

    Assert.Equal(1, factory.ObjectStorage.ReadCount(prefix + "model.onnx"));
    Assert.Equal(1, factory.ObjectStorage.ReadCount(prefix + "metadata.json"));
  }

  private string[] StoredCsv(Guid experimentId, Guid predictionId)
  {
    var stored = factory.ObjectStorage.Get(StorageKeys.Prediction(experimentId, predictionId));
    Assert.NotNull(stored);
    return Encoding.UTF8.GetString(stored.Value.Content).Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
  }

  private static async Task<string[]> FileProblemsAsync(HttpResponseMessage response)
  {
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;
    return problem.Errors["file"];
  }
}
