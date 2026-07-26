namespace ECommerce.Ordering.Domain.Orders;

/// <summary>
/// Where an order is in its lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// The numbers are persisted, so they are assigned explicitly and never reordered. Relying on
/// declaration order means inserting a status in the middle silently reinterprets every existing row.
/// </para>
/// <para>
/// Gaps are deliberate: statuses added later slot in numerically near their neighbours without
/// renumbering anything.
/// </para>
/// <para>
/// <b>This is a state machine, not a set of flags.</b> An order is in exactly one state, and the legal
/// transitions are encoded in <see cref="Order"/> rather than checked by whoever happens to be calling.
/// Booleans such as <c>IsPaid</c> and <c>IsCancelled</c> permit "paid and cancelled and shipped" to be
/// true at once, and something eventually sets that combination.
/// </para>
/// </remarks>
public enum OrderStatus
{
    /// <summary>Created and awaiting payment. Stock has been requested but not confirmed.</summary>
    Submitted = 10,

    /// <summary>Stock is reserved. Waiting for the payment result.</summary>
    AwaitingPayment = 20,

    /// <summary>Paid and ready to be picked. The point of no return for a customer cancellation.</summary>
    Paid = 30,

    /// <summary>Handed to the courier.</summary>
    Shipped = 40,

    /// <summary>Delivered to the customer. Terminal.</summary>
    Delivered = 50,

    /// <summary>Cancelled before dispatch, by the customer or by a failed step. Terminal.</summary>
    Cancelled = 90,
}
