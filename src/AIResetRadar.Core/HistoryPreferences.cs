namespace AIResetRadar.Core;

// Chart visibility is deliberately separate from monitoring and notification preferences.
public sealed record HistoryPreferences(bool Enabled = true, IReadOnlyList<string>? HiddenAccounts = null)
{
    public bool Includes(string accountId) => !(HiddenAccounts ?? []).Contains(accountId);
    public HistoryPreferences WithAccount(string accountId, bool visible) => this with {
        HiddenAccounts = (HiddenAccounts ?? []).Where(id => id != accountId)
            .Concat(visible ? [] : new[] { accountId }).Distinct().ToArray()
    };
    public HistoryPreferences ForAccounts(IEnumerable<string> accountIds)
    {
        var ids = accountIds.ToHashSet();
        return this with { HiddenAccounts = (HiddenAccounts ?? []).Where(ids.Contains).Distinct().ToArray() };
    }
}
