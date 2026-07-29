using ECommerce.Common.Exceptions;
using ECommerce.Ordering.Domain.Orders;
using FluentAssertions;

namespace ECommerce.Ordering.Domain.Tests;

/// <summary>
/// The Order aggregate's rules, asserted directly against the aggregate.
/// </summary>
/// <remarks>
/// Every test here names a business rule rather than a method. "a shipped order cannot be cancelled" is
/// a sentence someone in the business would recognise and could tell you is wrong;
/// "Cancel_WhenShipped_ThrowsDomainException" describes an implementation. When a rule changes, the test
/// that has to change should be findable by its description.
/// </remarks>
public class OrderTests
{
    private static ShippingAddress AnAddress() =>
        new("Casey Customer", "12 Rosewood Avenue", null, "Bristol", "BS1 4TP", "GB");

    private static OrderLineRequest ALine(
        string name = "Aurora Wireless Headphones",
        decimal price = 89.99m,
        int quantity = 1,
        Guid? productId = null) =>
        new(productId ?? Guid.CreateVersion7(), "AUR-HP-001", name, new Money(price, "GBP"), quantity);

    private static Order AnOrder(params OrderLineRequest[] lines) =>
        Order.Submit(
            "a1b2c3d4-0000-7000-8000-000000000001",
            "Casey Customer",
            AnAddress(),
            "GBP",
            lines.Length == 0 ? [ALine()] : lines);

    // -------------------------------------------------------------------------
    //  Creation
    // -------------------------------------------------------------------------

    [Fact]
    public void a_submitted_order_starts_in_the_submitted_state()
    {
        AnOrder().Status.Should().Be(OrderStatus.Submitted);
    }

    [Fact]
    public void an_order_must_contain_at_least_one_item()
    {
        Action submit = () => Order.Submit("buyer", "Casey", AnAddress(), "GBP", []);

        submit.Should().Throw<DomainException>().WithMessage("*at least one item*");
    }

    [Fact]
    public void an_order_gets_a_human_readable_reference_as_well_as_an_id()
    {
        Order order = AnOrder();

        // The GUID is for the database; ORD-20260726-4F2A is for the customer on the phone.
        order.OrderNumber.Should().MatchRegex(@"^ORD-\d{8}-[0-9A-F]{4}$");
        order.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void submitting_an_order_records_that_it_happened()
    {
        Order order = AnOrder();

        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderSubmittedDomainEvent>()
            .Which.OrderNumber.Should().Be(order.OrderNumber);
    }

    // -------------------------------------------------------------------------
    //  Lines and totals
    // -------------------------------------------------------------------------

    [Fact]
    public void the_total_is_the_sum_of_the_lines()
    {
        Order order = AnOrder(
            ALine(price: 89.99m, quantity: 2),
            ALine("Nimbus Laptop Stand", 34.50m, 1, Guid.CreateVersion7()));

        order.Total.Should().Be(new Money(214.48m, "GBP"));
        order.TotalUnits.Should().Be(3);
    }

    [Fact]
    public void two_lines_for_the_same_product_are_merged_into_one()
    {
        Guid productId = Guid.CreateVersion7();

        // Two picking instructions for one shelf is a warehouse problem, and an invoice the customer
        // cannot follow is a support problem.
        Order order = AnOrder(
            ALine(quantity: 2, productId: productId),
            ALine(quantity: 3, productId: productId));

        order.Items.Should().ContainSingle().Which.Quantity.Should().Be(5);
    }

    [Fact]
    public void a_line_may_not_exceed_the_per_line_quantity_limit()
    {
        Action submit = () => AnOrder(ALine(quantity: OrderItem.MaxQuantityPerLine + 1));

        submit.Should().Throw<DomainException>().WithMessage("*may not exceed*");
    }

    [Fact]
    public void merging_lines_cannot_be_used_to_get_past_the_quantity_limit()
    {
        Guid productId = Guid.CreateVersion7();

        // The interesting case: each line is individually legal, and the merge is not. A limit checked
        // only in the constructor would let this through.
        Action submit = () => AnOrder(
            ALine(quantity: 60, productId: productId),
            ALine(quantity: 60, productId: productId));

        submit.Should().Throw<DomainException>().WithMessage("*may not exceed*");
    }

    [Fact]
    public void an_order_may_not_hold_more_than_the_maximum_number_of_distinct_items()
    {
        OrderLineRequest[] lines = Enumerable
            .Range(0, Order.MaxItems + 1)
            .Select(i => ALine($"Product {i}", productId: Guid.CreateVersion7()))
            .ToArray();

        Action submit = () => AnOrder(lines);

        submit.Should().Throw<DomainException>().WithMessage("*more than*");
    }

    [Fact]
    public void a_line_priced_in_another_currency_is_rejected()
    {
        var euroLine = new OrderLineRequest(
            Guid.CreateVersion7(), "EUR-001", "Imported item", new Money(50m, "EUR"), 1);

        Action submit = () => AnOrder(euroLine);

        submit.Should().Throw<DomainException>().WithMessage("*GBP*EUR*");
    }

    [Fact]
    public void the_order_total_never_leaves_a_fraction_of_a_penny()
    {
        // 3 x 33.333 is 99.999, which cannot be charged. Rounding at construction means an
        // uncharageable amount cannot be held in the first place.
        Order order = AnOrder(ALine(price: 33.333m, quantity: 3));

        order.Total.Amount.Should().Be(99.99m);
    }

    // -------------------------------------------------------------------------
    //  The state machine
    // -------------------------------------------------------------------------

    [Fact]
    public void the_happy_path_runs_submitted_to_delivered()
    {
        Order order = AnOrder();

        order.ConfirmStock();
        order.Status.Should().Be(OrderStatus.AwaitingPayment);

        order.MarkAsPaid("pay_123");
        order.Status.Should().Be(OrderStatus.Paid);

        order.MarkAsShipped();
        order.Status.Should().Be(OrderStatus.Shipped);

        order.MarkAsDelivered();
        order.Status.Should().Be(OrderStatus.Delivered);
    }

    [Fact]
    public void an_order_cannot_be_paid_before_its_stock_is_confirmed()
    {
        Order order = AnOrder();

        Action pay = () => order.MarkAsPaid("pay_123");

        pay.Should().Throw<DomainException>().WithMessage("*Submitted, not AwaitingPayment*");
    }

    [Fact]
    public void an_order_cannot_be_shipped_before_it_is_paid()
    {
        Order order = AnOrder();
        order.ConfirmStock();

        Action ship = order.MarkAsShipped;

        ship.Should().Throw<DomainException>();
    }

    [Fact]
    public void confirming_stock_twice_is_rejected()
    {
        Order order = AnOrder();
        order.ConfirmStock();

        Action again = order.ConfirmStock;

        again.Should().Throw<DomainException>();
    }

    // -------------------------------------------------------------------------
    //  Idempotency — the rules that make at-least-once delivery survivable
    // -------------------------------------------------------------------------

    [Fact]
    public void paying_an_already_paid_order_is_ignored_rather_than_rejected()
    {
        Order order = AnOrder();
        order.ConfirmStock();
        order.MarkAsPaid("pay_123");
        order.ClearDomainEvents();

        // RabbitMQ guarantees at-least-once delivery, so this message WILL arrive twice eventually.
        // Throwing would dead-letter a message that describes something already true; raising a second
        // event would email the customer twice.
        order.MarkAsPaid("pay_123");

        order.Status.Should().Be(OrderStatus.Paid);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void cancelling_an_already_cancelled_order_is_ignored()
    {
        Order order = AnOrder();
        order.Cancel(OrderCancellationReason.RequestedByCustomer);
        order.ClearDomainEvents();

        order.Cancel(OrderCancellationReason.PaymentDeclined);

        order.DomainEvents.Should().BeEmpty();
        // The first reason stands. A compensating retry must not rewrite why it happened.
        order.CancellationReason.Should().Be(OrderCancellationReason.RequestedByCustomer);
    }

    // -------------------------------------------------------------------------
    //  Cancellation
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Submitted)]
    [InlineData(OrderStatus.AwaitingPayment)]
    [InlineData(OrderStatus.Paid)]
    public void an_order_can_be_cancelled_at_any_point_before_dispatch(OrderStatus upTo)
    {
        Order order = AnOrder();

        if (upTo >= OrderStatus.AwaitingPayment)
        {
            order.ConfirmStock();
        }

        if (upTo >= OrderStatus.Paid)
        {
            order.MarkAsPaid("pay_123");
        }

        order.CanBeCancelled.Should().BeTrue();
        order.Cancel(OrderCancellationReason.RequestedByCustomer);

        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public void a_shipped_order_cannot_be_cancelled_because_that_is_a_return()
    {
        Order order = AnOrder();
        order.ConfirmStock();
        order.MarkAsPaid("pay_123");
        order.MarkAsShipped();

        Action cancel = () => order.Cancel(OrderCancellationReason.RequestedByCustomer);

        // The message points at the right process rather than saying "invalid state". A cancellation
        // and a return move different money and different stock.
        cancel.Should().Throw<DomainException>().WithMessage("*return*");
        order.CanBeCancelled.Should().BeFalse();
    }

    [Fact]
    public void cancelling_records_whether_there_was_stock_to_release()
    {
        Order order = AnOrder();
        order.ConfirmStock();
        order.ClearDomainEvents();

        order.Cancel(OrderCancellationReason.PaymentDeclined);

        // The saga needs this to know whether to compensate. Releasing stock that was never reserved
        // inflates the available count, which is worse than the original failure.
        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderCancelledDomainEvent>()
            .Which.StockWasReserved.Should().BeTrue();
    }

    [Fact]
    public void cancelling_before_stock_was_reserved_says_there_is_nothing_to_release()
    {
        Order order = AnOrder();
        order.ClearDomainEvents();

        order.Cancel(OrderCancellationReason.OutOfStock);

        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderCancelledDomainEvent>()
            .Which.StockWasReserved.Should().BeFalse();
    }

    [Fact]
    public void the_reason_for_cancellation_is_part_of_the_record()
    {
        Order order = AnOrder();

        order.Cancel(OrderCancellationReason.PaymentDeclined);

        // "Cancelled because payment failed" and "cancelled because the customer changed their mind"
        // lead to different follow-up. A bare boolean loses that.
        order.CancellationReason.Should().Be(OrderCancellationReason.PaymentDeclined);
        order.CancelledAt.Should().NotBeNull();
    }

    // -------------------------------------------------------------------------
    //  The aggregate boundary
    // -------------------------------------------------------------------------

    [Fact]
    public void the_items_collection_cannot_be_modified_from_outside_the_aggregate()
    {
        Order order = AnOrder();

        // Every rule about lines - merging, limits, currency - lives in Order. A caller that could
        // reach the list directly could bypass all of them, and the aggregate boundary would be a
        // naming convention rather than a guarantee.
        //
        // Note what this does NOT assert. `AsReadOnly()` returns a ReadOnlyCollection<T>, which *does*
        // implement IList<T> - it satisfies the interface and throws on every mutating member. So
        // asserting "not assignable to IList<T>" fails while the protection is perfectly intact. The
        // guarantee worth testing is the behaviour, not the type.
        Action mutate = () => ((IList<OrderItem>)order.Items).Add(
            order.Items.Single());

        mutate.Should().Throw<NotSupportedException>();
        order.Items.Should().ContainSingle();
    }

    [Fact]
    public void a_line_total_is_derived_and_cannot_disagree_with_its_own_inputs()
    {
        Order order = AnOrder(ALine(price: 19.99m, quantity: 3));

        order.Items.Single().LineTotal.Should().Be(new Money(59.97m, "GBP"));
    }
}
