using AIResetRadar.Core;
namespace AIResetRadar.App.Services;
public static class ProviderNavigation
{
    public static async Task<bool> OpenAsync(ProviderAccount account, bool help = false)
    {
        var uri = help ? ProviderLinks.Help(account.Provider) : ProviderLinks.Usage(account);
        if (uri is null) return false;
        try { return await Launcher.Default.OpenAsync(uri); } catch { return false; }
    }
}
