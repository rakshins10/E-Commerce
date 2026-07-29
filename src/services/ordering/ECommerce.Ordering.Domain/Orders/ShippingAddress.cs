using ECommerce.Common.Guards;
using ECommerce.Common.SeedWork;

namespace ECommerce.Ordering.Domain.Orders;

/// <summary>
/// Where this order is being sent.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a copy, not a reference, and that is the important part.</b> The User Profile service owns
/// the customer's address book. An order stores its own <i>snapshot</i> of the address as it was at the
/// moment of purchase.
/// </para>
/// <para>
/// If the order held a foreign key instead, then a customer moving house next year would silently rewrite
/// where last year's parcel was sent. Every dispatch note, every delivery dispute and every tax record
/// would change retrospectively. The order is a record of something that happened, and history does not
/// get to change because a profile did.
/// </para>
/// <para>
/// This is the same reason the order copies the product name and unit price rather than looking them up.
/// See <see cref="OrderItem"/>.
/// </para>
/// <para>
/// A value object: two identical addresses are interchangeable, and an address is never edited in place —
/// it is replaced wholesale, which is exactly what value semantics express.
/// </para>
/// </remarks>
public sealed class ShippingAddress : ValueObject
{
    private ShippingAddress()
    {
        // EF Core.
    }

    public ShippingAddress(
        string recipient,
        string line1,
        string? line2,
        string city,
        string postcode,
        string country)
    {
        Recipient = Guard.AgainstTooLong(Guard.AgainstNullOrWhiteSpace(recipient), 200);
        Line1 = Guard.AgainstTooLong(Guard.AgainstNullOrWhiteSpace(line1), 200);
        Line2 = line2 is null ? null : Guard.AgainstTooLong(line2, 200);
        City = Guard.AgainstTooLong(Guard.AgainstNullOrWhiteSpace(city), 100);
        Postcode = Guard.AgainstTooLong(Guard.AgainstNullOrWhiteSpace(postcode), 20);

        // Two letters, ISO 3166-1 alpha-2. Not validated against a list: countries change, and a
        // hard-coded list is a deployment away from rejecting a real customer.
        Guard.Against(country?.Length != 2, "Country must be an ISO 3166-1 alpha-2 code.");
        Country = country!.ToUpperInvariant();
    }

    public string Recipient { get; private set; } = string.Empty;

    public string Line1 { get; private set; } = string.Empty;

    public string? Line2 { get; private set; }

    public string City { get; private set; } = string.Empty;

    public string Postcode { get; private set; } = string.Empty;

    public string Country { get; private set; } = "GB";

    public override string ToString() =>
        string.Join(", ", new[] { Recipient, Line1, Line2, City, Postcode, Country }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Recipient;
        yield return Line1;
        yield return Line2;
        yield return City;
        yield return Postcode;
        yield return Country;
    }
}
