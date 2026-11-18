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
        var billing = provider == "OpenAI API";
        Title = $"Connect {provider}"; BackgroundColor = Color.FromArgb("#141519");
        nickname.Text = reconnect?.Nickname ?? (devin ? "Work" : "Personal");
        var workspace = new Entry { Placeholder = "Company Devin Workspace URL", Text = reconnect?.Scope, IsReadOnly = reconnect is not null, IsVisible = devin };
        var signIn = new ActionButton { Text = billing ? "Connect with Chrome" : devin ? "Sign In with Devin" : "Sign in with Claude" };
        var cancel = new ActionButton { Text = "Cancel" };
        var setup = new VerticalStackLayout { IsVisible = false, Spacing = 12 };
        var help = new ActionButton { Text = "Install the Chrome Extension", IsVisible = billing };
        help.Clicked += (_, _) => setup.IsVisible = !setup.IsVisible;
        var copyFolder = new ActionButton { Text = "Copy Extension Folder Path" };
        copyFolder.Clicked += async (_, _) =>
        {
            try
            {
                await Clipboard.Default.SetTextAsync(Path.Combine(AppContext.BaseDirectory, "ChromeExtension"));
                status.Text = "Folder path copied. In Chrome, choose Load unpacked, paste this path into the folder picker, and choose Select Folder.";
            }
            catch { status.Text = "Could not copy the extension folder path. Try again."; }
        };
        setup.Children.Add(new Label { Text = "Chrome Extension Setup", FontSize = 21 });
        setup.Children.Add(new Label { Text = "This preview uses an unpacked extension, so Chrome asks you to install it manually.\n\n1. In Chrome's address bar, enter chrome://extensions and turn on Developer mode.\n\n2. Click Copy Extension Folder Path below. In Chrome, click Load unpacked, paste the path into the folder picker, and click Select Folder.\n\n3. Click Chrome's puzzle-piece Extensions button and pin Scrooge API Credits. Look for the purple top-hat icon beside the address bar.\n\nKeep the extension folder in place. If Scrooge API Credits is already installed, use its Reload button to pick up an update instead of adding it again.", TextColor = Color.FromArgb("#B6B8C8") });
        setup.Children.Add(copyFolder);
        setup.Children.Add(new Label { Text = "The Windows bridge must also be installed for this preview. The setup guide includes that step. Once both are installed, return here and choose Connect with Chrome.", TextColor = Color.FromArgb("#B6B8C8") });
        var guide = new ActionButton { Text = "Open Full Setup Guide" };
        guide.Clicked += async (_, _) =>
        {
            try { await Launcher.Default.OpenAsync("https://github.com/lancelarsen/AI-Token-Scrooge/blob/codex/openai-api-credit-pool/docs/chrome-api-credits.md"); }
            catch { status.Text = "Could not open the setup guide. Check your default browser and try again."; }
        };
        setup.Children.Add(guide);
        cancel.Clicked += async (_, _) => { if (committed || committing) return; await lifetime.CancelAsync(); await AppPanels.CloseAsync(this); };
        signIn.Clicked += async (_, _) =>
        {
            if (busy) return;
            if (string.IsNullOrWhiteSpace(nickname.Text)) { status.Text = "Enter an account nickname."; return; }
            var credential = Guid.NewGuid().ToString("N");
            busy = true; signIn.IsEnabled = false;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromMinutes(devin || billing ? 10 : 5));
            ProviderAccount? pending = null;
            try
            {
                status.Text = billing ? "Finish in Chrome\n\n1. On the billing page, check that the correct organization is selected.\n\n2. Click the Scrooge top-hat icon beside Chrome's address bar. If it is hidden, click the puzzle-piece Extensions button and choose Scrooge API Credits.\n\n3. In the extension popup, click Track This Organization.\n\nLeave this panel open. It closes automatically when your balance is added. Cancel stops the connection."
                    : $"Complete {provider} sign-in in your browser. Scrooge will check the account and usage when you finish.";
                var result = billing
                    ? await AppServices.Current.Monitor.Billing.ConnectAsync(reconnect?.Id ?? Guid.NewGuid().ToString("N"), credential, nickname.Text.Trim(), timeout.Token)
                    : devin
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
                new Label { Text = billing ? "Connect with Chrome opens your OpenAI billing page. Then click the Scrooge top-hat icon in Chrome's toolbar and choose Track This Organization to finish.\n\nRequires the Scrooge API Credits extension and Windows bridge. Your login stays in Chrome. Scrooge tracks the full organization credit pool; keep Chrome open for automatic refreshes." : devin ? "Paste your usual Devin Enterprise workspace URL and sign in. Scrooge reads your personal monthly ACUs and reset time from My Analytics." : "Sign in through Claude in your browser. Scrooge uses a separate Claude Code profile to read your session and weekly limits. Requires Claude Code 2.1.310 or later.", TextColor = Color.FromArgb("#B6B8C8") },
                nickname, workspace, help, setup, status, signIn, cancel
            }
        } };
    }
    protected override bool OnBackButtonPressed() { if (!committed && !committing) { lifetime.Cancel(); _ = AppPanels.CloseAsync(this); } return true; }
    protected override void OnHandlerChanging(HandlerChangingEventArgs args) { if (args.NewHandler is null) lifetime.Cancel(); base.OnHandlerChanging(args); }
}
