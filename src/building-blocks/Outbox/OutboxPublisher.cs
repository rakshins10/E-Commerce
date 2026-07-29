using System.Diagnostics;
using System.Text.Json;

using ECommerce.EventBus;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ECommerce.Outbox;

/// <summary>
/// Drains the outbox: reads unpublished rows, sends them to the broker, marks them published.
/// </summary>
/// <typeparam name="TContext">The service's own <see cref="DbContext"/>.</typeparam>
/// <remarks>
/// <para>
/// A background service, running in the same process as the API. That is a deliberate simplification for
/// this repo and worth being explicit about: a separate deployable would let publishing scale
/// independently and survive an API restart, at the cost of another thing to run and monitor. For a
/// reference implementation the in-process version is easier to follow and has the same semantics.
/// </para>
/// <para>
/// <b>Ordering is per-batch, not global.</b> Rows are read oldest-first, so events from the same
/// aggregate normally arrive in order. "Normally" is the honest word: with several replicas running,
/// two publishers can interleave. Where strict ordering matters, the consumer must handle it — a status
/// transition that checks the current state (as <c>Order</c> does) is robust to arriving out of order in
/// a way that a blind overwrite is not.
/// </para>
/// </remarks>
public sealed class OutboxPublisher<TContext>(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    ILogger<OutboxPublisher<TContext>> logger) : BackgroundService
    where TContext : DbContext, IOutboxContext
{
    private readonly OutboxOptions _options = options.Value;

    /// <summary>
    /// Reconstructs an event's <c>ActivityContext</c> so its span joins the trace that produced it.
    /// </summary>
    private static readonly ActivitySource ActivitySource = new("ECommerce.Outbox");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Outbox publisher started. Polling every {Interval}ms, batch size {BatchSize}.",
            _options.PollingIntervalMs,
            _options.BatchSize);

        // A short delay before the first pass, so the publisher does not compete with migrations and
        // warm-up on a cold start.
        await Task.Delay(_options.StartupDelayMs, stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int published = await PublishPendingAsync(stoppingToken).ConfigureAwait(false);

                // Only sleep when there was nothing to do. If a batch was full there is probably more
                // waiting, and sleeping through a backlog is how a queue becomes an incident.
                if (published < _options.BatchSize)
                {
                    await Task.Delay(_options.PollingIntervalMs, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The loop must not die. A publisher that exits on the first transient database error
                // stops every integration event in the service, silently, until someone restarts it.
                logger.LogError(ex, "Outbox publishing pass failed. Retrying after the poll interval.");
                await Task.Delay(_options.PollingIntervalMs, stoppingToken).ConfigureAwait(false);
            }
        }

        logger.LogInformation("Outbox publisher stopped.");
    }

    private async Task<int> PublishPendingAsync(CancellationToken cancellationToken)
    {
        // A fresh scope per pass. A BackgroundService is a singleton and a DbContext is scoped, so
        // holding one for the lifetime of the process would accumulate tracked entities forever and
        // never see another writer's committed rows.
        using IServiceScope scope = scopeFactory.CreateScope();

        TContext db = scope.ServiceProvider.GetRequiredService<TContext>();
        IEventBus bus = scope.ServiceProvider.GetRequiredService<IEventBus>();
        IOutboxEventResolver resolver = scope.ServiceProvider.GetRequiredService<IOutboxEventResolver>();

        List<OutboxMessage> pending = await db.OutboxMessages
            .Where(message => message.PublishedAt == null)
            .OrderBy(message => message.OccurredAt)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (pending.Count == 0)
        {
            return 0;
        }

        foreach (OutboxMessage message in pending)
        {
            await PublishOneAsync(message, bus, resolver, cancellationToken).ConfigureAwait(false);
        }

        // Saved once for the whole batch rather than per message. Each row's outcome is independent and
        // already recorded on the entity, so one round trip is enough.
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return pending.Count;
    }

    private async Task PublishOneAsync(
        OutboxMessage message,
        IEventBus bus,
        IOutboxEventResolver resolver,
        CancellationToken cancellationToken)
    {
        // Links this publish to the request that created the event, so a trace spans the HTTP call, the
        // database write, the publish, and eventually the consumer.
        ActivityContext.TryParse(message.TraceParent, null, out ActivityContext parent);

        using Activity? activity = ActivitySource.StartActivity(
            $"outbox publish {message.EventName}", ActivityKind.Producer, parent);

        activity?.SetTag("messaging.message.id", message.Id);
        activity?.SetTag("messaging.destination.name", message.EventName);

        try
        {
            Type? eventType = resolver.Resolve(message.EventName);

            if (eventType is null)
            {
                // An event type that no longer exists in this build - typically a rename, or a message
                // written by a newer version during a rolling deploy. Marking it published would lose
                // it silently; leaving it pending would block nothing (the query is not ordered by a
                // cursor) but would retry forever. Recording the error and leaving it pending is the
                // honest option: it stays visible in the table for someone to deal with.
                message.MarkFailed($"No CLR type is registered for event '{message.EventName}'.");

                logger.LogError(
                    "Outbox message {MessageId} has unknown event type {EventName}. Left unpublished.",
                    message.Id,
                    message.EventName);

                return;
            }

            if (JsonSerializer.Deserialize(message.Payload, eventType, OutboxSerialization.Options)
                is not IntegrationEvent @event)
            {
                message.MarkFailed($"Payload did not deserialise to an IntegrationEvent.");
                return;
            }

            await bus.PublishAsync(@event, cancellationToken).ConfigureAwait(false);

            message.MarkPublished();

            logger.LogDebug(
                "Published outbox message {MessageId} ({EventName}).", message.Id, message.EventName);
        }
        catch (Exception ex)
        {
            // Recorded, not rethrown. One poisonous message must not stop the other 49 in the batch,
            // and the row stays unpublished so the next pass retries it.
            message.MarkFailed(ex.ToString());
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            logger.LogWarning(
                ex,
                "Failed to publish outbox message {MessageId} ({EventName}), attempt {Attempts}.",
                message.Id,
                message.EventName,
                message.Attempts);
        }
    }
}
