namespace HelixReborn.Api.Messaging;

using Config;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

/// <summary>
/// The API's one long-lived RabbitMQ connection, opened lazily and thread-safely on first use, and opened again on the
/// next use after it drops. Automatic recovery is off: <see cref="JobEventConsumer"/> runs its own reconnect loop, and
/// letting the client also recover would leave two consumers. The topology is declared on every new connection.
/// </summary>
/// <param name="options">The broker URL.</param>
public sealed class RabbitMqConnection(IOptions<RabbitMqOptions> options) : IAsyncDisposable
{
  private readonly SemaphoreSlim _lock = new(1, 1);
  private IConnection? _connection;

  /// <summary>Returns the open connection, connecting (and declaring the topology) if there isn't one.</summary>
  /// <param name="cancellationToken">Cancels connecting.</param>
  /// <returns>An open connection.</returns>
  /// <exception cref="RabbitMQ.Client.Exceptions.BrokerUnreachableException">The broker can't be reached.</exception>
  public async Task<IConnection> GetAsync(CancellationToken cancellationToken)
  {
    if (_connection is { IsOpen: true } open)
    {
      return open;
    }

    await _lock.WaitAsync(cancellationToken);
    try
    {
      if (_connection is { IsOpen: true } current)
      {
        return current;
      }

      if (_connection is not null)
      {
        await _connection.DisposeAsync();
      }

      var factory = new ConnectionFactory
      {
        Uri = new Uri(options.Value.Url),
        ClientProvidedName = "helix-api",
        AutomaticRecoveryEnabled = false,
        TopologyRecoveryEnabled = false,
        RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
      };
      var connection = await factory.CreateConnectionAsync(cancellationToken);

      await using (var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken))
      {
        await Queues.DeclareTopologyAsync(channel, cancellationToken);
      }

      _connection = connection;
      return connection;
    }
    finally
    {
      _lock.Release();
    }
  }

  /// <inheritdoc />
  public async ValueTask DisposeAsync()
  {
    if (_connection is not null)
    {
      await _connection.DisposeAsync();
    }

    _lock.Dispose();
  }
}
