namespace AIResetRadar.Core;
public sealed record RadarSettings(bool Notifications = true, bool Sound = false, bool Privacy = false,
    bool QuietHours = false, int QuietStartHour = 22, int QuietEndHour = 8, decimal LowThreshold = 30,
    decimal CriticalThreshold = 10, bool LaunchAtSignIn = false, bool CloseToTray = true);
