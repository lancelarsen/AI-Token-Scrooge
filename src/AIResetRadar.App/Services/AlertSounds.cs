using Windows.Media.Core;
using Windows.Media.Playback;
using AIResetRadar.Core;
namespace AIResetRadar.App.Services;

internal static class AlertSounds
{
    static readonly MediaPlayer player = new() { Volume = 0.65 };
    public static string? Play(RadarSettings settings, string action, bool preview = false)
    {
        if (!preview && !settings.Sound) return null;
        if (action is not ("alert" or "snooze" or "dismiss" or "info")) return null;
        try
        {
            var theme = settings.SoundTheme == "Classic" ? "classic" : "duck";
            var path = Path.Combine(AppContext.BaseDirectory, "Sounds", $"{theme}-{action}.wav");
            if (!File.Exists(path)) return "Sound File Unavailable";
            player.Pause();
            var previous = player.Source;
            player.Source = MediaSource.CreateFromUri(new Uri(path));
            (previous as IDisposable)?.Dispose();
            player.Play();
            return null;
        }
        catch { return "Sound Playback Unavailable"; }
    }
}
