using AIResetRadar.App.Controls;
using AIResetRadar.App.Services;
using AIResetRadar.Core;
namespace AIResetRadar.App.Views;

public sealed class ConnectBrowserAccountPage : ContentPage
{
    readonly CancellationTokenSource lifetime = new();

    bool busy, committed, committing;
    readonly Entry nickname = new() { Text = "Personal", Placeholder = "Account nickname (Personal, Work…)" };
    readonly Label status = new() { TextColor = Color.FromArgb("#B6B8C8") };
    public ConnectBrowserAccountPage(string provider, Func<ProviderAccount, QuotaSnapshot, Task> connected, ProviderAccount? reconnect = null)
    {
        var devin = provider == "Devin";
        Title = $"Connect {provider}"; BackgroundColor = Color.FromArgb("#141519");
        nickname.Text = reconnect?.Nickname ?? (devin ? "Work" : "Personal");
        var workspace = new Entry { Placeholder = "Company Devin Workspace URL", Text = reconnect?.Scope, IsReadOnly = reconnect is not null, IsVisible = devin };
        var signIn = new ActionButton { Text = devin ? "Sign In with Devin" : "Sign in with Claude" };
        var cancel = new ActionButton { Text = "Cancel" };
        cancel.Clicked += async (_, _) => { if (committed || committing) return; await lifetime.CancelAsync(); await AppPanels.CloseAsync(this); };
        signIn.Clicked += async (_, _) =>
        {
            if (busy) return;
            if (string.IsNullOrWhiteSpace(nickname.Text)) { status.Text = "Enter an account nickname."; return; }
            var credential = Guid.NewGuid().ToString("N");
            busy = true; signIn.IsEnabled = false;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromMinutes(devin ? 10 : 5));
            ProviderAccount? pending = null;
            try
            {
                status.Text = $"Complete {provider} sign-in in your browser. Scrooge will check the account and usage when you finish.";
                var result = devin
                    ? await AppServices.Current.Monitor.Devin.ConnectAsync(reconnect?.Id ?? Guid.NewGuid().ToString("N"), credential, nickname.Text.Trim(), workspace.Text ?? "", timeout.Token)
                    : await AppServices.Current.Monitor.Claude.ConnectAsync(reconnect?.Id ?? Guid.NewGuid().ToString("N"), credential, nickname.Text.Trim(), timeout.Token);
                pending = result.Account;
                var accepted = reconnect is null ? result.Account : ReconnectPolicy.Accept(reconnect, result.Account);
                timeout.Token.ThrowIfCancellationRequested();
                cancel.IsEnabled = false;
                committing = true;
                await connected(accepted, result.Snapshot); committed = true; committing = false;
                await AppPanels.CloseAsync(this);
            }
            catch (Exception error) { status.Text = ConnectAccountPage.SafeMessage(error); }
            finally
            {
                if (!committed)
                {
                    pending ??= new ProviderAccount(credential, provider, "", "", "", CredentialId: credential, ProfileLocation: CredentialProfileLocation.LocalSecurityDirectory);
                    try { using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(8)); await AppServices.Current.Monitor.DisconnectAsync(pending, cleanup.Token); } catch { }
                }
                committing = false; busy = false; signIn.IsEnabled = true; cancel.IsEnabled = true;
            }
        };
        Content = new ScrollView { Content = new VerticalStackLayout
        {
            Padding = 32, Spacing = 18, MaximumWidthRequest = 640, HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = Title, FontSize = 26, FontFamily = "Segoe UI Semibold" },
                new Label { Text = devin ? "Paste your usual Devin Enterprise workspace URL and sign in. Scrooge reads your personal monthly ACUs and reset time from My Analytics." : "Sign in through Claude in your browser. Scrooge uses a separate Claude Code profile to read your session and weekly limits. Requires Claude Code 2.1.310 or later.", TextColor = Color.FromArgb("#B6B8C8") },
                nickname, workspace, status, signIn, cancel
            }
        } };
    }
    protected override bool OnBackButtonPressed() { if (!committed && !committing) { lifetime.Cancel(); _ = AppPanels.CloseAsync(this); } return true; }
    protected override void OnHandlerChanging(HandlerChangingEventArgs args) { if (args.NewHandler is null) lifetime.Cancel(); base.OnHandlerChanging(args); }
}
