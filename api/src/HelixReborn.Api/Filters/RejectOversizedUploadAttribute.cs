namespace HelixReborn.Api.Filters;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

/// <summary>
/// Returns a 413 problem response when an upload is larger than <see cref="MaxBytes"/>.
/// </summary>
/// <remarks>
/// <c>[RequestSizeLimit]</c> alone doesn't produce a 413 on an MVC action: form model binding catches the
/// "body too large" error and <c>[ApiController]</c> turns it into a 400. This filter runs before model binding,
/// rejects requests whose <c>Content-Length</c> is over the limit, and reads the form itself so that a chunked body
/// that hits the server's limit also gets a 413. Put it next to <c>[RequestSizeLimit]</c> and
/// <c>[RequestFormLimits]</c> with the same value.
/// </remarks>
/// <param name="maxBytes">The largest request body allowed, in bytes.</param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RejectOversizedUploadAttribute(long maxBytes) : Attribute, IAsyncResourceFilter
{
  /// <summary>Gets the largest request body allowed, in bytes.</summary>
  public long MaxBytes { get; } = maxBytes;

  /// <summary>Rejects oversized requests, otherwise reads the form and runs the rest of the pipeline.</summary>
  /// <param name="context">The resource filter context.</param>
  /// <param name="next">Runs the rest of the pipeline.</param>
  /// <returns>A task that completes when the filter has run.</returns>
  public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
  {
    var httpContext = context.HttpContext;
    var request = httpContext.Request;

    if (request.ContentLength > MaxBytes)
    {
      context.Result = TooLarge(httpContext);
      return;
    }

    if (request.HasFormContentType)
    {
      try
      {
        // Caches the form, so model binding reuses it instead of reading the body again.
        await request.ReadFormAsync(httpContext.RequestAborted);
      }
      catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
      {
        context.Result = TooLarge(httpContext);
        return;
      }
      catch (InvalidDataException)
      {
        // A malformed form: let model binding report it as a 400.
      }
    }

    await next();
  }

  /// <summary>Builds the 413 problem response.</summary>
  /// <param name="httpContext">The current request.</param>
  /// <returns>An <see cref="ObjectResult"/> holding the problem details.</returns>
  private ObjectResult TooLarge(HttpContext httpContext)
  {
    var factory = httpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
    var problem = factory.CreateProblemDetails(
      httpContext,
      StatusCodes.Status413PayloadTooLarge,
      detail: $"The upload is larger than the {MaxBytes / (1024 * 1024)} MB limit.");

    return new ObjectResult(problem) { StatusCode = StatusCodes.Status413PayloadTooLarge };
  }
}
