namespace AIResetRadar.Core;
public static class ProviderLinks
{
    public static Uri? Usage(ProviderAccount account) => account.Provider switch
    {
        "OpenAI" => new("https://chatgpt.com/codex/settings/usage"),
        "GitHub Copilot" when account.Scope.StartsWith("organization:", StringComparison.OrdinalIgnoreCase) =>
            new($"https://github.com/organizations/{Uri.EscapeDataString(account.Scope[13..])}/settings/billing"),
        "GitHub Copilot" => new("https://github.com/settings/billing"),
        _ => null
    };
    public static Uri? Help(string provider) => provider switch
    {
        "OpenAI" => new("https://help.openai.com/en/articles/11369540-using-codex-with-your-chatgpt-plan"),
        "GitHub Copilot" => new("https://docs.github.com/en/rest/billing/usage"),
        _ => null
    };
}
