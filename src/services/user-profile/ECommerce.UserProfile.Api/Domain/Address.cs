using ECommerce.Common.Guards;

namespace ECommerce.UserProfile.Api.Domain;

/// <summary>
/// A saved delivery or billing address.
/// </summary>
/// <remarks>
/// <para>
/// An <b>entity</b> here, not a value object, because a customer edits "my work address" and expects it to
/// remain the same address in their list. It has identity.
/// </para>
/// <para>
/// Note the contrast with Ordering: when an order is placed, the address is copied onto it as an immutable
/// <b>value object</b>. Editing this one afterwards must not change where a past order was shipped. Same
/// concept, different lifetime, different modelling — which is exactly what the bounded-context boundary is
/// for.
/// </para>
/// <para>
/// Reached only through <see cref="UserProfile"/>, which is what lets the aggregate guarantee "at most one
/// default shipping address". There is deliberately no <c>IAddressRepository</c> — that would be a hole
/// straight through the boundary.
/// </para>
/// </remarks>
public class Address
{
    private Address()
    {
    }

    public Address(
        string label,
        string line1,
        string? line2,
        string city,
        string postcode,
        string country,
        bool isDefaultShipping = false,
        bool isDefaultBilling = false)
    {
        Id = Guid.CreateVersion7();
        Label = Guard.AgainstTooLong(Guard.AgainstNullOrWhiteSpace(label), 50);
        Line1 = Guard.AgainstTooLong(Guard.AgainstNullOrWhiteSpace(line1), 200);
        Line2 = line2 is null ? null : Guard.AgainstTooLong(line2, 200);
        City = Guard.AgainstTooLong(Guard.AgainstNullOrWhiteSpace(city), 100);
        Postcode = Guard.AgainstTooLong(Guard.AgainstNullOrWhiteSpace(postcode), 20);

        // ISO 3166-1 alpha-2. Storing a code rather than free text means the value survives translation and
        // can be validated; "UK", "U.K." and "United Kingdom" are the same country to a human and three
        // different strings to a database.
        Country = Guard.AgainstTooLong(Guard.AgainstNullOrWhiteSpace(country), 2).ToUpperInvariant();

        IsDefaultShipping = isDefaultShipping;
        IsDefaultBilling = isDefaultBilling;
    }

    public Guid Id { get; private set; }

    public Guid UserProfileId { get; private set; }

    /// <summary>What the customer calls it — "Home", "Work". Their word, not ours.</summary>
    public string Label { get; private set; } = string.Empty;

    public string Line1 { get; private set; } = string.Empty;

    public string? Line2 { get; private set; }

    public string City { get; private set; } = string.Empty;

    public string Postcode { get; private set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-2 country code.</summary>
    public string Country { get; private set; } = string.Empty;

    public bool IsDefaultShipping { get; private set; }

    public bool IsDefaultBilling { get; private set; }

    /// <summary>
    /// Copies the editable fields from another instance.
    /// </summary>
    /// <remarks>
    /// Deliberately excludes <see cref="Id"/> and the default flags: identity must not change under an edit,
    /// and the default flags are the aggregate's business to coordinate, not this entity's.
    /// </remarks>
    internal void UpdateFrom(Address other)
    {
        Label = other.Label;
        Line1 = other.Line1;
        Line2 = other.Line2;
        City = other.City;
        Postcode = other.Postcode;
        Country = other.Country;
    }

    // internal, so only UserProfile can flip these - which is what makes the
    // "at most one default" invariant enforceable.
    internal void MarkAsDefaultShipping() => IsDefaultShipping = true;

    internal void ClearDefaultShipping() => IsDefaultShipping = false;

    internal void MarkAsDefaultBilling() => IsDefaultBilling = true;

    internal void ClearDefaultBilling() => IsDefaultBilling = false;
}
