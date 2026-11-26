namespace HelixReborn.Api.Services.Eda;

/// <summary>
/// An EDA service call failed. <see cref="StatusCode"/> says how: <c>422</c> EDA rejected the request (bad column,
/// bad option; <see cref="Exception.Message"/> is EDA's detail), <c>404</c> EDA couldn't find the dataset in S3,
/// <c>502</c> anything else (EDA unreachable, timed out, refused the token, or failed).
/// </summary>
/// <param name="statusCode">422, 404 or 502.</param>
/// <param name="message">What failed; EDA's own detail for 422 and 404.</param>
/// <param name="innerException">The underlying error, if any.</param>
public class EdaException(int statusCode, string message, Exception? innerException = null)
  : Exception(message, innerException)
{
  /// <summary>EDA rejected the request.</summary>
  public const int Rejected = 422;

  /// <summary>EDA couldn't find the dataset.</summary>
  public const int DatasetNotFound = 404;

  /// <summary>Anything else went wrong.</summary>
  public const int Failed = 502;

  /// <summary>422, 404 or 502; see the class summary.</summary>
  public int StatusCode { get; } = statusCode;
}
