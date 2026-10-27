namespace AIResetRadar.Core;

public enum AccountSortMode { NextReset, CustomOrder, MostRemaining, LeastRemaining }

public static class AccountOrdering
{
    public static string[] Normalize(IEnumerable<string>? saved, IEnumerable<string> connected)
    {
        var ids = connected.Distinct().ToArray();
        var live = ids.ToHashSet();
        return (saved ?? []).Where(live.Contains).Concat(ids).Distinct().ToArray();
    }

    public static string[] Move(IReadOnlyList<string> order, string accountId, int targetIndex)
    {
        var result = order.ToList();
        var index = result.IndexOf(accountId);
        if (index < 0 || result.Count == 0) return result.ToArray();
        result.RemoveAt(index);
        result.Insert(Math.Clamp(targetIndex, 0, result.Count), accountId);
        return result.ToArray();
    }
}
