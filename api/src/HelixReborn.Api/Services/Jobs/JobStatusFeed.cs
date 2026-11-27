namespace HelixReborn.Api.Services.Jobs;

using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;

/// <summary>
/// Following a job's entity (a run or an evaluation) as it changes: polling it, and turning its states into the
/// server-sent events the API streams (<c>status</c> with the state, then <c>end</c> once it's terminal).
/// </summary>
public static class JobStatusFeed
{
  /// <summary>How often <see cref="Poll{T}"/> reloads the entity.</summary>
  public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

  private static readonly JsonSerializerOptions EventJson = new(JsonSerializerDefaults.Web);

  /// <summary>
  /// Yields <paramref name="first"/>, then the entity again whenever its fingerprint changes, polled every
  /// <see cref="PollInterval"/> in a fresh DI scope (the stream can stay open for hours; a <c>DbContext</c> mustn't).
  /// Finishes once the entity is terminal or gone.
  /// </summary>
  /// <typeparam name="T">The entity's response type.</typeparam>
  /// <param name="first">The entity's current state, already loaded (and access-checked) by the caller.</param>
  /// <param name="load">Reloads the entity from a poll's scope; null when it's gone.</param>
  /// <param name="isTerminal">Whether a state is final.</param>
  /// <param name="fingerprint">What a state is compared by; a new fingerprint is yielded.</param>
  /// <param name="scopes">Creates a scope per poll.</param>
  /// <param name="time">The clock the polls wait on.</param>
  /// <param name="cancellationToken">Stops the stream.</param>
  /// <returns>The entity's successive states.</returns>
  public static async IAsyncEnumerable<T> Poll<T>(
    T first,
    Func<IServiceProvider, CancellationToken, Task<T?>> load,
    Func<T, bool> isTerminal,
    Func<T, string> fingerprint,
    IServiceScopeFactory scopes,
    TimeProvider time,
    [EnumeratorCancellation] CancellationToken cancellationToken)
    where T : class
  {
    yield return first;
    var last = fingerprint(first);
    var terminal = isTerminal(first);

    while (!terminal)
    {
      await Task.Delay(PollInterval, time, cancellationToken);

      await using var scope = scopes.CreateAsyncScope();
      var current = await load(scope.ServiceProvider, cancellationToken);
      if (current is null)
      {
        yield break;
      }

      var currentFingerprint = fingerprint(current);
      if (currentFingerprint != last)
      {
        last = currentFingerprint;
        yield return current;
      }

      terminal = isTerminal(current);
    }
  }

  /// <summary>
  /// Turns states into server-sent events: <c>status</c> with each state (camelCase JSON), then, if the last state is
  /// terminal, <c>end</c> with <c>{"status"}</c>.
  /// </summary>
  /// <typeparam name="T">The entity's response type.</typeparam>
  /// <param name="states">The states, e.g. from <see cref="Poll{T}"/>.</param>
  /// <param name="isTerminal">Whether a state is final.</param>
  /// <param name="status">A state's status, for the <c>end</c> event.</param>
  /// <param name="cancellationToken">Stops the stream.</param>
  /// <returns>The events.</returns>
  public static async IAsyncEnumerable<SseItem<string>> ToServerSentEvents<T>(
    IAsyncEnumerable<T> states,
    Func<T, bool> isTerminal,
    Func<T, object> status,
    [EnumeratorCancellation] CancellationToken cancellationToken)
    where T : class
  {
    T? last = null;
    await foreach (var state in states.WithCancellation(cancellationToken))
    {
      last = state;
      yield return new SseItem<string>(JsonSerializer.Serialize(state, EventJson), "status");
    }

    if (last is not null && isTerminal(last))
    {
      yield return new SseItem<string>(JsonSerializer.Serialize(new { status = status(last) }, EventJson), "end");
    }
  }
}
