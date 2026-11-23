namespace HelixReborn.Api.Tests;

using System.Text;
using Services.Datasets;
using Xunit;

public class CsvInspectorTests
{
  [Fact]
  public void Inspect_InfersNumericColumn_WhenEveryValueParses()
  {
    var result = Inspect(["id", "age"], Rows(10, i => [$"{i}", $"{20 + i}"]));

    var age = Assert.Single(result.Columns, x => x.Name == "age");
    Assert.Equal("numeric", age.InferredType);
    Assert.Equal(0, age.MissingCount);
  }

  [Fact]
  public void Inspect_InfersTextColumn_WhenAnyValueDoesNotParseAsNumber()
  {
    var result = Inspect(["id", "label"], Rows(10, i => [$"{i}", i == 3 ? "not-a-number" : "42"]));

    var label = Assert.Single(result.Columns, x => x.Name == "label");
    Assert.Equal("text", label.InferredType);
  }

  [Fact]
  public void Inspect_InfersEmptyColumn_WhenEveryValueIsBlank()
  {
    var result = Inspect(["id", "unused"], Rows(10, i => [$"{i}", ""]));

    var unused = Assert.Single(result.Columns, x => x.Name == "unused");
    Assert.Equal("empty", unused.InferredType);
    Assert.Equal(10, unused.MissingCount);
  }

  [Fact]
  public void Inspect_InfersNumericColumn_WhenMixedWithBlanks()
  {
    var result = Inspect(["id", "score"], Rows(10, i => [$"{i}", i == 5 ? "" : "1.5"]));

    var score = Assert.Single(result.Columns, x => x.Name == "score");
    Assert.Equal("numeric", score.InferredType);
    Assert.Equal(1, score.MissingCount);
  }

  [Fact]
  public void Inspect_CountsMissingValues_PerColumn()
  {
    var result = Inspect(["id", "note"], Rows(10, i => [$"{i}", i < 4 ? "" : "x"]));

    var note = Assert.Single(result.Columns, x => x.Name == "note");
    Assert.Equal(4, note.MissingCount);
  }

  [Fact]
  public void Inspect_HandlesQuotedFieldsContainingCommas()
  {
    var header = "id,description";
    var rows = Enumerable.Range(0, 10).Select(i => $"{i},\"Hello, world {i}\"");
    var csv = string.Join("\n", [header, .. rows]);

    var result = CsvInspector.Inspect(ToStream(csv), "data.csv", "text/csv");

    var description = Assert.Single(result.Columns, x => x.Name == "description");
    Assert.Equal("text", description.InferredType);
    Assert.Equal(10, result.RowCount);
  }

  private static CsvInspectionResult Inspect(string[] header, IEnumerable<string[]> rows)
  {
    var lines = new List<string> { string.Join(",", header) };
    lines.AddRange(rows.Select(row => string.Join(",", row)));

    return CsvInspector.Inspect(ToStream(string.Join("\n", lines)), "data.csv", "text/csv");
  }

  private static IEnumerable<string[]> Rows(int count, Func<int, string[]> row) =>
    Enumerable.Range(0, count).Select(row);

  private static MemoryStream ToStream(string csv) => new(Encoding.UTF8.GetBytes(csv));
}
