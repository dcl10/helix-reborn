namespace HelixReborn.Api.Config;

using System.ComponentModel.DataAnnotations;

/// <summary>How invitation emails are sent: where their link returns to, and for how long it works.</summary>
public class InvitationOptions
{
  /// <summary>Configuration section these options bind to.</summary>
  public const string SectionName = "Invitations";

  /// <summary>
  /// The web app's public URL, e.g. <c>http://localhost:3000</c>. Keycloak sends the invited user here once they
  /// have verified their email and set a password, so it must match one of <c>helix-web</c>'s valid redirect URIs.
  /// </summary>
  [Required]
  [Url]
  public string AppUrl { get; init; } = "";

  /// <summary>
  /// The Keycloak client the action link belongs to, <c>helix-web</c>. Keycloak checks <see cref="AppUrl"/> against
  /// this client's redirect URIs.
  /// </summary>
  [Required]
  public string ClientId { get; init; } = "helix-web";

  /// <summary>How long the emailed link works, in days.</summary>
  [Range(1, 30)]
  public int ExpiryDays { get; init; } = 7;
}
