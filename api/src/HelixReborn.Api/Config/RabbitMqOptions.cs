namespace HelixReborn.Api.Config;

using System.ComponentModel.DataAnnotations;

/// <summary>Where the API finds RabbitMQ, which carries training and evaluation jobs and their events.</summary>
public class RabbitMqOptions : IValidatableObject
{
  /// <summary>Configuration section these options bind to.</summary>
  public const string SectionName = "RabbitMq";

  /// <summary>
  /// The AMQP URL (<c>amqp://</c> or <c>amqps://</c>), credentials included. Defaults to the local Compose broker; set
  /// <c>RabbitMq__Url</c> everywhere else. Secret outside local development.
  /// </summary>
  [Required]
  public string Url { get; init; } = "amqp://helix:dev_password@localhost:5672/";

  /// <summary>Checks <see cref="Url"/> is an absolute <c>amqp</c> or <c>amqps</c> URL.</summary>
  /// <param name="validationContext">Unused.</param>
  /// <returns>The validation errors, if any.</returns>
  public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
  {
    if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("amqp" or "amqps"))
    {
      yield return new ValidationResult("Url must be an absolute amqp:// or amqps:// URL.", [nameof(Url)]);
    }
  }
}
