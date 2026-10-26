using AIResetRadar.Core;
namespace AIResetRadar.App.Presentation;

public sealed record QuotaRow(ProviderAccount Account, QuotaSnapshot Snapshot, QuotaWindow Window, decimal Low = 30, decimal Critical = 10)
{
    public string AccountLabel => $"{Account.Nickname} · {PlanLabels.Display(Account.Provider, Snapshot.PlanType)}";
    public bool IsWarning => Window.Capacity(Low, Critical) is CapacityState.Low or CapacityState.Critical or CapacityState.Exhausted;
    public string UsedText => Window.Used is { } used ? Window.Unit == UsageUnit.Percent ? $"{used:0.#}% used" : $"{used:N0} used" : "Usage unavailable";
    public string LeftText => Window.Unit == UsageUnit.Percent ? Window.RemainingPercent is { } percent ? $"{percent:0.#}% left" : "Left unavailable" :
        Window.Remaining is { } left ? left < 0 ? $"{-left:N0} over" : $"{left:N0} left" : "Limit unavailable";
    public string SourceNote => Snapshot.Health == SourceHealth.Demo ? "Demo data" : Snapshot.Health != SourceHealth.Healthy ? $"{Snapshot.Health}: {Snapshot.Message}" : Snapshot.UsageAllowed == false ? "Provider reports usage is paused" :
        Snapshot.Message ?? (Window.AllowanceSource == AllowanceProvenance.Manual ? "User-entered allowance · Billing data may lag" : "");
}

public sealed record HorizonEvent(string AccountId, string Title, string Detail, DateTimeOffset At, bool IsExpiry, string? WindowId = null, string? Provider = null, string? PlanBadge = null);

public sealed class DashboardState
{
    public List<ProviderAccount> Accounts { get; } = [];
    public Dictionary<string, QuotaSnapshot> Snapshots { get; } = [];
    public bool IsDemo { get; private set; }
    public string Sort { get; set; } = "Next reset";
    public RadarSettings Settings { get; set; } = new();
    public string RenderSignature(DateTimeOffset now) => System.Text.Json.JsonSerializer.Serialize(new
    {
        Accounts, Snapshots, Settings, Sort, IsDemo,
        LocalDate = now.ToLocalTime().Date,
        LocalOffset = now.ToLocalTime().Offset,
        Summary = ResetSummary.Evaluate(Accounts, Snapshots, now),
        Events = Events(now).ToArray()
    });
    public IEnumerable<QuotaRow> Rows
    {
        get
        {
            var rows = Accounts.Where(a => a.Enabled && Snapshots.ContainsKey(a.Id))
                .SelectMany(a => Snapshots[a.Id].Windows.Select(w => { var rule = Settings.RuleFor(a.Id, w.Id); return new QuotaRow(a, Snapshots[a.Id], w, rule.LowThreshold!.Value, rule.CriticalThreshold!.Value); }));
            return Sort switch { "Least remaining" => rows.OrderBy(r => r.Window.RemainingPercent ?? decimal.MaxValue),
                "Account" => rows.OrderBy(r => r.Account.Provider).ThenBy(r => r.Account.Nickname),
                _ => rows.OrderBy(r => r.Window.ResetsAt ?? DateTimeOffset.MaxValue) };
        }
    }

    public IEnumerable<HorizonEvent> Events(DateTimeOffset now) => Rows
        .Where(r => r.Window.ResetsAt > now).Select(r => new HorizonEvent(r.Account.Id,
            $"{r.Account.Provider} / {r.AccountLabel}", r.Window.Name, r.Window.ResetsAt!.Value, false, r.Window.Id, r.Account.Provider, TimelineBadge(r.Account)))
        .Concat(Accounts.Where(a => a.Enabled && Snapshots.ContainsKey(a.Id)).SelectMany(a =>
            Snapshots[a.Id].Reserve?.AvailableCount == 0 ? [] : Snapshots[a.Id].Reserve?.Credits?.Where(c => c.Status == "available" && c.ExpiresAt > now)
                .Select(c => new HorizonEvent(a.Id, "Earned reset expires", $"{a.Provider} / {a.Nickname} · {PlanLabels.Display(a.Provider, Snapshots[a.Id].PlanType)}", c.ExpiresAt!.Value, true, Provider: a.Provider, PlanBadge: TimelineBadge(a))) ?? []))
        .OrderBy(e => e.At);

    string? TimelineBadge(ProviderAccount account) => Accounts.Count(a => a.Enabled && a.Provider == account.Provider) > 1
        ? PlanLabels.Display(account.Provider, Snapshots.GetValueOrDefault(account.Id)?.PlanType) : null;

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
    public bool TryEditDemoAccount(ProviderAccount edit)
    {
        if (!IsDemo) return false;
        var index = Accounts.FindIndex(a => a.Id == edit.Id); if (index >= 0) Accounts[index] = Accounts[index] with { Nickname = edit.Nickname, Enabled = edit.Enabled };
        return true;
    }
    public void ApplyDemoScenario(string scenario)
    {
        if (!IsDemo) return;
        foreach (var id in Snapshots.Keys.ToArray())
        {
            var snapshot = Snapshots[id];
            Snapshots[id] = scenario switch
            {
                "reserve-only" => snapshot with { Windows = [], Message = "Demo: source reported reset credits without quota windows." },
                "offline" => snapshot with { Health = SourceHealth.Offline, Message = "Demo: network unavailable; last successful values retained." },
                "auth" => snapshot with { Health = SourceHealth.AuthenticationRequired, Message = "Demo: reconnect this account to resume monitoring." },
                "permission" => snapshot with { Health = SourceHealth.PermissionRequired, Message = "Demo: billing read access is unavailable." },
                "unknown" => snapshot with { Windows = snapshot.Windows.Select(w => w with { Used = null, Limit = null }).ToArray() },
                "exhausted" => snapshot with { Windows = snapshot.Windows.Select(w => w with { Used = w.Unit == UsageUnit.Percent ? 100 : w.Limit }).ToArray() },
                "due" => snapshot with { Windows = snapshot.Windows.Select(w => w with { ResetsAt = DateTimeOffset.UtcNow.AddMinutes(-1) }).ToArray() },
                "partial" when snapshot.Reserve is not null => snapshot with { Reserve = new(3, [new("sample-credit", "available", DateTimeOffset.UtcNow.AddMinutes(45))]) },
                _ => snapshot
            };
        }
    }
}

public sealed record HorizonDay(DateOnly Date, bool IsToday, HorizonEvent[] Events);

public static class HorizonTimeline
{
    public static HorizonDay[] Build(IEnumerable<HorizonEvent> source, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var events = source.Where(e => e.At >= now).OrderBy(e => e.At)
            .GroupBy(e => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.At, zone).DateTime))
            .ToDictionary(g => g.Key, g => g.ToArray());
        var last = events.Count == 0 ? today : events.Keys.Max();
        return Enumerable.Range(0, last.DayNumber - today.DayNumber + 1)
            .Select(i => new HorizonDay(today.AddDays(i), i == 0, events.GetValueOrDefault(today.AddDays(i)) ?? []))
            .ToArray();
    }
}
