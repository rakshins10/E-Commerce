using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ECommerce.EventBus.RabbitMQ;

/// <summary>
/// RabbitMQ implementation of <see cref="IEventBus"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pattern:</b> Publish-Subscribe, competing consumers, dead-lettering.
/// See <c>docs/adr/0016-rabbitmq-behind-ieventbus.md</c>.
/// </para>
/// <para><b>Topology</b> (see <c>docs/diagrams/event-flow.md</c>):</para>
/// <list type="bullet">
///   <item><description>One durable <b>topic exchange</b>, <c>ecommerce.events</c>.</description></item>
///   <item><description><b>Routing key = event name</b>, so a subscriber binds only what it wants.</description></item>
///   <item><description><b>One durable queue per service per event</b> — each subscriber gets its own copy and
///   its own failure handling.</description></item>
///   <item><description><b>Competing consumers within a service</b>: instances share a queue, so scaling out
///   spreads load with no code change.</description></item>
///   <item><description><b>A dead-letter exchange per queue</b>, so a message that can never succeed stops
///   blocking the ones behind it.</description></item>
/// </list>
/// </remarks>
public sealed class RabbitMqEventBus : IEventBus, IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IRabbitMqConnection _connection;
    private readonly IEventBusSubscriptionManager _subscriptions;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RabbitMqEventBus> _logger;
    private readonly RabbitMqOptions _options;
    private readonly List<IChannel> _consumerChannels = [];
    private readonly SemaphoreSlim _publishLock = new(1, 1);

    private IChannel? _publishChannel;
    private bool _disposed;

    public RabbitMqEventBus(
        IRabbitMqConnection connection,
        IEventBusSubscriptionManager subscriptions,
        IServiceProvider serviceProvider,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqEventBus> logger)
    {
        _connection = connection;
        _subscriptions = subscriptions;
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Called by the outbox dispatcher, not by application code — see <see cref="IEventBus.PublishAsync"/> for
    /// why publishing directly from a handler reintroduces the dual-write problem.
    /// </remarks>
    public async Task PublishAsync(IntegrationEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        IChannel channel = await GetPublishChannelAsync(cancellationToken);

        var properties = new BasicProperties
        {
            // Persistent: the broker writes the message to disk, so it survives a broker restart. A durable
            // queue holding transient messages still loses them - both flags are required.
            DeliveryMode = DeliveryModes.Persistent,
            ContentType = "application/json",
            Type = @event.EventName,

            // MessageId carries the outbox row id, which is what makes consumer-side deduplication possible.
            MessageId = @event.Id.ToString(),
            CorrelationId = @event.CorrelationId,
            Timestamp = new AmqpTimestamp(@event.OccurredAt.ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>
            {
                // Propagates the W3C trace context across the async hop so the consumer's span links back to
                // the producer's trace. Without this, every event boundary breaks the distributed trace.
                ["traceparent"] = @event.TraceParent,
            },
        };

        byte[] body = JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType(), SerializerOptions);

        // Serialised because IChannel is not thread-safe and this instance is a singleton.
        await _publishLock.WaitAsync(cancellationToken);
        try
        {
            // Awaiting this returns only once the broker has confirmed the message (publisher confirms are
            // enabled on the channel). If it throws, the outbox row stays unprocessed and is retried.
            await channel.BasicPublishAsync(
                exchange: _options.ExchangeName,
                routingKey: @event.EventName,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);
        }
        finally
        {
            _publishLock.Release();
        }

        _logger.LogDebug(
            "Published {EventName} {MessageId} (correlation {CorrelationId}).",
            @event.EventName,
            @event.Id,
            @event.CorrelationId);
    }

    /// <inheritdoc />
    public async Task SubscribeAsync<TEvent, THandler>(CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent
        where THandler : IIntegrationEventHandler<TEvent>
    {
        string eventName = typeof(TEvent).Name;

        // Idempotent: a repeated subscription must not start a second consumer, or every message would be
        // processed twice by design.
        if (!_subscriptions.AddSubscription<TEvent, THandler>())
        {
            _logger.LogDebug("Already subscribed to {EventName}; ignoring duplicate registration.", eventName);
            return;
        }

        await StartConsumerAsync(eventName, cancellationToken);

        _logger.LogInformation("Subscribed to {EventName} with handler {Handler}.", eventName, typeof(THandler).Name);
    }

    private async Task<IChannel> GetPublishChannelAsync(CancellationToken cancellationToken)
    {
        if (_publishChannel is { IsOpen: true })
        {
            return _publishChannel;
        }

        _publishChannel = await _connection.CreateChannelAsync(cancellationToken);

        await _publishChannel.ExchangeDeclareAsync(
            exchange: _options.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        return _publishChannel;
    }

    private async Task StartConsumerAsync(string eventName, CancellationToken cancellationToken)
    {
        // A dedicated channel per consumer: channels are not thread-safe, and one slow or failing consumer
        // must not stall the others.
        IChannel channel = await _connection.CreateChannelAsync(cancellationToken);

        string queueName = $"{_options.SubscriptionClientName}.{eventName}";
        string deadLetterExchange = $"{_options.ExchangeName}.dlx";
        string deadLetterQueue = $"{queueName}.dlq";

        await channel.ExchangeDeclareAsync(
            _options.ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false,
            cancellationToken: cancellationToken);

        // The dead-letter path is declared up front rather than on first failure - a queue cannot gain a
        // dead-letter exchange after creation without being deleted and redeclared.
        await channel.ExchangeDeclareAsync(
            deadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(
            deadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            deadLetterQueue, deadLetterExchange, queueName, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = deadLetterExchange,
                ["x-dead-letter-routing-key"] = queueName,
            },
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: queueName,
            exchange: _options.ExchangeName,
            routingKey: eventName,
            cancellationToken: cancellationToken);

        // Without a prefetch limit the broker pushes the whole queue to the first consumer to connect, which
        // exhausts its memory and leaves every other instance idle. See RabbitMqOptions.PrefetchCount.
        await channel.BasicQosAsync(
            prefetchSize: 0, prefetchCount: _options.PrefetchCount, global: false,
            cancellationToken: cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, args) => OnMessageReceivedAsync(channel, args);

        // autoAck: false. With autoAck the broker considers a message delivered the instant it hits the socket,
        // so a consumer that crashes mid-handler loses it permanently. Manual acknowledgement after successful
        // handling is what makes delivery at-least-once rather than at-most-once.
        await channel.BasicConsumeAsync(
            queue: queueName, autoAck: false, consumer: consumer, cancellationToken: cancellationToken);

        _consumerChannels.Add(channel);
    }

    private async Task OnMessageReceivedAsync(IChannel channel, BasicDeliverEventArgs args)
    {
        string eventName = args.BasicProperties.Type ?? args.RoutingKey;

        try
        {
            Type? eventType = _subscriptions.GetEventTypeByName(eventName);
            if (eventType is null)
            {
                // Not an error: a newer publisher may emit a type this build predates. Retrying will never make
                // the type known, so acknowledge and discard rather than dead-lettering forever.
                _logger.LogWarning("Received unknown event type {EventName}; acknowledging and discarding.", eventName);
                await channel.BasicAckAsync(args.DeliveryTag, multiple: false);
                return;
            }

            string json = Encoding.UTF8.GetString(args.Body.Span);
            object? @event = JsonSerializer.Deserialize(json, eventType, SerializerOptions);

            if (@event is null)
            {
                _logger.LogError("Failed to deserialise {EventName}; dead-lettering.", eventName);
                await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            // A scope per message: handlers take scoped dependencies (a DbContext, the current transaction),
            // and reusing one scope across messages would share a change tracker between unrelated units of
            // work - a classic and very hard-to-diagnose bug.
            await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();

            foreach (SubscriptionInfo subscription in _subscriptions.GetHandlersFor(eventName))
            {
                object handler = scope.ServiceProvider.GetRequiredService(subscription.HandlerType);

                Task task = (Task)subscription.HandlerType
                    .GetMethod(nameof(IIntegrationEventHandler<IntegrationEvent>.HandleAsync))!
                    .Invoke(handler, [@event, CancellationToken.None])!;

                await task;
            }

            await channel.BasicAckAsync(args.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            // requeue is decided by how many times this message has already been delivered. Requeueing forever
            // would let one permanently-failing message saturate the consumer and block everything behind it.
            bool exhausted = GetDeliveryAttempt(args) >= _options.MaxDeliveryAttempts;

            _logger.LogError(
                ex,
                "Handling {EventName} {MessageId} failed. {Action}.",
                eventName,
                args.BasicProperties.MessageId,
                exhausted ? "Attempts exhausted, dead-lettering" : "Requeueing for retry");

            await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: !exhausted);
        }
    }

    /// <summary>
    /// How many times the broker has already tried to deliver this message.
    /// </summary>
    /// <remarks>
    /// RabbitMQ's <c>x-death</c> header only appears once a message has been dead-lettered and returned, so it
    /// covers the redelivery case; a first delivery has neither header and counts as attempt 1.
    /// </remarks>
    private static long GetDeliveryAttempt(BasicDeliverEventArgs args)
    {
        if (args.BasicProperties.Headers?.TryGetValue("x-death", out object? death) == true
            && death is List<object> entries
            && entries.Count > 0
            && entries[0] is Dictionary<string, object> first
            && first.TryGetValue("count", out object? count))
        {
            return Convert.ToInt64(count, System.Globalization.CultureInfo.InvariantCulture) + 1;
        }

        return args.Redelivered ? 2 : 1;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (IChannel channel in _consumerChannels)
        {
            await channel.DisposeAsync();
        }

        if (_publishChannel is not null)
        {
            await _publishChannel.DisposeAsync();
        }

        _publishLock.Dispose();
    }
}
