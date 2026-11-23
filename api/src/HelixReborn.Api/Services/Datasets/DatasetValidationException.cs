namespace HelixReborn.Api.Services.Datasets;

/// <summary>The uploaded file isn't an acceptable dataset. The API answers 400 with every problem listed.</summary>
/// <param name="problems">What's wrong with the file, in words a user can act on.</param>
public class DatasetValidationException(IReadOnlyList<string> problems) : Exception(string.Join(" ", problems))
{
  /// <summary>Every problem found with the file.</summary>
  public IReadOnlyList<string> Problems { get; } = problems;
}
