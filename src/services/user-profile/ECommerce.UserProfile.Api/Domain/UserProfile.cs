using ECommerce.Common.Exceptions;
using ECommerce.Common.Guards;

namespace ECommerce.UserProfile.Api.Domain;

/// <summary>
/// Everything the business knows about a customer that is <b>not</b> identity.
/// </summary>
/// <remarks>
/// <para>
/// <b>The identity/profile split is the whole point of this service.</b> Keycloak answers
/// <i>"who are you and what may you do"</i>; this answers <i>"what do we know about you"</i>. See
/// <c>docs/adr/0004-identity-vs-profile-data-split.md</c>.
/// </para>
/// <para>
/// The test for which side a field belongs on: <b>is it needed to make an authorization decision?</b> If yes
/// it is identity data and belongs in the token. If no, it lives here and is fetched when needed. A shipping
/// address never decides whether a request is allowed, so it never belongs in a JWT.
/// </para>
/// <para>
/// <b>This class never stores a password, a role, or a group.</b> If it ever needs to, the boundary has been
/// drawn wrong.
/// </para>
/// <para>
/// It is an aggregate root in the DDD sense: addresses and consents are reached only through it, which is
/// what lets it enforce "at most one default shipping address" as an invariant rather than a hope.
/// </para>
/// </remarks>
public class UserProfile
{
    private readonly List<Address> _addresses = [];
    private readonly List<ConsentRecord> _consents = [];

    private UserProfile()
    {
    }

    public UserProfile(string subject, string? email, string? displayName)
    {
        Id = Guid.CreateVersion7();

        // The Keycloak `sub` claim. Opaque, immutable, never reused - and therefore the ONLY safe join key
        // between identity and business data. Email and username are mutable and reassignable, so keying on
        // either guarantees an eventual orphaning incident.
        Subject = Guard.AgainstNullOrWhiteSpace(subject);

        Email = email;
        DisplayName = displayName;
        Preferences = Preferences.Default();
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public string Subject { get; private set; } = string.Empty;

    public string? Email { get; private set; }

    public string? DisplayName { get; private set; }

    public string? PhoneNumber { get; private set; }

    /// <summary>Owned value object - stored in the same table, no identity of its own.</summary>
    public Preferences Preferences { get; private set; } = Preferences.Default();

    public IReadOnlyCollection<Address> Addresses => _addresses.AsReadOnly();

    public IReadOnlyCollection<ConsentRecord> Consents => _consents.AsReadOnly();

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public void UpdateContactDetails(string? displayName, string? phoneNumber)
    {
        DisplayName = displayName is null ? null : Guard.AgainstTooLong(displayName, 100);
        PhoneNumber = phoneNumber is null ? null : Guard.AgainstTooLong(phoneNumber, 30);
        Touch();
    }

    public void UpdatePreferences(Preferences preferences)
    {
        Preferences = Guard.AgainstNull(preferences);
        Touch();
    }

    /// <summary>
    /// Adds an address, maintaining the default-address invariants.
    /// </summary>
    /// <remarks>
    /// The rule "at most one default shipping address" is enforced <b>here, inside the aggregate</b>, rather
    /// than in the endpoint. That is the difference between a rule and a hope: a second endpoint, an import
    /// script or an admin tool all go through this method and all obey it.
    /// </remarks>
    public Address AddAddress(Address address)
    {
        Guard.AgainstNull(address);
        Guard.Against(_addresses.Count >= 20, "A profile may hold at most 20 addresses.");

        if (address.IsDefaultShipping)
        {
            ClearDefaultShipping();
        }

        if (address.IsDefaultBilling)
        {
            ClearDefaultBilling();
        }

        // The very first address becomes the default for both, because a customer with exactly one address
        // and no default would be asked to choose from a list of one at checkout.
        if (_addresses.Count == 0)
        {
            address.MarkAsDefaultShipping();
            address.MarkAsDefaultBilling();
        }

        _addresses.Add(address);
        Touch();

        return address;
    }

    public void UpdateAddress(Guid addressId, Address updated)
    {
        Address existing = FindAddress(addressId);

        if (updated.IsDefaultShipping && !existing.IsDefaultShipping)
        {
            ClearDefaultShipping();
        }

        if (updated.IsDefaultBilling && !existing.IsDefaultBilling)
        {
            ClearDefaultBilling();
        }

        existing.UpdateFrom(updated);
        Touch();
    }

    /// <summary>
    /// Removes an address, promoting another to default if the removed one was.
    /// </summary>
    /// <remarks>
    /// Leaving a profile with addresses but no default is the kind of half-state that produces a checkout
    /// page with nothing preselected. Promoting the next one keeps the invariant true after every operation,
    /// not just after the happy path.
    /// </remarks>
    public void RemoveAddress(Guid addressId)
    {
        Address address = FindAddress(addressId);

        bool wasDefaultShipping = address.IsDefaultShipping;
        bool wasDefaultBilling = address.IsDefaultBilling;

        _addresses.Remove(address);

        if (_addresses.Count > 0)
        {
            if (wasDefaultShipping)
            {
                _addresses[0].MarkAsDefaultShipping();
            }

            if (wasDefaultBilling)
            {
                _addresses[0].MarkAsDefaultBilling();
            }
        }

        Touch();
    }

    public void SetDefaultShipping(Guid addressId)
    {
        Address address = FindAddress(addressId);
        ClearDefaultShipping();
        address.MarkAsDefaultShipping();
        Touch();
    }

    public void SetDefaultBilling(Guid addressId)
    {
        Address address = FindAddress(addressId);
        ClearDefaultBilling();
        address.MarkAsDefaultBilling();
        Touch();
    }

    /// <summary>
    /// Records a consent decision.
    /// </summary>
    /// <remarks>
    /// <b>Append-only, never updated.</b> "When did they consent, and to what wording?" is a question
    /// regulators actually ask, and overwriting the previous record destroys the only evidence. Withdrawing
    /// consent adds a new record with <c>granted = false</c>; it does not delete the old one.
    /// </remarks>
    public void RecordConsent(string consentType, bool granted, string version)
    {
        _consents.Add(new ConsentRecord(Id, consentType, granted, version));
        Touch();
    }

    /// <summary>The most recent decision for a consent type, or null if never asked.</summary>
    public bool? CurrentConsent(string consentType) =>
        _consents
            .Where(c => string.Equals(c.ConsentType, consentType, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.RecordedAt)
            .Select(c => (bool?)c.Granted)
            .FirstOrDefault();

    private Address FindAddress(Guid addressId) =>
        _addresses.FirstOrDefault(a => a.Id == addressId)
        ?? throw new DomainException($"Address {addressId} does not belong to this profile.");

    private void ClearDefaultShipping()
    {
        foreach (Address address in _addresses)
        {
            address.ClearDefaultShipping();
        }
    }

    private void ClearDefaultBilling()
    {
        foreach (Address address in _addresses)
        {
            address.ClearDefaultBilling();
        }
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
