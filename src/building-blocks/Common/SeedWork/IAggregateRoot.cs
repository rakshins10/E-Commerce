namespace ECommerce.Common.SeedWork;

/// <summary>
/// Marks an entity as an <b>aggregate root</b> — the single entry point through which its aggregate is loaded,
/// modified, and saved.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pattern:</b> Aggregate / Aggregate Root (DDD tactical).
/// See <c>docs/domain/bounded-contexts.md#ordering</c> and <c>docs/diagrams/ordering-aggregate.md</c>.
/// </para>
/// <para>
/// An <b>aggregate</b> is a cluster of objects treated as one unit for data changes, and — this is the part that
/// matters — <b>one consistency boundary</b>. Everything inside it is guaranteed consistent after every
/// transaction; anything outside it is only <i>eventually</i> consistent.
/// </para>
/// <para>
/// The rules this interface exists to signal:
/// </para>
/// <list type="number">
///   <item><description><b>Outside code touches only the root.</b> Nothing loads an <c>OrderItem</c> directly;
///   it goes through <c>Order</c>. That is what lets the root enforce its invariants — if callers could mutate
///   items directly, no invariant could be guaranteed.</description></item>
///   <item><description><b>One repository per aggregate root</b>, never per entity. A repository for
///   <c>OrderItem</c> would be a hole straight through the boundary.</description></item>
///   <item><description><b>Reference other aggregates by id, never by object.</b> An <c>Order</c> holds a
///   <c>BuyerId</c>, not a <c>Buyer</c>. Holding the object implies they must be loaded and saved together,
///   which silently merges two consistency boundaries into one.</description></item>
///   <item><description><b>One transaction changes one aggregate instance.</b> Needing to change two in one
///   transaction is the signal that either the boundary is wrong, or the second change belongs in a saga
///   (<c>docs/adr/0011-orchestration-saga.md</c>).</description></item>
/// </list>
/// <para>
/// <b>How to size an aggregate.</b> Draw the boundary at what must be <i>transactionally</i> consistent, not at
/// what is convenient to load together. <c>OrderItem</c> is inside <c>Order</c> because an order's total must
/// never disagree with its lines. The buyer is outside because an order does not need the customer record to be
/// consistent with it at every instant. Too large and you get lock contention and slow loads; too small and
/// invariants have nowhere to live. When in doubt, prefer <i>smaller</i> — the usual failure is aggregates that
/// grow to swallow half the model.
/// </para>
/// <para>
/// This is a marker interface with no members. That is deliberate: its job is to make the boundary explicit in
/// the type system so repositories can be constrained to it
/// (<c>IRepository&lt;T&gt; where T : IAggregateRoot</c>) and so an architecture test can assert that nothing
/// else is exposed by a repository.
/// </para>
/// </remarks>
public interface IAggregateRoot;
