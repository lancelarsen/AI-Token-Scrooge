using AIResetRadar.App.Controls;
using AIResetRadar.App.Services;
using AIResetRadar.Core;
namespace AIResetRadar.App.Views;

public sealed class ConnectClaudePage : ContentPage
{
    readonly CancellationTokenSource lifetime = new();
    readonly string credential = Guid.NewGuid().ToString("N");
    bool busy, committed, committing;
    readonly Entry nickname = new() { Text = "Personal", Placeholder = "Account nickname (Personal, Work…)" };
    readonly Label status = new() { TextColor = Color.FromArgb("#B6B8C8") };
    public ConnectClaudePage(Func<ProviderAccount, QuotaSnapshot, Task> connected, ProviderAccount? reconnect = null)
    {
        Title = "Connect Claude"; BackgroundColor = Color.FromArgb("#141519");
        nickname.Text = reconnect?.Nickname ?? "Personal";
        var signIn = new ActionButton { Text = "Sign in with Claude" };
        var cancel = new ActionButton { Text = "Cancel" };
        cancel.Clicked += async (_, _) => { if (committed || committing) return; await lifetime.CancelAsync(); await Navigation.PopModalAsync(); };
        signIn.Clicked += async (_, _) =>
        {
            if (busy) return;
            if (string.IsNullOrWhiteSpace(nickname.Text)) { status.Text = "Enter an account nickname."; return; }
            busy = true; signIn.IsEnabled = false;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromMinutes(5));
            ProviderAccount? pending = null;
            try
            {
                status.Text = "Complete Claude sign-in in your browser. Scrooge will check the account and usage when you finish.";
                var result = await AppServices.Current.Monitor.Claude.ConnectAsync(reconnect?.Id ?? Guid.NewGuid().ToString("N"), credential, nickname.Text.Trim(), timeout.Token);
                pending = result.Account;
                var accepted = reconnect is null ? result.Account : ReconnectPolicy.Accept(reconnect, result.Account);
                timeout.Token.ThrowIfCancellationRequested();
                cancel.IsEnabled = false;
                committing = true;
                await connected(accepted, result.Snapshot); committed = true; committing = false;
                await Navigation.PopModalAsync();
            }
            catch (Exception error) { status.Text = ConnectAccountPage.SafeMessage(error); }
            finally
            {
                if (!committed)
                {
                    pending ??= new ProviderAccount(credential, "Claude", "", "", "", CredentialId: credential);
                    try { using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(8)); await AppServices.Current.Monitor.Claude.DisconnectAsync(pending, cleanup.Token); } catch { }
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
                new Label { Text = "Sign in through Claude in your browser. Scrooge uses a separate Claude Code profile to read your session and weekly limits. Requires Claude Code 2.1.310 or later.", TextColor = Color.FromArgb("#B6B8C8") },
                nickname, status, signIn, cancel
            }
        } };
    }
    protected override bool OnBackButtonPressed() { if (!committed && !committing) { lifetime.Cancel(); _ = Navigation.PopModalAsync(); } return true; }
    protected override void OnHandlerChanging(HandlerChangingEventArgs args) { if (args.NewHandler is null) lifetime.Cancel(); base.OnHandlerChanging(args); }
}
