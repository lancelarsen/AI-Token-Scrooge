namespace AIResetRadar.Core;

public static class RefreshSchedule
{
    public static DateTimeOffset AfterSuccess(QuotaSnapshot snapshot, string provider, DateTimeOffset now)
    {
        var next = now.AddSeconds(provider == "OpenAI" ? 120 : 300);
        var boundaries = snapshot.Windows.Select(w => w.ResetsAt)
            .Concat(snapshot.Reserve?.Credits?.Where(c => c.Status == "available").Select(c => c.ExpiresAt) ?? []);
        foreach (var boundary in boundaries)
            if (boundary > now && boundary < next) next = boundary.Value;
        // A provider can keep returning an elapsed boundary; ordinary polling bounds those retries.
        return next;
    }
}
