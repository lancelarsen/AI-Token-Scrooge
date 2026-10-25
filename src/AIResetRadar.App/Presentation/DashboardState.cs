using AIResetRadar.Core;
namespace AIResetRadar.App.Presentation;

public sealed record QuotaRow(ProviderAccount Account, QuotaSnapshot Snapshot, QuotaWindow Window)
{
    public bool IsWarning => Window.Capacity() is CapacityState.Low or CapacityState.Critical or CapacityState.Exhausted;
    public string UsedText => Window.Used is { } used ? Window.Unit == UsageUnit.Percent ? $"{used:0.#}% used" : $"{used:N0} used" : "Usage unavailable";
    public string LeftText => Window.Unit == UsageUnit.Percent ? Window.RemainingPercent is { } percent ? $"{percent:0.#}% left" : "Left unavailable" :
        Window.Remaining is { } left ? left < 0 ? $"{-left:N0} over" : $"{left:N0} left" : "Limit unavailable";
    public string SourceNote => Snapshot.Health == SourceHealth.Demo ? "Demo data" : Snapshot.Health != SourceHealth.Healthy ? $"{Snapshot.Health}: {Snapshot.Message}" : Snapshot.UsageAllowed == false ? "Provider reports usage is paused" :
        Window.AllowanceSource == AllowanceProvenance.Manual ? "User-entered allowance · Billing data may lag" : "";
}

public sealed record HorizonEvent(string AccountId, string Title, string Detail, DateTimeOffset At, bool IsExpiry);

public sealed class DashboardState
{
    public List<ProviderAccount> Accounts { get; } = [];
    public Dictionary<string, QuotaSnapshot> Snapshots { get; } = [];
    public bool IsDemo { get; private set; }
    public string Sort { get; set; } = "Next reset";
    public IEnumerable<QuotaRow> Rows
    {
        get
        {
            var rows = Accounts.Where(a => a.Enabled && Snapshots.ContainsKey(a.Id))
                .SelectMany(a => Snapshots[a.Id].Windows.Select(w => new QuotaRow(a, Snapshots[a.Id], w)));
            return Sort switch { "Least remaining" => rows.OrderBy(r => r.Window.RemainingPercent ?? decimal.MaxValue),
                "Account" => rows.OrderBy(r => r.Account.Provider).ThenBy(r => r.Account.Nickname),
                _ => rows.OrderBy(r => r.Window.ResetsAt ?? DateTimeOffset.MaxValue) };
        }
    }

    public IEnumerable<HorizonEvent> Events(DateTimeOffset now) => Rows
        .Where(r => r.Window.ResetsAt > now).Select(r => new HorizonEvent(r.Account.Id,
            $"{r.Account.Provider} / {r.Account.Nickname}", r.Window.Name, r.Window.ResetsAt!.Value, false))
        .Concat(Accounts.Where(a => a.Enabled && Snapshots.ContainsKey(a.Id)).SelectMany(a =>
            Snapshots[a.Id].Reserve?.Credits?.Where(c => c.Status == "available" && c.ExpiresAt > now)
                .Select(c => new HorizonEvent(a.Id, "Earned reset expires", $"{a.Provider} / {a.Nickname}", c.ExpiresAt!.Value, true)) ?? []))
        .OrderBy(e => e.At).Take(5);

    public void LoadDemo(DateTimeOffset now)
    {
        IsDemo = true; Accounts.Clear(); Snapshots.Clear();
        var personal = new ProviderAccount("demo-personal", "OpenAI", "Personal", "demo@example.invalid", "demo");
        var work = new ProviderAccount("demo-work", "OpenAI", "Work", "work@example.invalid", "demo-work");
        var github = new ProviderAccount("demo-github", "GitHub Copilot", "Work", "demo-github", "personal", Allowance: 10000);
        Accounts.AddRange([personal, github, work]);
        Snapshots[personal.Id] = new(personal.Id, personal.Subject, personal.Scope, now,
            [new("codex/primary", "Codex · 5-hour", UsageUnit.Percent, 38, 100, now.AddHours(2).AddMinutes(14), 300),
             new("codex/secondary", "Codex · Weekly", UsageUnit.Percent, 74, 100, now.AddDays(2), 10080)],
            new(2, [new("demo-r1", "available", now.AddDays(4)), new("demo-r2", "available", now.AddDays(9))]), SourceHealth.Demo);
        Snapshots[work.Id] = new(work.Id, work.Subject, work.Scope, now,
            [new("codex/primary", "Codex · 5-hour", UsageUnit.Percent, 19, 100, now.AddHours(3).AddMinutes(44), 300)], null, SourceHealth.Demo);
        Snapshots[github.Id] = new(github.Id, github.Subject, github.Scope, now,
            [new("copilot/monthly", "AI credits · Monthly (10,000)", UsageUnit.Credits, 8200, 10000,
                new DateTimeOffset(now.UtcDateTime.Year, now.UtcDateTime.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1))], null, SourceHealth.Demo);
    }

    public void ClearDemo() { IsDemo = false; Accounts.Clear(); Snapshots.Clear(); }
}
