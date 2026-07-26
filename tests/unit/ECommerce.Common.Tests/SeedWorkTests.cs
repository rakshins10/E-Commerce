using ECommerce.Common.Exceptions;
using ECommerce.Common.Guards;
using ECommerce.Common.Pagination;
using ECommerce.Common.Results;
using ECommerce.Common.SeedWork;
using FluentAssertions;

namespace ECommerce.Common.Tests;

// Test doubles. Deliberately minimal - these exercise the seedwork contracts, not a domain.
file sealed class TestEntity : Entity<Guid>
{
    public TestEntity(Guid id) => Id = id;

    public string Name { get; init; } = string.Empty;

    public void RaiseSomethingHappened() => RaiseDomainEvent(new SomethingHappened(DateTimeOffset.UtcNow));
}

file sealed class OtherEntity : Entity<Guid>
{
    public OtherEntity(Guid id) => Id = id;
}

file sealed record SomethingHappened(DateTimeOffset OccurredAt) : IDomainEvent;

file sealed class Money : ValueObject
{
    public Money(decimal amount, string currency)
    {
        Amount = Guard.AgainstNegative(amount);
        Currency = Guard.AgainstNullOrWhiteSpace(currency);
    }

    public decimal Amount { get; }

    public string Currency { get; }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;

        // Normalised here rather than at the comparison site - the whole point of centralising the
        // equality contract in one method.
        yield return Currency.ToUpperInvariant();
    }
}

public class EntityTests
{
    [Fact]
    public void Entities_with_the_same_id_are_equal_even_when_their_attributes_differ()
    {
        var id = Guid.CreateVersion7();
        var a = new TestEntity(id) { Name = "one" };
        var b = new TestEntity(id) { Name = "two" };

        // Identity equality, not structural. This is the property that makes an entity an entity, and the
        // reason it must not be a record.
        a.Should().Be(b);
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Entities_of_different_types_are_never_equal_even_with_the_same_id()
    {
        var id = Guid.CreateVersion7();

        new TestEntity(id).Equals(new OtherEntity(id)).Should().BeFalse();
    }

    [Fact]
    public void Transient_entities_are_equal_only_by_reference()
    {
        var a = new TestEntity(Guid.Empty);
        var b = new TestEntity(Guid.Empty);

        a.IsTransient().Should().BeTrue();

        // Two unsaved entities have no identity yet, so treating them as equal would collapse distinct
        // objects in a set - a genuinely nasty bug when adding several new items to an aggregate.
        a.Should().NotBe(b);
        a.Should().Be(a);
    }

    [Fact]
    public void Domain_events_are_collected_and_can_be_cleared()
    {
        var entity = new TestEntity(Guid.CreateVersion7());

        entity.DomainEvents.Should().BeEmpty();

        entity.RaiseSomethingHappened();
        entity.DomainEvents.Should().ContainSingle();

        // Cleared by the infrastructure after dispatch, so a long-lived tracked instance cannot publish the
        // same event twice.
        entity.ClearDomainEvents();
        entity.DomainEvents.Should().BeEmpty();
    }
}

public class ValueObjectTests
{
    [Fact]
    public void Value_objects_with_the_same_components_are_equal()
    {
        new Money(10.50m, "GBP").Should().Be(new Money(10.50m, "GBP"));
    }

    [Fact]
    public void Equality_uses_the_normalisation_declared_in_GetEqualityComponents()
    {
        new Money(10m, "gbp").Should().Be(new Money(10m, "GBP"));
    }

    [Fact]
    public void Value_objects_with_different_components_are_not_equal()
    {
        new Money(10m, "GBP").Should().NotBe(new Money(10m, "USD"));
        new Money(10m, "GBP").Should().NotBe(new Money(11m, "GBP"));
    }

    [Fact]
    public void A_value_object_guards_its_own_validity_on_construction()
    {
        // "Parse, don't validate": if you are holding one, it is valid, so nothing downstream needs to check.
        FluentActions.Invoking(() => new Money(-1m, "GBP")).Should().Throw<DomainException>();
        FluentActions.Invoking(() => new Money(1m, "  ")).Should().Throw<DomainException>();
    }
}

public class GuardTests
{
    [Fact]
    public void Guard_messages_name_the_expression_at_the_call_site()
    {
        int quantity = 0;

        // CallerArgumentExpression means the message survives a rename, unlike a hand-typed string.
        FluentActions.Invoking(() => Guard.AgainstNonPositive(quantity))
            .Should().Throw<DomainException>()
            .WithMessage("*quantity*");
    }

    [Fact]
    public void AgainstNegative_allows_zero_but_AgainstNonPositive_does_not()
    {
        // The distinction matters: a balance of zero is legitimate, a quantity of zero is not.
        Guard.AgainstNegative(0m).Should().Be(0m);
        FluentActions.Invoking(() => Guard.AgainstNonPositive(0)).Should().Throw<DomainException>();
    }
}

public class ResultTests
{
    [Fact]
    public void A_successful_result_exposes_its_value()
    {
        Result<int> result = Result.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Reading_the_value_of_a_failed_result_throws_rather_than_returning_null()
    {
        Result<int> result = Result.Failure<int>(Error.NotFound("order.not_found", "No such order."));

        result.IsFailure.Should().BeTrue();

        // Throwing means forgetting to check IsSuccess fails loudly and immediately, instead of producing a
        // default that surfaces somewhere unrelated.
        FluentActions.Invoking(() => result.Value).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_result_cannot_be_constructed_in_a_contradictory_state()
    {
        FluentActions.Invoking(() => Result.Failure(Error.None)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Match_forces_both_branches_to_be_handled()
    {
        Result<int> success = Result.Success(1);
        Result<int> failure = Result.Failure<int>(Error.Conflict("x", "y"));

        success.Match(value => $"ok:{value}", error => $"err:{error.Code}").Should().Be("ok:1");
        failure.Match(value => $"ok:{value}", error => $"err:{error.Code}").Should().Be("err:x");
    }
}

public class PaginationTests
{
    [Fact]
    public void Page_size_is_clamped_to_the_maximum()
    {
        // An unclamped page size is a denial-of-service vector: ?pageSize=10000000 is a free way to exhaust
        // server memory.
        new PageRequest(Page: 1, PageSize: 10_000).Normalise().PageSize.Should().Be(PageRequest.MaxPageSize);
    }

    [Fact]
    public void Invalid_page_and_size_fall_back_to_sensible_defaults()
    {
        PageRequest normalised = new PageRequest(Page: -5, PageSize: 0).Normalise();

        normalised.Page.Should().Be(1);
        normalised.PageSize.Should().Be(PageRequest.DefaultPageSize);
        normalised.Skip.Should().Be(0);
    }

    [Fact]
    public void Paging_metadata_is_computed_from_the_total()
    {
        var page = new PagedResult<string>(["a", "b"], Page: 2, PageSize: 2, TotalCount: 5);

        page.TotalPages.Should().Be(3);
        page.HasPrevious.Should().BeTrue();
        page.HasNext.Should().BeTrue();
    }
}
