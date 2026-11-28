namespace HelixReborn.Api.Services.Predictions;

/// <summary>
/// The upload can't be predicted on: not a CSV, missing columns, bad values or too many rows. The API answers 400.
/// </summary>
/// <param name="problems">What's wrong, for the user; at most the first 10 value problems.</param>
public class PredictionValidationException(IReadOnlyList<string> problems)
  : Exception(string.Join(" ", problems))
{
  /// <summary>What's wrong, for the user.</summary>
  public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>
/// The run can't predict: it hasn't succeeded, or its model files are missing or unusable. The API answers 409.
/// </summary>
/// <param name="message">Why.</param>
/// <param name="innerException">The underlying error, if any.</param>
public class PredictionConflictException(string message, Exception? innerException = null)
  : Exception(message, innerException);
