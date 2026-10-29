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
        if (action is not ("alert" or "snooze" or "dismiss" or "info" or "refresh")) return null;
        try
        {
            var theme = settings.SoundTheme == "Classic" ? "classic" : "duck";
            // Refresh uses the single-quack cue, rather than Show Info's double quack.
            var cue = action == "refresh" ? "dismiss" : action;
            var path = Path.Combine(AppContext.BaseDirectory, "Sounds", $"{theme}-{cue}.wav");
            if (!File.Exists(path)) return "Sound File Unavailable";
            player.Pause();
            player.Volume = double.IsFinite(settings.SoundVolume) ? Math.Clamp(settings.SoundVolume, 0, 100) / 100 : 0.65;
            var previous = player.Source;
            player.Source = MediaSource.CreateFromUri(new Uri(path));
            (previous as IDisposable)?.Dispose();
            player.Play();
            return null;
        }
        catch { return "Sound Playback Unavailable"; }
    }
}
