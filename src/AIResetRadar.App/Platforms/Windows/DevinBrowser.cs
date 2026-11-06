using AIResetRadar.Core;
using AIResetRadar.Infrastructure;
using Microsoft.Web.WebView2.Core;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIResetRadar.App.Services;

internal sealed class DevinBrowser : IDevinBrowser
{
    public Task<DevinReading> ReadAsync(Uri origin, string profile, bool interactive, CancellationToken ct) =>
        MainThread.InvokeOnMainThreadAsync(() => ReadOnUiAsync(origin, profile, interactive, ct));

    static async Task<DevinReading> ReadOnUiAsync(Uri origin, string profile, bool interactive, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = lifetime.Token;
        var window = new Microsoft.UI.Xaml.Window { Title = "Sign In with Devin" };
        var view = new Microsoft.UI.Xaml.Controls.WebView2();
        window.Content = view;
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 760));
        bool closed = false;
        window.Closed += (_, _) => { closed = true; lifetime.Cancel(); };
        try
        {
            if (Environment.GetEnvironmentVariables().Keys.Cast<string>().Any(name =>
                name.StartsWith("WEBVIEW2_", StringComparison.OrdinalIgnoreCase) &&
                name is not "WEBVIEW2_DEFAULT_BACKGROUND_COLOR"))
                throw new InvalidDataException("Scrooge cannot isolate Devin while WebView2 environment overrides are active.");
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, profile, new CoreWebView2EnvironmentOptions());
            if (!Path.GetFullPath(environment.UserDataFolder).Equals(Path.GetFullPath(profile), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Windows did not create the requested isolated Devin profile.");
            token.ThrowIfCancellationRequested();
            await view.EnsureCoreWebView2Async(environment);
            token.ThrowIfCancellationRequested();
            var core = view.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.NavigationStarting += (_, e) =>
            {
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var target) || target.Scheme != "https") e.Cancel = true;
            };
            core.SourceChanged += (_, _) =>
            {
                if (!closed && interactive && Uri.TryCreate(core.Source, UriKind.Absolute, out var current))
                    window.Title = $"Sign In with Devin — {current.GetLeftPart(UriPartial.Authority)}";
            };
            if (interactive) window.Activate();
            var subject = await IdentityAsync(core, origin, interactive, token);
            if (interactive) window.AppWindow.Hide();
            var analytics = new Uri(origin, "/settings/my-analytics");
            var raw = await ReadPageAsync<string[]>(core, analytics, UsageScript, false, token);
            var verified = await IdentityAsync(core, origin, false, token);
            if (subject != verified) throw new ProviderAccessException("The Devin account changed during the usage check.", SourceHealth.AuthenticationRequired);
            return DevinUsage.Parse(subject, raw, DateTimeOffset.UtcNow);
        }
        finally
        {
            view.Close();
            if (!closed) window.Close();
        }
    }

    static async Task<string> IdentityAsync(CoreWebView2 core, Uri origin, bool interactive, CancellationToken ct)
    {
        var id = await ReadPageAsync<string>(core, new Uri(origin, "/settings/preferences"),
            "document.querySelector('#profile-user-id p')?.innerText?.trim() || null", interactive, ct);
        if (!Regex.IsMatch(id, @"\A[A-Za-z0-9:|_-]{1,160}\z"))
            throw new ProviderAccessException("Devin did not supply a recognizable user ID.", SourceHealth.Unsupported);
        return id;
    }

    static async Task<T> ReadPageAsync<T>(CoreWebView2 core, Uri target, string expression, bool allowSignIn, CancellationToken ct) where T : class
    {
        // Check again inside the document so a navigation race cannot read another origin or an SSO form.
        var script = "location.origin === " + JsonSerializer.Serialize(target.GetLeftPart(UriPartial.Authority))
            + " && location.pathname === " + JsonSerializer.Serialize(target.AbsolutePath) + " ? (" + expression + ") : null";
        var loading = true;
        CoreWebView2NavigationCompletedEventArgs? finished = null;
        void Started(CoreWebView2 _, CoreWebView2NavigationStartingEventArgs e) { loading = true; finished = null; }
        void Completed(CoreWebView2 _, CoreWebView2NavigationCompletedEventArgs e) { loading = false; finished = e; }
        core.NavigationStarting += Started;
        core.NavigationCompleted += Completed;
        try
        {
            core.Navigate(target.AbsoluteUri);
            DateTimeOffset? arrived = null;
            bool retriedLanding = false;
            while (true)
            {
                await Task.Delay(350, ct);
                if (loading || finished is null) continue;
                if (finished.HttpStatusCode == 403)
                    throw new ProviderAccessException("Your account does not have access to this Devin page.", SourceHealth.PermissionRequired);
                if (finished.HttpStatusCode == 429)
                    throw new ProviderAccessException("Devin asked Scrooge to slow down. Try again later.", SourceHealth.Stale, DateTimeOffset.UtcNow.AddMinutes(15));
                if (!finished.IsSuccess)
                    throw new ProviderAccessException("The Devin page could not load. Check your connection and try again.", SourceHealth.Offline);
                if (!Uri.TryCreate(core.Source, UriKind.Absolute, out var page)) continue;
                if (page.GetLeftPart(UriPartial.Path) != target.AbsoluteUri)
                {
                    arrived = null;
                    if (page.Host == target.Host && page.AbsolutePath.StartsWith("/org/", StringComparison.Ordinal) && allowSignIn && !retriedLanding)
                    {
                        retriedLanding = true;
                        core.Navigate(target.AbsoluteUri);
                        continue;
                    }
                    if (allowSignIn) continue;
                    var login = page.Host != target.Host || page.AbsolutePath.StartsWith("/auth/", StringComparison.Ordinal);
                    throw new ProviderAccessException(login ? "Your Devin sign-in expired. Use Sign In Again."
                        : "Your account could not open Devin My Analytics.", login ? SourceHealth.AuthenticationRequired : SourceHealth.PermissionRequired);
                }
                arrived ??= DateTimeOffset.UtcNow;
                var json = await core.ExecuteScriptAsync(script);
                ct.ThrowIfCancellationRequested();
                if (json.Length is > 0 and <= 2048 && JsonSerializer.Deserialize<T>(json) is { } data)
                {
                    // Devin loads the monthly summary and the quota meter independently.
                    if (data is not string[] meter || meter is [_, { Length: > 0 }, { Length: > 0 }, _]
                        || DateTimeOffset.UtcNow - arrived > TimeSpan.FromSeconds(12)) return data;
                }
                if (DateTimeOffset.UtcNow - arrived > TimeSpan.FromSeconds(14))
                    throw new ProviderAccessException("Devin did not show the expected account or usage information. Check My Analytics in your workspace.", SourceHealth.Unsupported);
            }
        }
        finally
        {
            core.NavigationStarting -= Started;
            core.NavigationCompleted -= Completed;
        }
    }

    const string UsageScript = """
        (() => {
            const text = document.querySelector('#main-content')?.innerText ?? '';
            const amount = '([0-9][0-9,.]*)';
            const numbers = text.match(new RegExp(amount + '\\s*\\/\\s*' + amount + '\\s*\\(\\s*[0-9.]+\\s*%\\s*\\)\\s*ACUs consumed since', 'i'));
            const monthly = text.match(/Monthly ACU limit usage\s+([0-9][0-9,.]*)\s*\/\s*([0-9][0-9,.]*)/i);
            const meter = numbers ?? monthly;
            const used = text.match(/Monthly ACUs consumed\s+([0-9][0-9,.]*)/i);
            if (!meter && !used) return null;
            const reset = text.match(/Resets\s+(\d{1,2}\/\d{1,2})\s+at\s+(\d{1,2}:\d{2}\s*[AP]M)/i);
            return [meter?.[1] ?? used[1], meter?.[2] ?? '', reset ? reset[1] + ' ' + reset[2] : '',
                Intl.DateTimeFormat().resolvedOptions().timeZone ?? ''];
        })()
        """;
}
