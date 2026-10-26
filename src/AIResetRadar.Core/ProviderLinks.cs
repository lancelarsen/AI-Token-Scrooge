namespace AIResetRadar.Core;
public static class ProviderLinks
{
    public static Uri? Usage(ProviderAccount account) => account.Provider switch
    {
        "Claude" => new("https://claude.ai/new#settings/usage"),
        "OpenAI" => new("https://chatgpt.com/settings/usage?tab=overview"),
        "GitHub Copilot" when account.Scope.StartsWith("organization:", StringComparison.OrdinalIgnoreCase) =>
            new($"https://github.com/organizations/{Uri.EscapeDataString(account.Scope[13..])}/settings/billing"),
        "GitHub Copilot" => new("https://github.com/settings/billing"),
        _ => null
    };
    public static Uri? Help(string provider) => provider switch
    {
        "Claude" => new("https://support.claude.com/en/articles/11145838-use-claude-code-with-your-pro-or-max-plan"),
        "OpenAI" => new("https://help.openai.com/en/articles/11369540-using-codex-with-your-chatgpt-plan"),
        "GitHub Copilot" => new("https://docs.github.com/en/rest/billing/usage"),
        _ => null
    };
}
