using System.Diagnostics;
using AIResetRadar.Core;
using AIResetRadar.Infrastructure;
using AIResetRadar.App.Services;
namespace AIResetRadar.App.Views;

public sealed partial class ConnectAccountPage : ContentPage
{
    readonly Entry nickname = new() { Placeholder = "Account nickname (Personal, Work…)", Text = "Personal" };
    readonly Entry token = new() { Placeholder = "Fine-grained personal access token", IsPassword = true };
    readonly Entry allowance = new() { Placeholder = "Monthly allowance (optional; user-entered)", Keyboard = Keyboard.Numeric };
    readonly Entry organization = new() { Placeholder = "Organization billing owner (optional)" };
    readonly Picker unit = new() { ItemsSource = new[] { "AI credits", "Legacy premium requests" }, SelectedIndex = 0, TextColor = Colors.White };
    readonly Label status = new() { TextColor = Color.FromArgb("#B6B8C8") };
    readonly Label explanation = new() { TextColor = Color.FromArgb("#B6B8C8") };
    readonly Button connect = new() { Text = "Connect", BackgroundColor = Color.FromArgb("#7169CA") };
    readonly CancellationTokenSource lifetime = new();
    readonly bool codex;
    readonly Func<ProviderAccount, QuotaSnapshot, Task> connected;
    CodexConnection? connection;
    readonly string id;
    readonly string credentialId = Guid.NewGuid().ToString("N");
    readonly ProviderAccount? reconnect;
    string? loginId;
    bool signingIn;
    bool connecting;
    bool committed, committing;
    ProviderAccount? pendingAccount;

    public ConnectAccountPage(bool codex, Func<ProviderAccount, QuotaSnapshot, Task> connected, ProviderAccount? reconnect = null)
    {
        this.reconnect = reconnect; id = reconnect?.Id ?? Guid.NewGuid().ToString("N");
        if (reconnect is not null)
        {
            nickname.Text = reconnect.Nickname; allowance.Text = reconnect.Allowance?.ToString();
            organization.Text = reconnect.Scope.StartsWith("organization:") ? reconnect.Scope[13..] : ""; organization.IsReadOnly = true;
            unit.SelectedIndex = reconnect.Unit == UsageUnit.Requests ? 1 : 0; unit.IsEnabled = false;
        }
        this.codex = codex; this.connected = connected; Title = codex ? "Connect OpenAI Codex" : "Connect GitHub Copilot";
        BackgroundColor = Color.FromArgb("#141519");
        var cancel = new Button { Text = "Cancel" }; cancel.Clicked += async (_, _) => await CloseAsync();
        var form = new VerticalStackLayout { Padding = 32, Spacing = 18, MaximumWidthRequest = 640, HorizontalOptions = LayoutOptions.Center };
        form.Children.Add(new Label { Text = Title, FontSize = 26, FontFamily = "Segoe UI Semibold" });
        explanation.Text = "Sign in through OpenAI in your browser. Radar keeps this account in its own protected Codex profile and only reads usage limits.";
        form.Children.Add(explanation);
        var help = new Button { Text = "Usage and access help" };
        help.Clicked += async (_, _) => {
            var uri = ProviderLinks.Help(codex ? "OpenAI" : "GitHub Copilot");
            try { if (uri is not null) await Launcher.Default.OpenAsync(uri); } catch { status.Text = "Help could not open. Check your default browser."; }
        };
        form.Children.Add(help);
        form.Children.Add(nickname);
        if (!codex) ConfigureGitHubForm(form);
        else connect.Text = "Sign in with OpenAI";
        form.Children.Add(status); form.Children.Add(connect);
        if (!codex) form.Children.Add(githubPrompt);
        form.Children.Add(cancel);
        connect.Clicked += async (_, _) => await ConnectAsync();
        Content = new ScrollView { Content = form };
    }

    async Task ConnectAsync()
    {
        if (connecting) return;
        if (string.IsNullOrWhiteSpace(nickname.Text)) { status.Text = "Enter an account nickname."; return; }
        connecting = true; connect.IsEnabled = false;
        try
        {
            if (codex)
            {
                if (signingIn) { await FinishCodexAsync(); return; }
                if (connection is not null) { await connection.DisposeAsync(); connection = null; }
                connection = await AppServices.Current.Monitor.OpenCodexAsync(credentialId, lifetime.Token);
                connection.Notification += notification =>
                {
                    if (ProviderMapping.Text(notification, "method") == "account/login/completed")
                        MainThread.BeginInvokeOnMainThread(async () => { if (lifetime.IsCancellationRequested) return; status.Text = "Sign-in completed. Checking account…"; await FinishCodexAsync(); });
                };
                var login = await connection.CallAsync("account/login/start", new { type = "chatgpt" }, lifetime.Token);
                loginId = ProviderMapping.Text(login, "loginId");
                var url = ProviderMapping.Text(login, "authUrl");
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !(uri.Host == "auth.openai.com" || uri.Host.EndsWith(".openai.com", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("OpenAI returned an unexpected sign-in address.");
                signingIn = true; status.Text = "Complete sign-in in your browser. This window will confirm the account when you finish.";
                connect.Text = "Check sign-in";
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            else
            {
                await ConnectGitHubAsync();
            }
        }
        catch (Exception error) { status.Text = SafeMessage(error); }
        finally { committing = false; connecting = false; connect.IsEnabled = true; }
    }
    bool finishing;
    async Task FinishCodexAsync()
    {
        if (connection is null || finishing) return; finishing = true;
        try
        {
            var result = await AppServices.Current.Monitor.CompleteCodexAsync(connection, id, nickname.Text.Trim(), lifetime.Token, credentialId);
            var accepted = reconnect is null ? result.Account : ReconnectPolicy.Accept(reconnect, result.Account);
            lifetime.Token.ThrowIfCancellationRequested();
            committing = true; await connected(accepted, result.Snapshot); committed = true; committing = false; await CloseAsync();
        }
        catch (Exception error) { status.Text = SafeMessage(error); }
        finally { committing = false; finishing = false; }
    }
    bool closing;
    Task? cleanup;
    protected override bool OnBackButtonPressed() { _ = CloseAsync(); return true; }
    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        if (args.NewHandler is null) _ = CleanupAsync();
        base.OnHandlerChanging(args);
    }
    async Task CloseAsync()
    {
        if (closing || committing) return; closing = true;
        await CleanupAsync();
        await Navigation.PopModalAsync();
    }
    Task CleanupAsync() => cleanup ??= CleanupCoreAsync();
    async Task CleanupCoreAsync()
    {
        await lifetime.CancelAsync(); token.Text = ""; githubChallenge = null; githubCode.Text = "";
        if (connection is not null)
        {
            var owned = connection; connection = null;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { if (loginId is not null) await owned.CallAsync("account/login/cancel", new { loginId }, timeout.Token); } catch { }
            if (!committed) { try { await owned.CallAsync("account/logout", ct: timeout.Token); } catch { } }
            await owned.DisposeAsync();
        }
        if (!committed && pendingAccount is not null)
        { try { await AppServices.Current.Monitor.DisconnectAsync(pendingAccount); } catch { } pendingAccount = null; }
    }
    public static string SafeMessage(Exception error) => error switch {
        ProviderAccessException or InvalidDataException or FileNotFoundException => error.Message,
        OperationCanceledException => "The connection timed out or was canceled. Try again.",
        _ => "Could not complete the connection. Check your connection and provider sign-in, then retry."
    };
}
