using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace ECommerce.EventBus.RabbitMQ;

/// <summary>
/// Owns the single long-lived AMQP connection for this process and re-establishes it when it drops.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pattern:</b> Retry with exponential backoff and jitter (Polly). See <c>docs/concept-map.md</c>.
/// </para>
/// <para>
/// <b>One connection, many channels.</b> An AMQP connection is a TCP connection and is expensive to create;
/// channels are cheap, multiplexed logical sessions over it. Opening a connection per publish is the classic
/// RabbitMQ performance mistake — it exhausts sockets and adds a full TCP plus AMQP handshake to every message.
/// So: one connection per process, one channel per publisher or consumer. <b>Channels are not thread-safe</b>,
/// which is why they are never shared across concurrent operations.
/// </para>
/// </remarks>
public interface IRabbitMqConnection : IAsyncDisposable
{
    bool IsConnected { get; }

    Task<bool> TryConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens a new channel, connecting first if necessary.</summary>
    Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IRabbitMqConnection"/>
public sealed class RabbitMqConnection : IRabbitMqConnection
{
    private readonly ConnectionFactory _connectionFactory;
    private readonly ILogger<RabbitMqConnection> _logger;
    private readonly ResiliencePipeline _connectPipeline;
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    private IConnection? _connection;
    private bool _disposed;

    public RabbitMqConnection(IOptions<RabbitMqOptions> options, ILogger<RabbitMqConnection> logger)
    {
        _logger = logger;
        RabbitMqOptions value = options.Value;

        _connectionFactory = new ConnectionFactory
        {
            HostName = value.HostName,
            Port = value.Port,
            UserName = value.UserName,
            Password = value.Password,
            VirtualHost = value.VirtualHost,
            ClientProvidedName = value.SubscriptionClientName,

            // The client's own recovery handles blips; our Polly pipeline handles the broker being absent
            // entirely at startup, which recovery alone does not cover.
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
        };

        _connectPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<BrokerUnreachableException>()
                    .Handle<SocketException>(),
                MaxRetryAttempts = value.ConnectionRetryCount,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(1),

                // Jitter matters more than it looks. Without it, every service that started at the same time
                // retries at exactly the same instants, so they arrive as a synchronised thundering herd against
                // a broker that is already struggling. Jitter spreads the retries out.
                UseJitter = true,
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        args.Outcome.Exception,
                        "RabbitMQ connection attempt {Attempt} failed; retrying in {Delay}.",
                        args.AttemptNumber + 1,
                        args.RetryDelay);
                    return ValueTask.CompletedTask;
                },
            })
            .Build();
    }

    public bool IsConnected => _connection is { IsOpen: true } && !_disposed;

    public async Task<bool> TryConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            return true;
        }

        // Serialise connection attempts: without this, N consumers starting concurrently each open their own
        // connection, and all but one are leaked.
        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected)
            {
                return true;
            }

            await _connectPipeline.ExecuteAsync(
                async ct => _connection = await _connectionFactory.CreateConnectionAsync(ct),
                cancellationToken);

            if (!IsConnected)
            {
                _logger.LogCritical("RabbitMQ connection could not be established and all retries are exhausted.");
                return false;
            }

            _connection!.ConnectionShutdownAsync += OnConnectionShutdownAsync;
            _logger.LogInformation(
                "RabbitMQ connection established to {HostName}.",
                _connection.Endpoint.HostName);

            return true;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected && !await TryConnectAsync(cancellationToken))
        {
            throw new InvalidOperationException("No RabbitMQ connection is available to create a channel on.");
        }

        // Publisher confirms: the broker acknowledges that it has taken responsibility for the message. The
        // outbox dispatcher marks a row processed only after this, so a message lost in flight is republished
        // rather than silently dropped. Without confirms, `BasicPublish` is fire-and-forget and the outbox's
        // delivery guarantee is fiction.
        return await _connection!.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);
    }

    private Task OnConnectionShutdownAsync(object? sender, ShutdownEventArgs args)
    {
        if (!_disposed)
        {
            _logger.LogWarning("RabbitMQ connection shut down: {Reason}. Automatic recovery will retry.", args.ReplyText);
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_connection is not null)
        {
            _connection.ConnectionShutdownAsync -= OnConnectionShutdownAsync;
            await _connection.DisposeAsync();
        }

        _connectLock.Dispose();
    }
}
