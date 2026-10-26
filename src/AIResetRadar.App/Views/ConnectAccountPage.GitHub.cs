using AIResetRadar.App.Services;
using AIResetRadar.Core;
using AIResetRadar.Infrastructure;
namespace AIResetRadar.App.Views;

public sealed partial class ConnectAccountPage
{
    readonly Button githubMode = new() { Text = "Use an access token instead" };
    readonly Label githubCode = new() { FontSize = 28, FontFamily = "Segoe UI Semibold", TextColor = Color.FromArgb("#B5A0F4"), HorizontalTextAlignment = TextAlignment.Center };
    readonly VerticalStackLayout githubPrompt = new() { Spacing = 10, IsVisible = false };
    GitHubDeviceChallenge? githubChallenge;
    bool useGitHubToken;

    void ConfigureGitHubForm(VerticalStackLayout form)
    {
        form.Children.Add(unit); form.Children.Add(allowance);
        githubMode.Clicked += (_, _) => SetGitHubMode(!useGitHubToken);
        form.Children.Add(githubMode); form.Children.Add(token); form.Children.Add(organization);
        githubPrompt.Children.Add(new Label { Text = "Enter this code on GitHub, then approve AI Reset Radar.", HorizontalTextAlignment = TextAlignment.Center });
        githubPrompt.Children.Add(githubCode);
        SemanticProperties.SetDescription(githubCode, "GitHub sign-in code. Enter this code only on github.com.");
        var copy = new Button { Text = "Copy code" };
        copy.Clicked += async (_, _) => { if (githubChallenge is { } challenge) await Clipboard.Default.SetTextAsync(challenge.UserCode); };
        var open = new Button { Text = "Open GitHub" }; open.Clicked += async (_, _) => await OpenGitHubAsync();
        githubPrompt.Children.Add(copy); githubPrompt.Children.Add(open); form.Children.Add(githubPrompt);
        var organizationReconnect = reconnect?.Scope.StartsWith("organization:", StringComparison.Ordinal) == true;
        githubMode.IsVisible = !organizationReconnect;
        SetGitHubMode(organizationReconnect);
    }

    void SetGitHubMode(bool useToken)
    {
        useGitHubToken = useToken; token.Text = ""; token.IsVisible = organization.IsVisible = useToken;
        githubMode.Text = useToken ? "Use browser sign-in instead" : "Use an access token instead";
        connect.Text = useToken ? "Connect account" : "Sign in with GitHub";
        explanation.Text = useToken ?
            "Advanced connection: use a fine-grained token with Plan read permission for personal billing. Organization billing requires authorized administration access. Your token stays protected on this PC." :
            "Sign in through GitHub in your browser. Radar reads your personal Copilot usage and protects this account's sign-in on this PC.";
        status.Text = !useToken && AppServices.Current.GitHubClientId is null ? "Browser sign-in is awaiting the one-time GitHub app registration for this preview." : "";
    }

    async Task OpenGitHubAsync()
    {
        if (githubChallenge is not { } challenge) return;
        try { await Launcher.Default.OpenAsync(challenge.VerificationUri); }
        catch { status.Text = "Open github.com/login/device in your browser and enter the code shown here."; }
    }

    async Task ConnectGitHubAsync()
    {
        decimal? limit = null;
        if (!string.IsNullOrWhiteSpace(allowance.Text))
        {
            if (!decimal.TryParse(allowance.Text, out var parsed) || parsed <= 0) { status.Text = "Allowance must be a positive number, or blank."; return; }
            limit = parsed;
        }
        if (useGitHubToken && string.IsNullOrWhiteSpace(token.Text)) { status.Text = "Enter the access token in this protected field."; return; }
        if (!useGitHubToken && AppServices.Current.GitHubClientId is null) { status.Text = "Browser sign-in needs the one-time GitHub app registration completed by Radar's developer."; return; }
        if (pendingAccount is not null) { await AppServices.Current.Monitor.DisconnectAsync(pendingAccount); pendingAccount = null; }
        nickname.IsEnabled = allowance.IsEnabled = unit.IsEnabled = organization.IsEnabled = token.IsEnabled = githubMode.IsEnabled = false;
        try
        {
            var monitor = AppServices.Current.Monitor;
            var selectedUnit = unit.SelectedIndex == 1 ? UsageUnit.Requests : UsageUnit.Credits;
            (ProviderAccount Account, QuotaSnapshot Snapshot) result;
            if (useGitHubToken)
                result = await monitor.ConnectGitHubAsync(nickname.Text.Trim(), token.Text.Trim(), limit, selectedUnit, organization.Text, lifetime.Token, reconnect);
            else
            {
                status.Text = "Opening GitHub sign-in…";
                var challenge = await AppServices.Current.GitHubSignIn.BeginAsync(AppServices.Current.GitHubClientId!, lifetime.Token);
                lifetime.Token.ThrowIfCancellationRequested(); githubChallenge = challenge;
                githubCode.Text = challenge.UserCode; githubPrompt.IsVisible = true;
                status.Text = "Complete sign-in in your browser. Radar will connect when you approve access.";
                await OpenGitHubAsync();
                var credentials = await AppServices.Current.GitHubSignIn.WaitAsync(challenge, lifetime.Token);
                status.Text = "Checking GitHub identity and billing access…";
                result = await monitor.ConnectGitHubWithBrowserAsync(nickname.Text.Trim(), credentials, limit, selectedUnit, lifetime.Token, reconnect);
            }
            pendingAccount = result.Account; token.Text = "";
            if (lifetime.IsCancellationRequested) { await monitor.DisconnectAsync(result.Account); pendingAccount = null; return; }
            committing = true; await connected(result.Account, result.Snapshot); committed = true; committing = false; await CloseAsync();
        }
        finally
        {
            githubPrompt.IsVisible = false; githubChallenge = null; githubCode.Text = "";
            nickname.IsEnabled = allowance.IsEnabled = organization.IsEnabled = token.IsEnabled = githubMode.IsEnabled = true;
            unit.IsEnabled = reconnect is null;
        }
    }
}
