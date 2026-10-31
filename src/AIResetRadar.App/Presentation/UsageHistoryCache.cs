using AIResetRadar.Core;

namespace AIResetRadar.App.Presentation;

/// <summary>Immutable per-account run cache. Extended runs replace their former endpoints.</summary>
public sealed record UsageHistoryCache(ProviderAccount Account, DateTimeOffset From, long Revision, HistoryInterval[] Intervals)
{
    public bool Covers(ProviderAccount account, DateTimeOffset from) =>
        Account.Id == account.Id && Account.Provider == account.Provider && Account.Subject == account.Subject &&
        Account.ScopeEquals(account.Scope) && From <= from;

    public UsageHistoryCache Append(ProviderAccount account, DateTimeOffset from, HistoryPage page)
    {
        var runs = Intervals.Concat(page.Intervals).GroupBy(i => i.Key).Select(g => g.Last()).OrderBy(i => i.First.ObservedAt).ToArray();
        var preceding = runs.LastOrDefault(i => i.Last.ObservedAt < from);
        return new(account, from, page.Revision, runs.Where(i => i.Last.ObservedAt >= from || ReferenceEquals(i, preceding)).ToArray());
    }

    public HistorySeries Build(DateTimeOffset through) =>
        UsageHistory.Build(Account, Intervals.SelectMany(i => i.Endpoints()), From, through);
}
