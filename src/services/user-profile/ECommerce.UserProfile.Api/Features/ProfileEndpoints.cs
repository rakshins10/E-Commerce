using ECommerce.Auth;
using ECommerce.Common.Exceptions;
using ECommerce.UserProfile.Api.Domain;
using ECommerce.UserProfile.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.UserProfile.Api.Features;

// --- Contracts ---------------------------------------------------------------
// Separate from the domain types on purpose: these are a wire contract that a
// client depends on, and exposing entities would make every internal rename a
// breaking API change.

public sealed record AddressDto(
    Guid Id,
    string Label,
    string Line1,
    string? Line2,
    string City,
    string Postcode,
    string Country,
    bool IsDefaultShipping,
    bool IsDefaultBilling);

public sealed record PreferencesDto(
    string Locale,
    string Currency,
    string Theme,
    bool MarketingEmail,
    bool MarketingSms,
    bool OrderUpdatesEmail,
    bool OrderUpdatesSms);

public sealed record ProfileDto(
    Guid Id,
    string Subject,
    string? Email,
    string? DisplayName,
    string? PhoneNumber,
    PreferencesDto Preferences,
    IReadOnlyList<AddressDto> Addresses);

public sealed record UpdateContactRequest(string? DisplayName, string? PhoneNumber);

public sealed record SaveAddressRequest(
    string Label,
    string Line1,
    string? Line2,
    string City,
    string Postcode,
    string Country,
    bool IsDefaultShipping = false,
    bool IsDefaultBilling = false);

/// <summary>
/// The My Account API.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every endpoint here is scoped to the caller's own profile</b>, resolved from the <c>sub</c> claim —
/// never from a route parameter. That is the strongest form of resource-based authorization available: there
/// is no id to tamper with, because the identifier comes from a signed token rather than from the request.
/// </para>
/// <para>
/// Contrast with orders in Phase 6, where <c>GET /orders/{id}</c> genuinely needs an ownership check because
/// the id is caller-supplied. Where a design can make tampering <i>impossible</i> rather than <i>rejected</i>,
/// it should. See <c>docs/authorization-model.md</c>.
/// </para>
/// </remarks>
public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/profile").WithTags("Profile");

        group.MapGet("/me", GetMyProfile)
            .RequirePermission(Permissions.Profile.ReadOwn)
            .WithSummary("The signed-in user's profile, addresses and preferences");

        group.MapPut("/me/contact", UpdateContact)
            .RequirePermission(Permissions.Profile.WriteOwn)
            .WithSummary("Update display name and phone number");

        group.MapPut("/me/preferences", UpdatePreferences)
            .RequirePermission(Permissions.Profile.WriteOwn)
            .WithSummary("Update locale, currency, theme and notification preferences");

        group.MapPost("/me/addresses", AddAddress)
            .RequirePermission(Permissions.Profile.WriteOwn)
            .WithSummary("Add a saved address");

        group.MapPut("/me/addresses/{addressId:guid}", UpdateAddress)
            .RequirePermission(Permissions.Profile.WriteOwn)
            .WithSummary("Update a saved address");

        group.MapDelete("/me/addresses/{addressId:guid}", RemoveAddress)
            .RequirePermission(Permissions.Profile.WriteOwn)
            .WithSummary("Remove a saved address");

        group.MapPost("/me/addresses/{addressId:guid}/default-shipping", SetDefaultShipping)
            .RequirePermission(Permissions.Profile.WriteOwn)
            .WithSummary("Make an address the default for shipping");

        group.MapPost("/me/addresses/{addressId:guid}/default-billing", SetDefaultBilling)
            .RequirePermission(Permissions.Profile.WriteOwn)
            .WithSummary("Make an address the default for billing");

        return app;
    }

    private static async Task<IResult> GetMyProfile(
        ICurrentUser currentUser,
        UserProfileDbContext db,
        CancellationToken cancellationToken)
    {
        Domain.UserProfile profile = await GetOrProvisionAsync(currentUser, db, cancellationToken);
        return Results.Ok(ToDto(profile));
    }

    private static async Task<IResult> UpdateContact(
        UpdateContactRequest request,
        ICurrentUser currentUser,
        UserProfileDbContext db,
        CancellationToken cancellationToken)
    {
        Domain.UserProfile profile = await GetOrProvisionAsync(currentUser, db, cancellationToken);

        profile.UpdateContactDetails(request.DisplayName, request.PhoneNumber);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToDto(profile));
    }

    private static async Task<IResult> UpdatePreferences(
        PreferencesDto request,
        ICurrentUser currentUser,
        UserProfileDbContext db,
        CancellationToken cancellationToken)
    {
        Domain.UserProfile profile = await GetOrProvisionAsync(currentUser, db, cancellationToken);

        bool marketingEmailWas = profile.Preferences.MarketingEmail;
        bool marketingSmsWas = profile.Preferences.MarketingSms;

        profile.UpdatePreferences(new Preferences(
            request.Locale,
            request.Currency,
            request.Theme,
            request.MarketingEmail,
            request.MarketingSms,
            request.OrderUpdatesEmail,
            request.OrderUpdatesSms));

        // A marketing preference change is a CONSENT event, not just a settings change. Recording it
        // append-only is what makes "when did they opt in, and to what wording?" answerable later.
        if (marketingEmailWas != request.MarketingEmail)
        {
            profile.RecordConsent("marketing-email", request.MarketingEmail, "v1");
        }

        if (marketingSmsWas != request.MarketingSms)
        {
            profile.RecordConsent("marketing-sms", request.MarketingSms, "v1");
        }

        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToDto(profile));
    }

    private static async Task<IResult> AddAddress(
        SaveAddressRequest request,
        ICurrentUser currentUser,
        UserProfileDbContext db,
        CancellationToken cancellationToken)
    {
        Domain.UserProfile profile = await GetOrProvisionAsync(currentUser, db, cancellationToken);

        try
        {
            profile.AddAddress(ToAddress(request));
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException exception)
        {
            // 422, not 400: the request was well-formed, a business rule refused it. Collapsing the two
            // loses information the client could act on.
            return Problem(exception, StatusCodes.Status422UnprocessableEntity);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // Report WHICH entity EF thought it was updating. The default message ("expected to affect 1
            // row(s), but actually affected 0") names nothing, which makes it one of the least actionable
            // exceptions in EF Core.
            string detail = string.Join(
                "; ",
                exception.Entries.Select(e => $"{e.Metadata.Name}={e.State}"));

            return Results.Problem(
                title: "Concurrency failure",
                detail: detail.Length > 0 ? detail : exception.Message,
                statusCode: StatusCodes.Status409Conflict);
        }

        return Results.Ok(ToDto(profile));
    }

    private static async Task<IResult> UpdateAddress(
        Guid addressId,
        SaveAddressRequest request,
        ICurrentUser currentUser,
        UserProfileDbContext db,
        CancellationToken cancellationToken)
    {
        Domain.UserProfile profile = await GetOrProvisionAsync(currentUser, db, cancellationToken);

        try
        {
            profile.UpdateAddress(addressId, ToAddress(request));
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException exception)
        {
            // 404, not 403, when the address is not theirs. A 403 would confirm the address EXISTS, letting
            // an attacker enumerate ids by watching which status comes back.
            return Problem(exception, StatusCodes.Status404NotFound);
        }

        return Results.Ok(ToDto(profile));
    }

    private static async Task<IResult> RemoveAddress(
        Guid addressId,
        ICurrentUser currentUser,
        UserProfileDbContext db,
        CancellationToken cancellationToken)
    {
        Domain.UserProfile profile = await GetOrProvisionAsync(currentUser, db, cancellationToken);

        try
        {
            profile.RemoveAddress(addressId);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException exception)
        {
            return Problem(exception, StatusCodes.Status404NotFound);
        }

        return Results.Ok(ToDto(profile));
    }

    private static Task<IResult> SetDefaultShipping(
        Guid addressId,
        ICurrentUser currentUser,
        UserProfileDbContext db,
        CancellationToken cancellationToken) =>
        SetDefaultAsync(addressId, currentUser, db, shipping: true, cancellationToken);

    private static Task<IResult> SetDefaultBilling(
        Guid addressId,
        ICurrentUser currentUser,
        UserProfileDbContext db,
        CancellationToken cancellationToken) =>
        SetDefaultAsync(addressId, currentUser, db, shipping: false, cancellationToken);

    private static async Task<IResult> SetDefaultAsync(
        Guid addressId,
        ICurrentUser currentUser,
        UserProfileDbContext db,
        bool shipping,
        CancellationToken cancellationToken)
    {
        Domain.UserProfile profile = await GetOrProvisionAsync(currentUser, db, cancellationToken);

        try
        {
            if (shipping)
            {
                profile.SetDefaultShipping(addressId);
            }
            else
            {
                profile.SetDefaultBilling(addressId);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException exception)
        {
            return Problem(exception, StatusCodes.Status404NotFound);
        }

        return Results.Ok(ToDto(profile));
    }

    /// <summary>
    /// Loads the caller's profile, creating it on first use.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Lazy provisioning.</b> A profile is created the first time an authenticated user touches this
    /// service, rather than during registration. Two reasons, in order of importance:
    /// </para>
    /// <list type="number">
    ///   <item><description>A slow or failed profile service must never block a login. Putting a call to us
    ///   inside the authentication path would make logging in only as available as the least available thing
    ///   it touches.</description></item>
    ///   <item><description>It keeps the dependency pointing the right way — Keycloak knows nothing about
    ///   us.</description></item>
    /// </list>
    /// <para>
    /// It is also <b>idempotent</b>: two concurrent first requests both attempt an insert, the unique index
    /// on <c>subject</c> rejects the loser, and it reloads instead. Relying on a check-then-insert without
    /// the index would create duplicate profiles under exactly the race it is meant to prevent.
    /// </para>
    /// </remarks>
    private static async Task<Domain.UserProfile> GetOrProvisionAsync(
        ICurrentUser currentUser,
        UserProfileDbContext db,
        CancellationToken cancellationToken)
    {
        string subject = currentUser.UserId
                         ?? throw new InvalidOperationException("Authenticated request has no subject claim.");

        Domain.UserProfile? profile = await LoadAsync(db, subject, cancellationToken);
        if (profile is not null)
        {
            return profile;
        }

        profile = new Domain.UserProfile(subject, currentUser.Email, currentUser.UserName);
        db.Profiles.Add(profile);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lost the race. The other request created it; detach ours and read theirs.
            db.Entry(profile).State = EntityState.Detached;
            profile = await LoadAsync(db, subject, cancellationToken)
                      ?? throw new InvalidOperationException("Profile insert conflicted but no profile exists.");
        }

        return profile;
    }

    private static Task<Domain.UserProfile?> LoadAsync(
        UserProfileDbContext db,
        string subject,
        CancellationToken cancellationToken) =>
        db.Profiles
            .Include(p => p.Addresses)
            .Include(p => p.Consents)
            .FirstOrDefaultAsync(p => p.Subject == subject, cancellationToken);

    private static Address ToAddress(SaveAddressRequest request) =>
        new(
            request.Label,
            request.Line1,
            request.Line2,
            request.City,
            request.Postcode,
            request.Country,
            request.IsDefaultShipping,
            request.IsDefaultBilling);

    /// <summary>
    /// Hand-written mapping. See <c>docs/adr/0015-manual-mappers-over-automapper.md</c> — renaming a property
    /// breaks the build here rather than silently producing a null in a client.
    /// </summary>
    private static ProfileDto ToDto(Domain.UserProfile profile) =>
        new(
            profile.Id,
            profile.Subject,
            profile.Email,
            profile.DisplayName,
            profile.PhoneNumber,
            new PreferencesDto(
                profile.Preferences.Locale,
                profile.Preferences.Currency,
                profile.Preferences.Theme,
                profile.Preferences.MarketingEmail,
                profile.Preferences.MarketingSms,
                profile.Preferences.OrderUpdatesEmail,
                profile.Preferences.OrderUpdatesSms),
            [.. profile.Addresses
                .OrderByDescending(a => a.IsDefaultShipping)
                .ThenBy(a => a.Label)
                .Select(a => new AddressDto(
                    a.Id, a.Label, a.Line1, a.Line2, a.City, a.Postcode, a.Country,
                    a.IsDefaultShipping, a.IsDefaultBilling))]);

    private static IResult Problem(DomainException exception, int statusCode) =>
        Results.Problem(
            title: statusCode == StatusCodes.Status404NotFound ? "Not found" : "Request rejected",
            detail: exception.Message,
            statusCode: statusCode);
}
