using AIResetRadar.Core;
using AIResetRadar.Infrastructure;
using Microsoft.Web.WebView2.Core;
using System.Text.Json;

namespace AIResetRadar.App.Services;

/// <summary>Observes the provider's own billing reads in a dedicated browser profile; never exports a session token.</summary>
internal sealed class OpenAiBillingBrowser : IOpenAiBillingBrowser
{
    public Task<OpenAiBillingReading> ReadAsync(string profile, bool interactive, string? expectedSubject,
        string? expectedOrganization, CancellationToken ct) => MainThread.InvokeOnMainThreadAsync(
            () => ReadOnUiAsync(profile, interactive, expectedSubject, expectedOrganization, ct));

    static async Task<OpenAiBillingReading> ReadOnUiAsync(string profile, bool interactive, string? expectedSubject,
        string? expectedOrganization, CancellationToken ct)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = lifetime.Token;
        var window = new Microsoft.UI.Xaml.Window { Title = "Sign In to OpenAI API" };
        var view = new Microsoft.UI.Xaml.Controls.WebView2();
        var connect = new Microsoft.UI.Xaml.Controls.Button { Content = "Track This Organization", IsEnabled = false,
            Margin = new Microsoft.UI.Xaml.Thickness(12) };
        var panel = new Microsoft.UI.Xaml.Controls.Grid();
        panel.RowDefinitions.Add(new() { Height = Microsoft.UI.Xaml.GridLength.Auto });
        panel.RowDefinitions.Add(new() { Height = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star) });
        panel.Children.Add(connect); panel.Children.Add(view); Microsoft.UI.Xaml.Controls.Grid.SetRow(view, 1);
        window.Content = panel;
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 800));
        var closed = false;
        window.Closed += (_, _) => { closed = true; lifetime.Cancel(); };
        var completion = new TaskCompletionSource<OpenAiBillingReading>(TaskCreationOptions.RunContinuationsAsynchronously);
        OpenAiBillingIdentity? identity = null;
        OpenAiBillingReading? candidate = null;
        JsonElement? grants = null;
        string? scope = null;
        string? requestedScope = null;
        var generation = 0;
        var accepting = interactive;
        var pendingReads = new HashSet<Task>();
        try
        {
            if (Environment.GetEnvironmentVariables().Keys.Cast<string>().Any(n =>
                n.StartsWith("WEBVIEW2_", StringComparison.OrdinalIgnoreCase) && n != "WEBVIEW2_DEFAULT_BACKGROUND_COLOR"))
                throw new InvalidDataException("Scrooge cannot isolate API billing while WebView2 environment overrides are active.");
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, profile, new CoreWebView2EnvironmentOptions());
            if (!Path.GetFullPath(environment.UserDataFolder).Equals(Path.GetFullPath(profile), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Windows did not create the requested isolated API billing profile.");
            token.ThrowIfCancellationRequested();
            await view.EnsureCoreWebView2Async(environment);
            token.ThrowIfCancellationRequested();
            var core = view.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.AddWebResourceRequestedFilter("https://api.openai.com/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) =>
            {
                if (e.Request.Uri == OpenAiBilling.GrantsEndpoint && e.Request.Method == "GET")
                {
                    requestedScope = e.Request.Headers.Contains("OpenAI-Organization") ? e.Request.Headers.GetHeader("OpenAI-Organization") : null;
                    grants = null; scope = null; candidate = null; connect.IsEnabled = false;
                }
                // No inference, purchases, changes to billing, or credential creation through this view.
                var uri = new Uri(e.Request.Uri);
                var dashboardRead = e.Request.Method == "GET" &&
                    (uri.AbsolutePath.StartsWith("/v1/dashboard/", StringComparison.Ordinal) || uri.AbsolutePath.StartsWith("/v1/dashapi/", StringComparison.Ordinal));
                var signIn = e.Request.Uri == OpenAiBilling.LoginEndpoint && e.Request.Method == "POST";
                if (!dashboardRead && !signIn && uri.AbsolutePath.StartsWith("/v1/", StringComparison.Ordinal))
                    e.Response = environment.CreateWebResourceResponse(null, 403, "Read Only", "");
            };
            core.NavigationStarting += (_, e) =>
            {
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || uri.Scheme != "https") { e.Cancel = true; return; }
                generation++; identity = null; grants = null; scope = null; candidate = null; connect.IsEnabled = false;
                if (!accepting && uri.Host != "platform.openai.com")
                { e.Cancel = true; completion.TrySetException(Auth()); }
            };
            core.NavigationCompleted += (_, e) =>
            {
                if (!e.IsSuccess && !token.IsCancellationRequested)
                    completion.TrySetException(new ProviderAccessException("The OpenAI billing page could not load.", SourceHealth.Offline));
            };
            core.SourceChanged += (_, _) =>
            {
                if (accepting && !closed && Uri.TryCreate(core.Source, UriKind.Absolute, out var uri))
                    window.Title = $"Sign In to OpenAI API - {uri.GetLeftPart(UriPartial.Authority)}";
            };

            void TryRead()
            {
                if (identity is null || grants is null || scope is null) return;
                var reading = OpenAiBilling.Read(identity, scope, grants.Value, DateTimeOffset.UtcNow);
                if (expectedSubject is not null && reading.Subject != expectedSubject ||
                    expectedOrganization is not null && reading.Organization != expectedOrganization) throw Auth();
                candidate = reading;
                if (accepting) connect.IsEnabled = true;
                else completion.TrySetResult(reading);
            }

            async Task ObserveAsync(CoreWebView2WebResourceResponseReceivedEventArgs e, int started)
            {
                try
                {
                    var isLogin = e.Request.Uri == OpenAiBilling.LoginEndpoint;
                    if (!isLogin && e.Request.Uri != OpenAiBilling.GrantsEndpoint) return;
                    if (!isLogin && e.Request.Method != "GET") return;
                    var requestScope = !isLogin && e.Request.Headers.Contains("OpenAI-Organization")
                        ? e.Request.Headers.GetHeader("OpenAI-Organization") : null;
                    if (e.Response.StatusCode is 401 or 403)
                    {
                        if (!accepting) throw new ProviderAccessException("OpenAI API billing requires sign-in or organization billing access.",
                            e.Response.StatusCode == 401 ? SourceHealth.AuthenticationRequired : SourceHealth.PermissionRequired);
                        return;
                    }
                    if (e.Response.StatusCode == 429)
                        throw new ProviderAccessException("OpenAI asked Scrooge to slow down. Try again later.", SourceHealth.Stale,
                            OpenAiBilling.RetryAt(e.Response.Headers.Contains("Retry-After") ? e.Response.Headers.GetHeader("Retry-After") : null, DateTimeOffset.UtcNow));
                    if (e.Response.StatusCode != 200)
                        throw new ProviderAccessException("OpenAI API billing is temporarily unavailable.", SourceHealth.Offline);
                    using var content = await e.Response.GetContentAsync().AsTask(token);
                    using var stream = content.AsStreamForRead();
                    using var buffer = new MemoryStream();
                    var chunk = new byte[8192];
                    int read;
                    while ((read = await stream.ReadAsync(chunk, token)) != 0)
                    {
                        if (buffer.Length + read > 2_000_000) throw new InvalidDataException("The OpenAI billing response was too large.");
                        buffer.Write(chunk, 0, read);
                    }
                    if (started != generation || token.IsCancellationRequested || !isLogin && requestScope != requestedScope) return;
                    using var document = JsonDocument.Parse(buffer.ToArray());
                    if (isLogin) identity = OpenAiBilling.Identity(document.RootElement);
                    else { grants = document.RootElement.Clone(); scope = requestScope; }
                    TryRead();
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception error)
                {
                    completion.TrySetException(error is ProviderAccessException ? error :
                        new ProviderAccessException("OpenAI did not return readable API billing data. Try signing in again.", SourceHealth.Unsupported));
                }
            }

            core.WebResourceResponseReceived += (sender, e) =>
            {
                if (e.Request.Uri != OpenAiBilling.LoginEndpoint && e.Request.Uri != OpenAiBilling.GrantsEndpoint) return;
                var task = ObserveAsync(e, generation);
                pendingReads.Add(task);
                _ = task.ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() => pendingReads.Remove(task)), TaskScheduler.Default);
            };
            connect.Click += (_, _) =>
            {
                if (candidate is null) return;
                expectedSubject = candidate.Subject; expectedOrganization = candidate.Organization;
                accepting = false; connect.IsEnabled = false; window.AppWindow.Hide();
                // Reload verifies identity and scope again, without exporting cookies or tokens.
                core.Reload();
            };
            if (interactive) window.Activate();
            core.Navigate(OpenAiBilling.Page);
            return await completion.Task.WaitAsync(token);
        }
        finally
        {
            lifetime.Cancel();
            view.Close();
            if (!closed) window.Close();
            if (pendingReads.Count > 0) await Task.WhenAll(pendingReads.ToArray());
        }
    }

    static ProviderAccessException Auth() => new("Sign in to OpenAI Platform again with the original account and organization.", SourceHealth.AuthenticationRequired);
}
