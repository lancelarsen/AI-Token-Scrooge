using System.Diagnostics;
using AIResetRadar.Core;
using AIResetRadar.Infrastructure;
using AIResetRadar.App.Services;
namespace AIResetRadar.App.Views;

public sealed class ConnectAccountPage : ContentPage
{
    readonly Entry nickname = new() { Placeholder = "Account nickname (Personal, Work…)", Text = "Personal" };
    readonly Entry token = new() { Placeholder = "Fine-grained personal access token", IsPassword = true };
    readonly Entry allowance = new() { Placeholder = "Monthly allowance (optional; user-entered)", Keyboard = Keyboard.Numeric };
    readonly Entry organization = new() { Placeholder = "Organization billing owner (optional)" };
    readonly Picker unit = new() { ItemsSource = new[] { "AI credits", "Legacy premium requests" }, SelectedIndex = 0, TextColor = Colors.White };
    readonly Label status = new() { TextColor = Color.FromArgb("#B6B8C8") };
    readonly Button connect = new() { Text = "Connect", BackgroundColor = Color.FromArgb("#7169CA") };
    readonly CancellationTokenSource lifetime = new();
    readonly bool codex;
    readonly Func<ProviderAccount, QuotaSnapshot, Task> connected;
    CodexConnection? connection;
    readonly string id = Guid.NewGuid().ToString("N");
    string? loginId;
    bool signingIn;

    public ConnectAccountPage(bool codex, Func<ProviderAccount, QuotaSnapshot, Task> connected)
    {
        this.codex = codex; this.connected = connected; Title = codex ? "Connect OpenAI Codex" : "Connect GitHub Copilot";
        BackgroundColor = Color.FromArgb("#141519");
        var cancel = new Button { Text = "Cancel" }; cancel.Clicked += async (_, _) => await CloseAsync();
        var form = new VerticalStackLayout { Padding = 32, Spacing = 18, MaximumWidthRequest = 640, HorizontalOptions = LayoutOptions.Center };
        form.Children.Add(new Label { Text = Title, FontSize = 26, FontFamily = "Segoe UI Semibold" });
        form.Children.Add(new Label { Text = codex ? "Sign in through OpenAI in your browser. Radar keeps this account in its own protected Codex profile and only reads usage limits." :
            "Use a GitHub fine-grained token with Plan read permission for personal billing. Organization reports require authorized organization access. Your token is protected with Windows DPAPI.", TextColor = Color.FromArgb("#B6B8C8") });
        form.Children.Add(nickname);
        if (!codex) { form.Children.Add(token); form.Children.Add(unit); form.Children.Add(allowance); form.Children.Add(organization); }
        else connect.Text = "Sign in with OpenAI";
        form.Children.Add(status); form.Children.Add(connect); form.Children.Add(cancel);
        connect.Clicked += async (_, _) => await ConnectAsync();
        Content = new ScrollView { Content = form };
    }

    async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(nickname.Text)) { status.Text = "Enter an account nickname."; return; }
        connect.IsEnabled = false;
        try
        {
            if (codex)
            {
                if (signingIn) { await FinishCodexAsync(); return; }
                connection = await AppServices.Current.Monitor.OpenCodexAsync(id, lifetime.Token);
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
                if (string.IsNullOrWhiteSpace(token.Text)) { status.Text = "Enter a GitHub token. Do not paste it into chat."; return; }
                decimal? limit = null;
                if (!string.IsNullOrWhiteSpace(allowance.Text))
                {
                    if (!decimal.TryParse(allowance.Text, out var parsed) || parsed <= 0) { status.Text = "Allowance must be a positive number, or blank."; return; }
                    limit = parsed;
                }
                status.Text = "Checking GitHub identity and billing access…";
                var result = await AppServices.Current.Monitor.ConnectGitHubAsync(nickname.Text.Trim(), token.Text.Trim(), limit,
                    unit.SelectedIndex == 1 ? UsageUnit.Requests : UsageUnit.Credits, organization.Text, lifetime.Token);
                token.Text = ""; await connected(result.Account, result.Snapshot); await CloseAsync();
            }
        }
        catch (Exception error) { status.Text = SafeMessage(error); }
        finally { connect.IsEnabled = true; }
    }
    bool finishing;
    async Task FinishCodexAsync()
    {
        if (connection is null || finishing) return; finishing = true;
        try
        {
            var result = await AppServices.Current.Monitor.CompleteCodexAsync(connection, id, nickname.Text.Trim(), lifetime.Token);
            await connected(result.Account, result.Snapshot); await CloseAsync();
        }
        catch (Exception error) { status.Text = SafeMessage(error); }
        finally { finishing = false; }
    }
    async Task CloseAsync()
    {
        await lifetime.CancelAsync(); token.Text = "";
        if (connection is not null)
        {
            try { if (loginId is not null) await connection.CallAsync("account/login/cancel", new { loginId }); } catch { }
            await connection.DisposeAsync(); connection = null;
        }
        await Navigation.PopModalAsync();
    }
    public static string SafeMessage(Exception error) => error switch {
        ProviderAccessException or InvalidDataException or FileNotFoundException => error.Message,
        OperationCanceledException => "The connection timed out or was canceled. Try again.",
        _ => "Could not complete the connection. Check your connection and provider sign-in, then retry."
    };
}
