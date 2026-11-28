namespace HelixReborn.Api.Filters;

using Config;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

/// <summary>
/// Applies <see cref="PredictionOptions.MaxUploadBytes"/> to a prediction upload, as <c>[RequestSizeLimit]</c>,
/// <c>[RequestFormLimits]</c> and <see cref="RejectOversizedUploadAttribute"/> do for a constant limit: the server's
/// body limit and the form's multipart limit are set to it, and anything larger gets a 413. Apply with
/// <c>[ServiceFilter(typeof(PredictionUploadLimitFilter))]</c>.
/// </summary>
/// <param name="options">The upload limit.</param>
public sealed class PredictionUploadLimitFilter(IOptions<PredictionOptions> options) : IAsyncResourceFilter
{
  /// <summary>Sets the limits, then rejects an oversized request or reads the form and runs the rest.</summary>
  /// <param name="context">The resource filter context.</param>
  /// <param name="next">Runs the rest of the pipeline.</param>
  /// <returns>A task that completes when the filter has run.</returns>
  public Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
  {
    var maxBytes = options.Value.MaxUploadBytes;
    var features = context.HttpContext.Features;

    if (features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySize)
    {
      bodySize.MaxRequestBodySize = maxBytes;
    }

    if (features.Get<IFormFeature>() is not { Form: not null })
    {
      features.Set<IFormFeature>(new FormFeature(
        context.HttpContext.Request,
        new FormOptions { MultipartBodyLengthLimit = maxBytes }));
    }

    return RejectOversizedUploadAttribute.RejectOrContinueAsync(context, next, maxBytes);
  }
}
