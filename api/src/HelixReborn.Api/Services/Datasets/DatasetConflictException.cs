namespace HelixReborn.Api.Services.Datasets;

/// <summary>The experiment already has a dataset; only one is allowed. The API answers 409.</summary>
/// <param name="message">Why, in words a user can act on.</param>
public class DatasetConflictException(string message) : Exception(message);
