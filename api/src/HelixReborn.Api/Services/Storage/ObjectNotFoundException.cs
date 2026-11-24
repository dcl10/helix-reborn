namespace HelixReborn.Api.Services.Storage;

/// <summary>No object exists at the requested key.</summary>
/// <param name="key">The key that was requested.</param>
public class ObjectNotFoundException(string key) : Exception($"No object exists at key '{key}'.");
