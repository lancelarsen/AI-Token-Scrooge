namespace AIResetRadar.Core;

public static class PlanLabels
{
    public static string Display(string provider, string? plan) => (provider, plan) switch
    {
        ("Claude", "pro") => "Pro",
        ("Claude", "max") => "Max",
        ("Claude", "team") => "Team",
        ("Claude", "enterprise") => "Enterprise",
        ("OpenAI", "free") => "Free",
        ("OpenAI", "go") => "Go",
        ("OpenAI", "plus") => "Plus",
        ("OpenAI", "pro") => "Pro",
        ("OpenAI", "prolite") => "Pro Lite",
        ("OpenAI", "promax") => "Pro Max",
        ("OpenAI", "team") => "Team",
        ("OpenAI", "business") => "Business",
        ("OpenAI", "enterprise") => "Enterprise",
        ("OpenAI", "edu") => "Edu",
        ("GitHub Copilot", "individual") => "Pro",
        ("GitHub Copilot", "individual_pro") => "Pro+",
        ("GitHub Copilot", "individual_max") => "Max",
        ("GitHub Copilot", "individual_edu") => "Education",
        ("GitHub Copilot", "business") => "Business",
        ("GitHub Copilot", "enterprise") => "Enterprise",
        _ => "Plan unavailable"
    };
}
