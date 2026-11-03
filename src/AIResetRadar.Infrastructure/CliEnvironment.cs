namespace AIResetRadar.Infrastructure;

internal static class CliEnvironment
{
    static readonly string[] ProviderPrefixes = ["OPENAI_", "CODEX_", "ANTHROPIC_", "CLAUDE_", "GITHUB_", "GH_", "AZURE_OPENAI_", "GEMINI_"];
    static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "GOOGLE_API_KEY", "GOOGLE_APPLICATION_CREDENTIALS", "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY",
        "AWS_SESSION_TOKEN", "AWS_PROFILE", "AWS_SHARED_CREDENTIALS_FILE", "AWS_CONFIG_FILE",
        "NODE_OPTIONS", "NODE_PATH", "BUN_OPTIONS"
    };

    internal static void Sanitize(IDictionary<string, string?> environment)
    {
        foreach (var key in environment.Keys.Where(key => Excluded.Contains(key) ||
            ProviderPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))).ToArray())
            environment.Remove(key);
    }
}
