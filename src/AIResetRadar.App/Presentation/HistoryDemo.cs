using AIResetRadar.Core;

namespace AIResetRadar.App.Presentation;

/// <summary>Explicit demo-only observations, never persisted or mixed with connected accounts.</summary>
public static class HistoryDemo
{
    public static HistorySeries[] Build(ProviderAccount[] accounts,
        IReadOnlyDictionary<string, QuotaSnapshot> snapshots, DateTimeOffset now, TimeZoneInfo zone)
    {
        var start = new HorizonViewport(now, null, zone, 700).StartAt;
        List<HistorySeries> result = [];
        foreach (var account in accounts.Where(a => a.Enabled))
        {
            if (!snapshots.TryGetValue(account.Id, out var current) || current.Health != SourceHealth.Demo ||
                !current.BelongsTo(account) || UsageHistory.SelectWindow(account.Provider, current.Windows).Window is not { } window ||
                window.RemainingPercent is not { } endRemaining) continue;

            var index = result.Count;
            var refreshAt = window.ResetsAt is { } next
                ? account.Provider == "GitHub Copilot" ? next.AddMonths(-1) : next.AddDays(-7)
                : (DateTimeOffset?)null;
            if (refreshAt <= start || refreshAt >= now) refreshAt = null;
            var expiryAt = account.Provider == "OpenAI" && index == 0 ? start.AddDays(4.25) : (DateTimeOffset?)null;
            var startRemaining = Math.Min(99, Math.Max(78 - index * 4, endRemaining + 35));
            var restoredRemaining = Math.Min(99, Math.Max(endRemaining, index % 2 == 0 ? 91 : 96));
            var records = new List<QuotaSnapshot>();
            var steps = (int)Math.Ceiling((now - start).TotalMinutes / 5);
            // Synthetic work sessions: idle nights, uneven bursts, and small reported increments.
            // Shape is informed by aggregate local observations, never copied account readings.
            var activity = new double[steps + 1];
            for (var i = 1; i <= steps; i++)
            {
                var local = TimeZoneInfo.ConvertTime(start.AddMinutes(i * 5), zone);
                var slot = local.Hour * 12 + local.Minute / 5;
                var seed = unchecked((uint)(local.DayOfYear * 7919 + slot * 104729 + index * 15485863));
                seed ^= seed >> 16; seed *= 0x7feb352d; seed ^= seed >> 15;
                var session = (slot + index * 17 + local.DayOfYear * 7) % 91;
                var active = local.Hour >= 8 + index % 3 && local.Hour < 23 - index % 2 && session < 33;
                var weight = active && seed % 7 < 3 ? 1 + (seed / 7 % 9) / 3d : 0;
                activity[i] = activity[i - 1] + weight;
            }
            double At(DateTimeOffset time) => activity[Math.Clamp((int)Math.Ceiling((time - start).TotalMinutes / 5), 0, steps)];
            double Progress(DateTimeOffset time, DateTimeOffset from, DateTimeOffset to) =>
                At(to) <= At(from) ? Fraction(time, from, to) : (At(time) - At(from)) / (At(to) - At(from));
            for (var step = 0; step <= steps; step++)
            {
                var at = step == steps ? now : start.AddMinutes(step * 5);
                // Make missing observations honest and visibly different from an unused account.
                if (account.Provider == "Claude" && at > start.AddDays(4).AddHours(3) && at < start.AddDays(4).AddHours(6)) continue;
                decimal remaining;
                if (refreshAt is { } refresh)
                {
                    remaining = at < refresh
                        ? Lerp(startRemaining, 24 + index * 3, Progress(at, start, refresh))
                        : Lerp(restoredRemaining, endRemaining, Progress(at, refresh, now));
                }
                else remaining = Lerp(startRemaining, endRemaining, Progress(at, start, now));
                remaining = at == now ? endRemaining : decimal.Round(Math.Clamp(remaining, 0, 100), account.Provider == "GitHub Copilot" ? 1 : 0);
                var used = window.Unit == UsageUnit.Percent ? 100 - remaining : window.Limit * (100 - remaining) / 100;
                var period = window with { Used = used, ResetsAt = refreshAt is { } r && at < r ? r : window.ResetsAt };
                var credits = expiryAt is { } expiry && at <= expiry
                    ? new ResetReserve(1, [new($"demo-history-expiry:{account.Id}", "available", expiry)])
                    : null;
                records.Add(current with { ObservedAt = at, ProviderDataAt = at, Windows = [period], Reserve = credits,
                    Health = SourceHealth.Demo, Message = "Demo History" });
            }
            result.Add(UsageHistory.Build(account, records, start, now, TimeSpan.FromMinutes(5)));
        }
        return result.ToArray();
    }

    static decimal Lerp(decimal start, decimal end, double fraction) => start + (end - start) * (decimal)Math.Clamp(fraction, 0, 1);
    static double Fraction(DateTimeOffset at, DateTimeOffset start, DateTimeOffset end) =>
        end <= start ? 1 : (at - start).TotalSeconds / (end - start).TotalSeconds;
}
