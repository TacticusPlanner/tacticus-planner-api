using Microsoft.Extensions.Options;
using TacticusPlanner.Api.Features.Analytics;
using TacticusPlanner.Domain.Accounts;

namespace TacticusPlanner.Api.Tests;

public sealed class AnalyticsIdentityDeriverTests
{
    private static AnalyticsIdentityDeriver CreateDeriver(byte[]? key = null) =>
        new(Options.Create(new AnalyticsOptions
        {
            IdentityKey = Convert.ToBase64String(key ?? new byte[32]),
        }));

    [Fact]
    public void SameAccountDerivesTheSameIdEveryTime()
    {
        var deriver = CreateDeriver();
        var accountId = AccountId.From(Guid.CreateVersion7());

        var first = deriver.Derive(accountId, AnalyticsIdentityDeriver.PostHogDestination);
        var second = deriver.Derive(accountId, AnalyticsIdentityDeriver.PostHogDestination);

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentAccountsDeriveDifferentIds()
    {
        var deriver = CreateDeriver();

        var first = deriver.Derive(AccountId.From(Guid.CreateVersion7()), AnalyticsIdentityDeriver.PostHogDestination);
        var second = deriver.Derive(AccountId.From(Guid.CreateVersion7()), AnalyticsIdentityDeriver.PostHogDestination);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void IdIsNotTheAccountIdentifierInAnyEncoding()
    {
        var deriver = CreateDeriver();
        var accountId = AccountId.From(Guid.CreateVersion7());

        var analyticsId = deriver.Derive(accountId, AnalyticsIdentityDeriver.PostHogDestination);

        Assert.NotEqual(accountId.Value.ToString(), analyticsId, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(accountId.Value.ToString("N"), analyticsId, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DifferentDestinationsDeriveUnlinkableIds()
    {
        var deriver = CreateDeriver();
        var accountId = AccountId.From(Guid.CreateVersion7());

        var posthog = deriver.Derive(accountId, "posthog");
        var otherDestination = deriver.Derive(accountId, "some-other-vendor");

        Assert.NotEqual(posthog, otherDestination);
    }

    [Fact]
    public void KnowingTheAccountIdWithoutTheKeyCannotProduceTheAnalyticsId()
    {
        var deriverA = CreateDeriver([.. Enumerable.Repeat((byte)0xAA, 32)]);
        var deriverB = CreateDeriver([.. Enumerable.Repeat((byte)0xBB, 32)]);
        var accountId = AccountId.From(Guid.CreateVersion7());

        var idFromA = deriverA.Derive(accountId, AnalyticsIdentityDeriver.PostHogDestination);
        var idFromB = deriverB.Derive(accountId, AnalyticsIdentityDeriver.PostHogDestination);

        Assert.NotEqual(idFromA, idFromB);
    }

    [Fact]
    public void IdsDoNotSortInAccountCreationOrder()
    {
        // v7 GUIDs sort by creation time. If ids preserved that ordering, this would be flaky by
        // construction rather than needing a contrived counter-example - so a handful of accounts
        // created in immediate succession is enough to cover specs/analytics-identity/spec.md's
        // "creation time is not recoverable" scenario.
        var deriver = CreateDeriver();
        var accountsInCreationOrder = Enumerable.Range(0, 20)
            .Select(_ => AccountId.From(Guid.CreateVersion7()))
            .ToList();

        var idsInCreationOrder = accountsInCreationOrder
            .Select(accountId => deriver.Derive(accountId, AnalyticsIdentityDeriver.PostHogDestination))
            .ToList();
        var idsSortedAscending = idsInCreationOrder.OrderBy(id => id, StringComparer.Ordinal).ToList();

        Assert.NotEqual(idsInCreationOrder, idsSortedAscending);
    }
}
