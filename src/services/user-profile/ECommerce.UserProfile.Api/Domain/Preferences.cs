using ECommerce.Common.Guards;
using ECommerce.Common.SeedWork;

namespace ECommerce.UserProfile.Api.Domain;

/// <summary>
/// How a customer wants the shop to behave for them.
/// </summary>
/// <remarks>
/// <para>
/// A <b>value object</b>: it has no identity of its own, two profiles with identical preferences are
/// interchangeable in every way that matters, and it is replaced wholesale rather than mutated. Stored as
/// owned columns on the profile row — a separate table would imply a lifetime it does not have.
/// </para>
/// <para>
/// <b>These are profile data, not identity data.</b> None of them decides whether a request is allowed, so
/// none belongs in the token — putting them in claims would bloat every request to every service with data
/// almost none of them care about. See <c>docs/adr/0004</c>.
/// </para>
/// </remarks>
public sealed class Preferences : ValueObject
{
    private Preferences()
    {
    }

    public Preferences(
        string locale,
        string currency,
        string theme,
        bool marketingEmail,
        bool marketingSms,
        bool orderUpdatesEmail,
        bool orderUpdatesSms)
    {
        Locale = Guard.AgainstTooLong(Guard.AgainstNullOrWhiteSpace(locale), 10);
        Currency = Guard.AgainstTooLong(Guard.AgainstNullOrWhiteSpace(currency), 3).ToUpperInvariant();

        Theme = theme is "light" or "dark" or "system"
            ? theme
            : throw new Common.Exceptions.DomainException(
                $"Theme must be light, dark or system, but was '{theme}'.");

        MarketingEmail = marketingEmail;
        MarketingSms = marketingSms;
        OrderUpdatesEmail = orderUpdatesEmail;
        OrderUpdatesSms = orderUpdatesSms;
    }

    /// <summary>BCP 47 language tag, e.g. <c>en-GB</c>. Drives date and number formatting.</summary>
    public string Locale { get; private set; } = "en-GB";

    /// <summary>ISO 4217 code the customer wants prices shown in.</summary>
    public string Currency { get; private set; } = "GBP";

    /// <summary><c>light</c>, <c>dark</c> or <c>system</c>.</summary>
    public string Theme { get; private set; } = "system";

    /// <summary>Consent to non-transactional email.</summary>
    /// <remarks>
    /// Legally distinct from <see cref="OrderUpdatesEmail"/>. Marketing requires opt-in and can be withdrawn;
    /// an order confirmation is part of the contract and is sent regardless. Conflating the two is how a shop
    /// ends up either spamming people or failing to tell them their order shipped.
    /// </remarks>
    public bool MarketingEmail { get; private set; }

    public bool MarketingSms { get; private set; }

    /// <summary>Transactional order updates by email. Defaults to on.</summary>
    public bool OrderUpdatesEmail { get; private set; } = true;

    public bool OrderUpdatesSms { get; private set; }

    /// <summary>
    /// Sensible defaults for a new profile.
    /// </summary>
    /// <remarks>
    /// <b>Marketing defaults to OFF.</b> Pre-ticked marketing consent is not valid consent under GDPR, and a
    /// default that quietly opts people in is the kind of thing that shows up in a regulator's report rather
    /// than a bug tracker.
    /// </remarks>
    public static Preferences Default() =>
        new(
            locale: "en-GB",
            currency: "GBP",
            theme: "system",
            marketingEmail: false,
            marketingSms: false,
            orderUpdatesEmail: true,
            orderUpdatesSms: false);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Locale;
        yield return Currency;
        yield return Theme;
        yield return MarketingEmail;
        yield return MarketingSms;
        yield return OrderUpdatesEmail;
        yield return OrderUpdatesSms;
    }
}

/// <summary>
/// An append-only record that a customer agreed to — or withdrew from — something.
/// </summary>
/// <remarks>
/// Records the <b>version</b> of the wording they agreed to, not just the fact of agreement. "They consented"
/// is not a defensible answer on its own; "they consented to v2.1 of the marketing policy on this date" is.
/// </remarks>
public class ConsentRecord
{
    private ConsentRecord()
    {
    }

    public ConsentRecord(Guid userProfileId, string consentType, bool granted, string version)
    {
        Id = Guid.CreateVersion7();
        UserProfileId = userProfileId;
        ConsentType = Guard.AgainstTooLong(Guard.AgainstNullOrWhiteSpace(consentType), 50);
        Granted = granted;
        Version = Guard.AgainstTooLong(Guard.AgainstNullOrWhiteSpace(version), 20);
        RecordedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid UserProfileId { get; private set; }

    /// <summary>e.g. <c>marketing-email</c>, <c>terms-of-service</c>.</summary>
    public string ConsentType { get; private set; } = string.Empty;

    public bool Granted { get; private set; }

    /// <summary>Version of the wording agreed to.</summary>
    public string Version { get; private set; } = string.Empty;

    public DateTimeOffset RecordedAt { get; private set; }
}
